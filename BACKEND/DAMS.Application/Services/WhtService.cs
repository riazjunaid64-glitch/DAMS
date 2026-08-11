using System.Globalization;
using System.Text;
using DAMS.Application.Common;
using DAMS.Application.DTOs.WhtDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    /// <summary>
    /// Withholding tax: what to deduct, what has been deducted, and what has been handed to FBR.
    /// <para>
    /// The invariant everything here protects: an expense is a business cost at its GROSS amount,
    /// but only the NET leaves the bank. The difference is a liability, not a saving. It shows up
    /// in the account balance until a <see cref="WhtDeposit"/> records it being paid over.
    /// </para>
    /// </summary>
    public sealed class WhtService : IWhtService
    {
        private readonly AppDbContext _context;
        private readonly IFinanceAccountService _accountService;

        public WhtService(AppDbContext context, IFinanceAccountService accountService)
        {
            _context = context;
            _accountService = accountService;
        }

        // ── Calculation ─────────────────────────────────────────────────────────────

        public async Task<WhtCalculationResultDto> CalculateAsync(
            WhtCalculationRequestDto request, CancellationToken cancellationToken = default)
        {
            var date = (request.Date ?? PakistanTime.Today).Date;
            var startMonth = await FinanceSettingsQuery.FinancialYearStartMonthAsync(_context, cancellationToken);
            var yearLabel = FinancialYear.Label(date, startMonth);

            var category = request.CategoryId.HasValue
                ? await _context.ExpenseCategories.AsNoTracking()
                    .SingleOrDefaultAsync(c => c.Id == request.CategoryId.Value, cancellationToken)
                : null;

            if (category == null)
            {
                return new WhtCalculationResultDto
                {
                    IsWhtApplicable = false,
                    NetPaid = request.GrossAmount,
                    FinancialYear = yearLabel,
                    Notice = request.CategoryId.HasValue
                        ? "That expense category no longer exists. Pick another one."
                        : "Choose a managed category to work out withholding tax."
                };
            }

            var vendor = request.VendorId.HasValue
                ? await _context.Vendors.AsNoTracking()
                    .SingleOrDefaultAsync(v => v.Id == request.VendorId.Value, cancellationToken)
                : null;
            var filerStatus = vendor?.FilerStatus ?? FilerStatus.Unknown;

            var yearToDate = await YearToDateAsync(
                vendor, category, date, startMonth, request.ExcludeExpenseId, cancellationToken);

            var result = WhtCalculator.Compute(
                Rates(category, vendor != null), filerStatus, request.GrossAmount, yearToDate);

            return new WhtCalculationResultDto
            {
                IsWhtApplicable = result.IsWhtApplicable,
                Rate = result.Rate,
                WhtAmount = result.WhtAmount,
                NetPaid = result.NetPaid,
                WhtApplied = result.WhtApplied,
                FilerStatus = filerStatus,
                TaxSection = category.TaxSection,
                BelowThreshold = result.BelowThreshold,
                AnnualThreshold = result.AnnualThreshold,
                YearToDateTotal = result.YearToDateTotal,
                FinancialYear = yearLabel,
                Notice = BuildNotice(category, vendor, result, yearLabel)
            };
        }

        public async Task ApplyToExpenseAsync(
            Expense expense, decimal? requestedRate, decimal? requestedAmount, string? overrideReason,
            CancellationToken cancellationToken = default)
        {
            var category = expense.CategoryId.HasValue
                ? await _context.ExpenseCategories.AsNoTracking()
                    .SingleOrDefaultAsync(c => c.Id == expense.CategoryId.Value, cancellationToken)
                : null;

            var vendor = expense.VendorId.HasValue
                ? await _context.Vendors.AsNoTracking()
                    .SingleOrDefaultAsync(v => v.Id == expense.VendorId.Value, cancellationToken)
                : null;
            var filerStatus = vendor?.FilerStatus ?? FilerStatus.Unknown;

            if (category is not { IsWhtApplicable: true })
            {
                // Free-text head, or a head that carries no withholding. Refuse a tax figure here
                // rather than dropping it silently — a swallowed deduction is an unexplained gap
                // between the invoice and the bank.
                if (requestedAmount is > 0m || requestedRate is > 0m)
                    throw new InvalidOperationException(category == null
                        ? "Withholding tax needs a managed expense category. Pick one, or clear the tax fields."
                        : $"\"{category.Name}\" is not subject to withholding tax. Clear the tax fields to save.");
                ClearWht(expense, filerStatus, category?.TaxSection);
                return;
            }

            var startMonth = await FinanceSettingsQuery.FinancialYearStartMonthAsync(_context, cancellationToken);
            var yearToDate = await YearToDateAsync(
                vendor, category, expense.Date, startMonth,
                expense.Id == 0 ? null : expense.Id, cancellationToken);

            var suggested = WhtCalculator.Compute(
                Rates(category, vendor != null), filerStatus, expense.Amount, yearToDate);

            decimal rate;
            decimal amount;
            if (requestedAmount.HasValue)
            {
                // The amount wins over the rate: an operator typing a figure is copying it off the
                // vendor invoice, and that figure is what will be deposited.
                amount = requestedAmount.Value;
                rate = WhtCalculator.EffectiveRate(expense.Amount, amount);
            }
            else if (requestedRate.HasValue && requestedRate.Value != suggested.Rate)
            {
                rate = requestedRate.Value;
                amount = WhtCalculator.TaxOn(expense.Amount, rate);
            }
            else
            {
                // No override, or a rate echoed back unchanged — take the computed figure, which
                // is the one that respects the annual threshold.
                rate = suggested.Rate;
                amount = suggested.WhtAmount;
            }

            ValidateTax(expense.Amount, rate, amount);

            var overridden = amount != suggested.WhtAmount;
            if (overridden && string.IsNullOrWhiteSpace(overrideReason))
                throw new InvalidOperationException(
                    $"Withholding tax was changed from the calculated {suggested.WhtAmount:N2}. Give a reason for the override.");

            expense.WhtRate = rate;
            expense.WhtAmount = amount;
            expense.WhtApplied = amount > 0m;
            expense.WhtRateOverridden = overridden;
            expense.WhtOverrideReason = overridden ? overrideReason!.Trim() : null;
            // Snapshotted whether or not tax was withheld: a below-threshold payment still counts
            // towards the section's annual aggregate, so the section has to be recorded.
            expense.WhtTaxSection = category.TaxSection;
            expense.VendorFilerStatusAtEntry = filerStatus;
        }

        private static void ClearWht(Expense expense, FilerStatus filerStatus, string? section)
        {
            expense.WhtApplied = false;
            expense.WhtRate = 0m;
            expense.WhtAmount = 0m;
            expense.WhtRateOverridden = false;
            expense.WhtOverrideReason = null;
            expense.WhtTaxSection = section;
            expense.VendorFilerStatusAtEntry = filerStatus;
        }

        private static void ValidateTax(decimal grossAmount, decimal rate, decimal amount)
        {
            if (rate < 0m) throw new InvalidOperationException("Withholding rate cannot be negative.");
            if (rate > 100m) throw new InvalidOperationException("Withholding rate cannot exceed 100%.");
            if (amount < 0m) throw new InvalidOperationException("Withholding tax cannot be negative.");
            // Equal is allowed (a 100% deduction nets to zero); more than the payment would make
            // the vendor owe money for having been paid.
            if (amount > grossAmount)
                throw new InvalidOperationException("Withholding tax cannot exceed the gross amount of the expense.");
        }

        /// <summary>
        /// The rate table as the calculator sees it.
        /// <para>
        /// The annual threshold is an allowance per supplier, so it can only be applied to a payee
        /// the system can actually aggregate. Without a vendor record the allowance is withheld —
        /// not granted — because granting it to an unidentified payee would let one supplier be
        /// paid in slices that each look exempt while the year's total is far over the limit.
        /// That is the same direction of caution as treating an unknown filer as a non-filer:
        /// over-deducting is recoverable by the vendor, under-deducting is penalised.
        /// </para>
        /// </summary>
        private static WhtCalculator.CategoryRates Rates(ExpenseCategory category, bool vendorLinked) =>
            new(category.IsWhtApplicable, category.FilerRate, category.NonFilerRate,
                vendorLinked ? category.AnnualThreshold : 0m);

        /// <summary>
        /// Gross already paid to this vendor in the financial year under the same tax section.
        /// Sections, not categories: the statutory threshold is one aggregate for all goods from a
        /// supplier, not a separate allowance for cement and another for bricks. Categories with
        /// no section fall back to matching on the category itself.
        /// </summary>
        private async Task<decimal> YearToDateAsync(
            Vendor? vendor, ExpenseCategory category, DateTime date, int startMonth,
            int? excludeExpenseId, CancellationToken cancellationToken)
        {
            if (vendor == null || category.AnnualThreshold <= 0m)
                return 0m;

            var (start, end) = FinancialYear.Window(date, startMonth);
            var vendorId = vendor.Id;
            var vendorName = vendor.Name;
            var query = _context.Expenses.AsNoTracking()
                .Where(e => e.Date >= start && e.Date < end
                    // Payments made before this vendor had a record carry the same name as free
                    // text. They are the same supplier's money, so they count towards the annual
                    // aggregate — without this, the first year after a vendor is created starts
                    // its allowance again from zero and under-withholds.
                    && (e.VendorId == vendorId || (e.VendorId == null && e.Vendor == vendorName)));

            query = string.IsNullOrWhiteSpace(category.TaxSection)
                ? query.Where(e => e.CategoryId == category.Id)
                : query.Where(e => e.WhtTaxSection == category.TaxSection);

            if (excludeExpenseId.HasValue)
                query = query.Where(e => e.Id != excludeExpenseId.Value);

            return await query.SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m;
        }

        private static string? BuildNotice(
            ExpenseCategory category, Vendor? vendor, WhtCalculator.Result result, string yearLabel)
        {
            if (!result.IsWhtApplicable)
                return $"\"{category.Name}\" is not subject to withholding tax at the point of payment.";

            if (vendor == null)
                return category.AnnualThreshold > 0m
                    ? $"No vendor linked, so the {category.AnnualThreshold:N0} annual allowance cannot be tracked and tax is withheld from the first rupee, at the non-filer rate. Link a vendor to use their filer status and their allowance."
                    : "No vendor linked — the non-filer rate is applied. Link a vendor to use their filer status.";

            var status = vendor.FilerStatus switch
            {
                FilerStatus.Filer => "Filer",
                FilerStatus.NonFiler => "Non-filer",
                _ => "Filer status unknown, so the non-filer rate applies"
            };

            if (result.BelowThreshold)
                return $"{status}. {vendor.Name} has been paid {result.YearToDateTotal:N0} of the {result.AnnualThreshold:N0} annual threshold in {yearLabel} — no tax is withheld until it is crossed.";

            if (result.AnnualThreshold > 0m && result.YearToDateTotal > 0m)
                return $"{status}. The {result.AnnualThreshold:N0} annual threshold is crossed — {vendor.Name} has been paid {result.YearToDateTotal:N0} in {yearLabel}.";

            return $"{status}.";
        }

        // ── Settings ────────────────────────────────────────────────────────────────

        public async Task<FinanceSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
        {
            // Read-only on purpose: the update path can create the singleton if it is missing, but
            // a GET must never stage an insert on the shared request-scoped context, where some
            // later SaveChanges would carry it in as a side effect.
            var settings = await _context.FinanceSettings.AsNoTracking()
                .SingleOrDefaultAsync(s => s.Id == FinanceSetting.SingletonId, cancellationToken);
            return MapSettings(settings ?? new FinanceSetting());
        }

        public async Task<FinanceSettingsDto> UpdateSettingsAsync(
            SaveFinanceSettingsDto dto, string? actorName, CancellationToken cancellationToken = default)
        {
            if (dto.FinancialYearStartMonth is < 1 or > 12)
                throw new InvalidOperationException("Financial year start month must be between 1 and 12.");
            if (dto.MarkRatesConfirmed && dto.ClearRatesConfirmation)
                throw new InvalidOperationException("Choose either confirming the rates or clearing the confirmation, not both.");

            var settings = await LoadSettingsAsync(cancellationToken);
            if (_context.Entry(settings).State != EntityState.Added)
                ApplyConcurrencyToken(settings, dto.ConcurrencyToken);

            settings.FinancialYearStartMonth = dto.FinancialYearStartMonth;
            if (dto.MarkRatesConfirmed)
            {
                settings.WhtRatesConfirmedAt = DateTime.UtcNow;
                settings.WhtRatesConfirmedByName = string.IsNullOrWhiteSpace(actorName) ? null : actorName.Trim();
            }
            else if (dto.ClearRatesConfirmation)
            {
                settings.WhtRatesConfirmedAt = null;
                settings.WhtRatesConfirmedByName = null;
            }
            settings.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);
            return MapSettings(settings);
        }

        private async Task<FinanceSetting> LoadSettingsAsync(CancellationToken cancellationToken)
        {
            var settings = await _context.FinanceSettings
                .SingleOrDefaultAsync(s => s.Id == FinanceSetting.SingletonId, cancellationToken);
            if (settings != null) return settings;

            // The seed creates this row; recreating it here keeps stores that were never seeded
            // (in-memory tests, a hand-restored database) working instead of failing at read time.
            settings = new FinanceSetting { Id = FinanceSetting.SingletonId };
            _context.FinanceSettings.Add(settings);
            return settings;
        }

        private static FinanceSettingsDto MapSettings(FinanceSetting settings)
        {
            var month = FinancialYear.Normalise(settings.FinancialYearStartMonth);
            return new FinanceSettingsDto
            {
                FinancialYearStartMonth = month,
                WhtRatesConfirmedAt = settings.WhtRatesConfirmedAt,
                WhtRatesConfirmedByName = settings.WhtRatesConfirmedByName,
                CurrentFinancialYear = FinancialYear.Label(PakistanTime.Today, month),
                ConcurrencyToken = settings.RowVersion.Length == 0
                    ? string.Empty
                    : Convert.ToBase64String(settings.RowVersion)
            };
        }

        // ── Reporting ───────────────────────────────────────────────────────────────

        public async Task<WhtPayableSummaryDto> GetPayableSummaryAsync(
            DateTime? from, DateTime? to, CancellationToken cancellationToken = default)
        {
            var withheld = WithheldQuery(from, to);

            var bySection = await withheld
                .GroupBy(e => e.WhtTaxSection)
                .Select(g => new WhtSectionTotalDto
                {
                    TaxSection = g.Key ?? string.Empty,
                    GrossAmount = g.Sum(e => (decimal?)e.Amount) ?? 0m,
                    WhtAmount = g.Sum(e => (decimal?)e.WhtAmount) ?? 0m,
                    ExpenseCount = g.Count()
                })
                .ToListAsync(cancellationToken);

            var totalWithheldAllTime = await WithheldQuery(null, null)
                .SumAsync(e => (decimal?)e.WhtAmount, cancellationToken) ?? 0m;
            var totalDepositedAllTime = await DepositQuery(null, null)
                .SumAsync(d => (decimal?)d.Amount, cancellationToken) ?? 0m;

            return new WhtPayableSummaryDto
            {
                WithheldInPeriod = bySection.Sum(s => s.WhtAmount),
                DepositedInPeriod = await DepositQuery(from, to).SumAsync(d => (decimal?)d.Amount, cancellationToken) ?? 0m,
                // A liability is a balance, so it is deliberately not date-filtered: what is still
                // owed to FBR does not change because the user picked a narrower period.
                OutstandingPayable = totalWithheldAllTime - totalDepositedAllTime,
                TotalWithheldAllTime = totalWithheldAllTime,
                TotalDepositedAllTime = totalDepositedAllTime,
                ExpenseCount = bySection.Sum(s => s.ExpenseCount),
                VendorCount = await withheld.Select(e => e.VendorId).Distinct().CountAsync(cancellationToken),
                BySection = bySection
                    .Select(s => new WhtSectionTotalDto
                    {
                        TaxSection = string.IsNullOrWhiteSpace(s.TaxSection) ? "Unspecified" : s.TaxSection,
                        GrossAmount = s.GrossAmount,
                        WhtAmount = s.WhtAmount,
                        ExpenseCount = s.ExpenseCount
                    })
                    .OrderByDescending(s => s.WhtAmount)
                    .ToList()
            };
        }

        public async Task<List<WhtVendorLineDto>> GetByVendorAsync(
            DateTime? from, DateTime? to, CancellationToken cancellationToken = default)
        {
            // Grouped by vendor *and* section: a s.165 statement reports each section separately,
            // and one supplier can be paid under more than one.
            var rows = await WithheldQuery(from, to)
                .GroupBy(e => new { e.VendorId, e.WhtTaxSection })
                .Select(g => new
                {
                    g.Key.VendorId,
                    g.Key.WhtTaxSection,
                    // Free-text vendors have no record to read a name from; take the snapshot on
                    // any row in the group, which is the name that was on the payment.
                    FallbackName = g.Min(e => e.Vendor),
                    GrossAmount = g.Sum(e => (decimal?)e.Amount) ?? 0m,
                    WhtAmount = g.Sum(e => (decimal?)e.WhtAmount) ?? 0m,
                    ExpenseCount = g.Count()
                })
                .ToListAsync(cancellationToken);

            var vendorIds = rows.Where(r => r.VendorId.HasValue).Select(r => r.VendorId!.Value).Distinct().ToList();
            var vendors = await _context.Vendors.AsNoTracking()
                .Where(v => vendorIds.Contains(v.Id))
                .Select(v => new { v.Id, v.Name, v.Ntn, v.Cnic, v.FilerStatus })
                .ToDictionaryAsync(v => v.Id, cancellationToken);

            return rows
                .Select(r =>
                {
                    var vendor = r.VendorId.HasValue && vendors.TryGetValue(r.VendorId.Value, out var found) ? found : null;
                    return new WhtVendorLineDto
                    {
                        VendorId = r.VendorId,
                        VendorName = vendor?.Name ?? r.FallbackName ?? "Unnamed vendor",
                        Ntn = vendor?.Ntn,
                        Cnic = vendor?.Cnic,
                        FilerStatus = vendor?.FilerStatus ?? FilerStatus.Unknown,
                        TaxSection = string.IsNullOrWhiteSpace(r.WhtTaxSection) ? null : r.WhtTaxSection,
                        GrossAmount = r.GrossAmount,
                        WhtAmount = r.WhtAmount,
                        NetPaid = r.GrossAmount - r.WhtAmount,
                        ExpenseCount = r.ExpenseCount
                    };
                })
                .OrderByDescending(r => r.WhtAmount)
                .ThenBy(r => r.VendorName)
                .ToList();
        }

        public async Task<(string FileName, byte[] Content)> ExportAsync(
            DateTime? from, DateTime? to, CancellationToken cancellationToken = default)
        {
            var lines = await GetByVendorAsync(from, to, cancellationToken);

            var csv = new StringBuilder();
            csv.AppendLine("Vendor,NTN,CNIC,Filer status,Tax section,Payments,Gross amount,Tax withheld,Net paid");
            foreach (var line in lines)
            {
                csv.Append(Csv(line.VendorName)).Append(',')
                   .Append(Csv(line.Ntn)).Append(',')
                   .Append(Csv(line.Cnic)).Append(',')
                   .Append(Csv(line.FilerStatus.ToString())).Append(',')
                   .Append(Csv(line.TaxSection)).Append(',')
                   .Append(line.ExpenseCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                   .Append(line.GrossAmount.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                   .Append(line.WhtAmount.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                   .Append(line.NetPaid.ToString("0.00", CultureInfo.InvariantCulture))
                   .AppendLine();
            }
            csv.Append("Total,,,,,")
               .Append(lines.Sum(l => l.ExpenseCount).ToString(CultureInfo.InvariantCulture)).Append(',')
               .Append(lines.Sum(l => l.GrossAmount).ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
               .Append(lines.Sum(l => l.WhtAmount).ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
               .Append(lines.Sum(l => l.NetPaid).ToString("0.00", CultureInfo.InvariantCulture))
               .AppendLine();

            var name = $"wht-statement-{Stamp(from, "start")}-to-{Stamp(to, "today")}.csv";
            // BOM, so Excel reads it as UTF-8 and vendor names with non-ASCII characters survive.
            return (name, Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray());
        }

        private static string Stamp(DateTime? value, string fallback) =>
            value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? fallback;

        private static string Csv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            // A leading =, +, - or @ is executed as a formula by Excel; prefix it so a vendor name
            // cannot become a spreadsheet command in whoever opens the export.
            var text = "=+-@".Contains(value[0]) ? "'" + value : value;
            return text.Contains(',') || text.Contains('"') || text.Contains('\n') || text.Contains('\r')
                ? $"\"{text.Replace("\"", "\"\"")}\""
                : text;
        }

        private IQueryable<Expense> WithheldQuery(DateTime? from, DateTime? to)
        {
            var query = _context.Expenses.AsNoTracking().Where(e => e.WhtAmount > 0m);
            if (from.HasValue) query = query.Where(e => e.Date >= from.Value.Date);
            if (to.HasValue) query = query.Where(e => e.Date < to.Value.Date.AddDays(1));
            return query;
        }

        private IQueryable<WhtDeposit> DepositQuery(DateTime? from, DateTime? to)
        {
            var query = _context.WhtDeposits.AsNoTracking().AsQueryable();
            if (from.HasValue) query = query.Where(d => d.DepositDate >= from.Value.Date);
            if (to.HasValue) query = query.Where(d => d.DepositDate < to.Value.Date.AddDays(1));
            return query;
        }

        // ── FBR deposits ────────────────────────────────────────────────────────────

        public Task<List<WhtDepositDto>> GetDepositsAsync(
            DateTime? from, DateTime? to, CancellationToken cancellationToken = default) =>
            Project(DepositQuery(from, to).OrderByDescending(d => d.DepositDate).ThenByDescending(d => d.Id))
                .ToListAsync(cancellationToken);

        public async Task<WhtDepositDto> CreateDepositAsync(
            SaveWhtDepositDto dto, int? adminUserId, CancellationToken cancellationToken = default)
        {
            await ValidateDepositAsync(dto, null, cancellationToken);
            await _accountService.EnsureSelectableAsync(dto.FinanceAccountId, null, cancellationToken);

            var deposit = new WhtDeposit
            {
                FinanceAccountId = dto.FinanceAccountId,
                Amount = dto.Amount,
                DepositDate = dto.DepositDate?.Date ?? PakistanTime.Today,
                ChallanNumber = Clean(dto.ChallanNumber),
                PeriodFrom = dto.PeriodFrom?.Date,
                PeriodTo = dto.PeriodTo?.Date,
                Notes = Clean(dto.Notes),
                CreatedByUserId = adminUserId,
                CreatedAt = DateTime.UtcNow
            };
            _context.WhtDeposits.Add(deposit);
            await _context.SaveChangesAsync(cancellationToken);
            return await GetDepositAsync(deposit.Id, cancellationToken);
        }

        public async Task<WhtDepositDto> UpdateDepositAsync(
            int id, SaveWhtDepositDto dto, CancellationToken cancellationToken = default)
        {
            var deposit = await _context.WhtDeposits.SingleOrDefaultAsync(d => d.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("WHT deposit not found.");
            await ValidateDepositAsync(dto, id, cancellationToken);
            ApplyConcurrencyToken(deposit, dto.ConcurrencyToken);
            await _accountService.EnsureSelectableAsync(dto.FinanceAccountId, deposit.FinanceAccountId, cancellationToken);

            deposit.FinanceAccountId = dto.FinanceAccountId;
            deposit.Amount = dto.Amount;
            if (dto.DepositDate.HasValue) deposit.DepositDate = dto.DepositDate.Value.Date;
            deposit.ChallanNumber = Clean(dto.ChallanNumber);
            deposit.PeriodFrom = dto.PeriodFrom?.Date;
            deposit.PeriodTo = dto.PeriodTo?.Date;
            deposit.Notes = Clean(dto.Notes);

            await _context.SaveChangesAsync(cancellationToken);
            return await GetDepositAsync(id, cancellationToken);
        }

        public async Task DeleteDepositAsync(int id, CancellationToken cancellationToken = default)
        {
            var deposit = await _context.WhtDeposits.SingleOrDefaultAsync(d => d.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("WHT deposit not found.");
            _context.WhtDeposits.Remove(deposit);
            await _context.SaveChangesAsync(cancellationToken);
        }

        private async Task<WhtDepositDto> GetDepositAsync(int id, CancellationToken cancellationToken) =>
            await Project(_context.WhtDeposits.AsNoTracking().Where(d => d.Id == id))
                .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("WHT deposit not found.");

        private static IQueryable<WhtDepositDto> Project(IQueryable<WhtDeposit> query) =>
            query.Select(d => new WhtDepositDto
            {
                Id = d.Id,
                FinanceAccountId = d.FinanceAccountId,
                FinanceAccountName = d.FinanceAccount != null ? d.FinanceAccount.Name : null,
                Amount = d.Amount,
                DepositDate = d.DepositDate,
                ChallanNumber = d.ChallanNumber,
                PeriodFrom = d.PeriodFrom,
                PeriodTo = d.PeriodTo,
                Notes = d.Notes,
                CreatedAt = d.CreatedAt,
                ConcurrencyToken = Convert.ToBase64String(d.RowVersion)
            });

        private async Task ValidateDepositAsync(SaveWhtDepositDto dto, int? excludingId, CancellationToken cancellationToken)
        {
            if (dto.Amount <= 0m)
                throw new InvalidOperationException("Deposit amount must be greater than zero.");
            if (dto.Amount > 999_999_999_999.99m)
                throw new InvalidOperationException("Deposit amount is outside the supported range.");
            if (dto.FinanceAccountId <= 0)
                throw new InvalidOperationException("Select the account the deposit was paid from.");
            if (dto.PeriodFrom.HasValue && dto.PeriodTo.HasValue && dto.PeriodFrom > dto.PeriodTo)
                throw new InvalidOperationException("The period start cannot be after the period end.");
            if (dto.ChallanNumber?.Trim().Length > 100)
                throw new InvalidOperationException("Challan number cannot exceed 100 characters.");
            if (dto.Notes?.Trim().Length > 1000)
                throw new InvalidOperationException("Notes cannot exceed 1000 characters.");

            var challan = Clean(dto.ChallanNumber);
            if (challan != null && await _context.WhtDeposits.AnyAsync(
                    d => d.ChallanNumber == challan && (!excludingId.HasValue || d.Id != excludingId.Value), cancellationToken))
                throw new InvalidOperationException("A deposit with this challan number has already been recorded.");
        }

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private void ApplyConcurrencyToken<T>(T entity, string? token) where T : class
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("The record version is missing. Refresh and try again.");
            try { _context.Entry(entity).Property("RowVersion").OriginalValue = Convert.FromBase64String(token); }
            catch (FormatException) { throw new InvalidOperationException("The record version is invalid. Refresh and try again."); }
        }
    }
}
