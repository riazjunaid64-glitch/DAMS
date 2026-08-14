using DAMS.Application.Common;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.DTOs.WhtDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public sealed class VendorService : IVendorService
    {
        private readonly AppDbContext _context;

        public VendorService(AppDbContext context) => _context = context;

        public async Task<PagedResult<VendorDto>> GetPageAsync(
            string? search, bool activeOnly, int skip, int take, CancellationToken cancellationToken = default)
        {
            var query = _context.Vendors.AsNoTracking().AsQueryable();
            if (activeOnly) query = query.Where(v => v.IsActive);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(v => v.Name.Contains(term)
                    || (v.Ntn != null && v.Ntn.Contains(term))
                    || (v.Cnic != null && v.Cnic.Contains(term))
                    || (v.Phone != null && v.Phone.Contains(term)));
            }

            var (yearStart, yearEnd) = await CurrentYearWindowAsync(cancellationToken);
            var rows = await Project(query.OrderByDescending(v => v.IsActive).ThenBy(v => v.Name), yearStart, yearEnd)
                .Skip(skip).Take(take + 1).ToListAsync(cancellationToken);
            return new PagedResult<VendorDto> { Items = rows.Take(take).ToList(), HasMore = rows.Count > take };
        }

        public Task<List<VendorOptionDto>> GetOptionsAsync(bool includeInactive, CancellationToken cancellationToken = default) =>
            _context.Vendors.AsNoTracking()
                .Where(v => includeInactive || v.IsActive)
                .OrderByDescending(v => v.IsActive).ThenBy(v => v.Name)
                .Select(v => new VendorOptionDto
                {
                    Id = v.Id, Name = v.Name, FilerStatus = v.FilerStatus, Ntn = v.Ntn, IsActive = v.IsActive
                })
                .ToListAsync(cancellationToken);

        public async Task<VendorDto> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            var (yearStart, yearEnd) = await CurrentYearWindowAsync(cancellationToken);
            return await Project(_context.Vendors.AsNoTracking().Where(v => v.Id == id), yearStart, yearEnd)
                    .SingleOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("Vendor not found.");
        }

        public async Task<VendorDto> CreateAsync(SaveVendorDto dto, int? adminUserId, CancellationToken cancellationToken = default)
        {
            Validate(dto);
            await EnsureUniqueAsync(dto, null, cancellationToken);

            var vendor = new Vendor
            {
                Name = dto.Name.Trim(),
                Ntn = Clean(dto.Ntn),
                Cnic = Clean(dto.Cnic),
                Phone = Clean(dto.Phone),
                Address = Clean(dto.Address),
                Notes = Clean(dto.Notes),
                FilerStatus = dto.FilerStatus,
                // A status is only "checked" once someone says they looked it up; defaulting to
                // "now" would make an unverified guess look like a verified ATL lookup.
                FilerStatusCheckedAt = dto.MarkFilerStatusChecked ? DateTime.UtcNow : null,
                IsActive = dto.IsActive,
                CreatedByUserId = adminUserId,
                CreatedAt = DateTime.UtcNow
            };
            _context.Vendors.Add(vendor);
            await _context.SaveChangesAsync(cancellationToken);
            await AdoptFreeTextHistoryAsync(vendor.Id, vendor.Name, cancellationToken);
            return await GetByIdAsync(vendor.Id, cancellationToken);
        }

        public async Task<VendorDto> UpdateAsync(int id, SaveVendorDto dto, CancellationToken cancellationToken = default)
        {
            Validate(dto);
            var vendor = await _context.Vendors.SingleOrDefaultAsync(v => v.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("Vendor not found.");
            ApplyConcurrencyToken(vendor, dto.ConcurrencyToken);
            await EnsureUniqueAsync(dto, id, cancellationToken);

            // Held so that a rename can still claim what is filed under the old name — otherwise it
            // walks away from that history and the vendor's annual total silently restarts.
            var previousName = vendor.Name;

            // Changing the filer status only affects expenses entered from here on: each expense
            // snapshots the status it was withheld under.
            vendor.Name = dto.Name.Trim();
            vendor.Ntn = Clean(dto.Ntn);
            vendor.Cnic = Clean(dto.Cnic);
            vendor.Phone = Clean(dto.Phone);
            vendor.Address = Clean(dto.Address);
            vendor.Notes = Clean(dto.Notes);
            vendor.FilerStatus = dto.FilerStatus;
            if (dto.MarkFilerStatusChecked) vendor.FilerStatusCheckedAt = DateTime.UtcNow;
            vendor.IsActive = dto.IsActive;
            vendor.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);
            if (vendor.Name != previousName)
                await AdoptFreeTextHistoryAsync(vendor.Id, previousName, cancellationToken);
            return await GetByIdAsync(id, cancellationToken);
        }

        public async Task<List<VendorYtdLineDto>> GetYearToDateAsync(
            int vendorId, DateTime? asOf, CancellationToken cancellationToken = default)
        {
            if (!await _context.Vendors.AnyAsync(v => v.Id == vendorId, cancellationToken))
                throw new InvalidOperationException("Vendor not found.");

            var startMonth = await FinanceSettingsQuery.FinancialYearStartMonthAsync(_context, cancellationToken);
            var reference = (asOf ?? PakistanTime.Today).Date;
            var (start, end) = FinancialYear.Window(reference, startMonth);
            var label = FinancialYear.Label(reference, startMonth);

            // Grouped by section, because that is the unit the statutory threshold applies to.
            // Rows with no section (heads that carry no withholding) collapse into one line.
            // Asset purchases are counted alongside expenses so this panel shows the same total the
            // threshold is actually decided on — a breakdown that omitted them would explain a
            // deduction with a figure that did not justify it.
            var expenseGroups = await _context.Expenses.AsNoTracking()
                .Where(e => e.VendorId == vendorId && e.Date >= start && e.Date < end)
                .GroupBy(e => e.WhtTaxSection)
                .Select(g => new SectionYtd(
                    g.Key,
                    g.Sum(e => (decimal?)e.Amount) ?? 0m,
                    g.Sum(e => (decimal?)e.WhtAmount) ?? 0m,
                    g.Count()))
                .ToListAsync(cancellationToken);
            var purchaseGroups = await _context.AssetPurchases.AsNoTracking()
                .Where(p => p.VendorId == vendorId && p.Date >= start && p.Date < end)
                .GroupBy(p => p.WhtTaxSection)
                .Select(g => new SectionYtd(
                    g.Key,
                    g.Sum(p => (decimal?)p.Amount) ?? 0m,
                    g.Sum(p => (decimal?)p.WhtAmount) ?? 0m,
                    g.Count()))
                .ToListAsync(cancellationToken);

            return expenseGroups.Concat(purchaseGroups)
                .GroupBy(g => g.Section ?? string.Empty, StringComparer.Ordinal)
                .Select(g => new VendorYtdLineDto
                {
                    Scope = string.IsNullOrWhiteSpace(g.Key) ? "No withholding section" : $"Section {g.Key}",
                    FinancialYear = label,
                    GrossPaid = g.Sum(x => x.Gross),
                    WhtWithheld = g.Sum(x => x.Wht),
                    ExpenseCount = g.Sum(x => x.Count)
                })
                .OrderByDescending(g => g.GrossPaid)
                .ToList();
        }

        /// <summary>
        /// Claims the unlinked expense rows that were already this vendor's money.
        /// <para>
        /// Before a supplier has a record, their payments carry the typed name and no link, and
        /// those payments still count towards the vendor's annual threshold. Matching on the name
        /// at query time is enough right up until someone corrects the business name — at which
        /// point the whole history silently detaches and the allowance restarts. Writing the id
        /// once makes the link permanent, and it is the only field touched: the name on each
        /// expense stays the one that was on the payment.
        /// </para>
        /// <para>
        /// Fixed-asset purchases are claimed on the same terms. They feed the same annual allowance,
        /// so leaving them behind would detach exactly the history the allowance is computed from.
        /// </para>
        /// </summary>
        private async Task AdoptFreeTextHistoryAsync(int vendorId, string name, CancellationToken cancellationToken)
        {
            var orphanExpenses = await _context.Expenses
                .Where(e => e.VendorId == null && e.Vendor == name)
                .ToListAsync(cancellationToken);
            var orphanPurchases = await _context.AssetPurchases
                .Where(p => p.VendorId == null && p.Vendor == name)
                .ToListAsync(cancellationToken);
            if (orphanExpenses.Count == 0 && orphanPurchases.Count == 0) return;

            foreach (var expense in orphanExpenses)
                expense.VendorId = vendorId;
            foreach (var purchase in orphanPurchases)
                purchase.VendorId = vendorId;
            await _context.SaveChangesAsync(cancellationToken);
        }

        private sealed record SectionYtd(string? Section, decimal Gross, decimal Wht, int Count);

        private async Task<(DateTime Start, DateTime End)> CurrentYearWindowAsync(CancellationToken cancellationToken)
        {
            var startMonth = await FinanceSettingsQuery.FinancialYearStartMonthAsync(_context, cancellationToken);
            return FinancialYear.Window(PakistanTime.Today, startMonth);
        }

        private static IQueryable<VendorDto> Project(IQueryable<Vendor> query, DateTime yearStart, DateTime yearEnd) =>
            query.Select(v => new VendorDto
            {
                Id = v.Id,
                Name = v.Name,
                Ntn = v.Ntn,
                Cnic = v.Cnic,
                Phone = v.Phone,
                Address = v.Address,
                Notes = v.Notes,
                FilerStatus = v.FilerStatus,
                FilerStatusCheckedAt = v.FilerStatusCheckedAt,
                IsActive = v.IsActive,
                // Asset purchases are added to both totals for the same reason the detail panel
                // adds them: the annual threshold is one allowance per supplier per section over
                // everything they were paid. Summing only expenses here would report Rs 0 for a
                // supplier who has only ever sold us equipment, and disagree with the breakdown
                // GetYearToDateAsync shows for that same vendor.
                YearToDateGross = (v.Expenses.Where(e => e.Date >= yearStart && e.Date < yearEnd)
                        .Sum(e => (decimal?)e.Amount) ?? 0m)
                    + (v.AssetPurchases.Where(p => p.Date >= yearStart && p.Date < yearEnd)
                        .Sum(p => (decimal?)p.Amount) ?? 0m),
                YearToDateWht = (v.Expenses.Where(e => e.Date >= yearStart && e.Date < yearEnd)
                        .Sum(e => (decimal?)e.WhtAmount) ?? 0m)
                    + (v.AssetPurchases.Where(p => p.Date >= yearStart && p.Date < yearEnd)
                        .Sum(p => (decimal?)p.WhtAmount) ?? 0m),
                ExpenseCount = v.Expenses.Count + v.AssetPurchases.Count,
                CreatedAt = v.CreatedAt,
                UpdatedAt = v.UpdatedAt,
                ConcurrencyToken = Convert.ToBase64String(v.RowVersion)
            });

        private async Task EnsureUniqueAsync(SaveVendorDto dto, int? excludingId, CancellationToken cancellationToken)
        {
            var name = dto.Name.Trim();
            if (await _context.Vendors.AnyAsync(
                    v => v.Name == name && (!excludingId.HasValue || v.Id != excludingId.Value), cancellationToken))
                throw new InvalidOperationException("A vendor with this name already exists.");

            var ntn = Clean(dto.Ntn);
            if (ntn != null && await _context.Vendors.AnyAsync(
                    v => v.Ntn == ntn && (!excludingId.HasValue || v.Id != excludingId.Value), cancellationToken))
                throw new InvalidOperationException("A vendor with this NTN already exists.");
        }

        private static void Validate(SaveVendorDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new InvalidOperationException("Vendor name is required.");
            if (dto.Name.Trim().Length > 200)
                throw new InvalidOperationException("Vendor name cannot exceed 200 characters.");
            if (!Enum.IsDefined(dto.FilerStatus))
                throw new InvalidOperationException("Select a valid filer status.");
            if (dto.Ntn?.Trim().Length > 30)
                throw new InvalidOperationException("NTN cannot exceed 30 characters.");
            if (dto.Cnic?.Trim().Length > 20)
                throw new InvalidOperationException("CNIC cannot exceed 20 characters.");
            if (dto.Phone?.Trim().Length > 50)
                throw new InvalidOperationException("Phone cannot exceed 50 characters.");
            if (dto.Address?.Trim().Length > 500)
                throw new InvalidOperationException("Address cannot exceed 500 characters.");
            if (dto.Notes?.Trim().Length > 1000)
                throw new InvalidOperationException("Notes cannot exceed 1000 characters.");
        }

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private void ApplyConcurrencyToken(Vendor vendor, string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("The vendor version is missing. Refresh and try again.");
            try { _context.Entry(vendor).Property(v => v.RowVersion).OriginalValue = Convert.FromBase64String(token); }
            catch (FormatException) { throw new InvalidOperationException("The vendor version is invalid. Refresh and try again."); }
        }
    }
}
