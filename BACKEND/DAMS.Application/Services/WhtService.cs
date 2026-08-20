using System.Data;
using System.Globalization;
using System.Text;
using DAMS.Application.Common;
using DAMS.Application.DTOs.WhtDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

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
                vendor, category, date, startMonth,
                request.ExcludeExpenseId, request.ExcludeAssetPurchaseId, cancellationToken);

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

        public Task ApplyToExpenseAsync(
            Expense expense, decimal? requestedRate, decimal? requestedAmount, string? overrideReason,
            CancellationToken cancellationToken = default) =>
            ApplyAsync(expense, "expense", requestedRate, requestedAmount, overrideReason, cancellationToken);

        public Task ApplyToAssetPurchaseAsync(
            AssetPurchase purchase, decimal? requestedRate, decimal? requestedAmount, string? overrideReason,
            CancellationToken cancellationToken = default) =>
            ApplyAsync(purchase, "purchase", requestedRate, requestedAmount, overrideReason, cancellationToken);

        /// <summary>
        /// Resolves the tax for one payment to a supplier, whichever kind of record it is.
        /// <para>
        /// An expense and an asset purchase are opposite in the accounts and identical to FBR: both
        /// are money paid to a supplier under a tax section, so both take the same rate, the same
        /// filer treatment, and — critically — the same shared annual allowance. Running them
        /// through one method is what keeps that true.
        /// </para>
        /// </summary>
        private async Task ApplyAsync(
            IWithholdingSubject subject, string noun, decimal? requestedRate, decimal? requestedAmount,
            string? overrideReason, CancellationToken cancellationToken)
        {
            if (KeepsExistingTax(subject, requestedRate, requestedAmount))
                return;

            var category = subject.CategoryId.HasValue
                ? await _context.ExpenseCategories.AsNoTracking()
                    .SingleOrDefaultAsync(c => c.Id == subject.CategoryId.Value, cancellationToken)
                : null;

            var vendor = subject.VendorId.HasValue
                ? await _context.Vendors.AsNoTracking()
                    .SingleOrDefaultAsync(v => v.Id == subject.VendorId.Value, cancellationToken)
                : null;
            var filerStatus = vendor?.FilerStatus ?? FilerStatus.Unknown;

            if (category is not { IsWhtApplicable: true })
            {
                // Free-text head, or a head that carries no withholding. Refuse a tax figure here
                // rather than dropping it silently — a swallowed deduction is an unexplained gap
                // between the invoice and the bank.
                if (requestedAmount is > 0m || requestedRate is > 0m)
                    throw new InvalidOperationException(category == null
                        ? $"Withholding tax needs a managed category. Pick one, or clear the tax fields on this {noun}."
                        : $"\"{category.Name}\" is not subject to withholding tax. Clear the tax fields to save.");
                ClearWht(subject, filerStatus, category?.TaxSection);
                return;
            }

            var startMonth = await FinanceSettingsQuery.FinancialYearStartMonthAsync(_context, cancellationToken);
            var isExpense = subject is Expense;
            var ownId = subject.Id == 0 ? (int?)null : subject.Id;
            var yearToDate = await YearToDateAsync(
                vendor, category, subject.Date, startMonth,
                isExpense ? ownId : null, isExpense ? null : ownId, cancellationToken);

            var suggested = WhtCalculator.Compute(
                Rates(category, vendor != null), filerStatus, subject.Amount, yearToDate);

            decimal rate;
            decimal amount;
            if (requestedAmount.HasValue)
            {
                // The amount wins over the rate: an operator typing a figure is copying it off the
                // vendor invoice, and that figure is what will be deposited.
                amount = requestedAmount.Value;
                rate = WhtCalculator.EffectiveRate(subject.Amount, amount);
            }
            else if (requestedRate.HasValue && requestedRate.Value != suggested.Rate)
            {
                rate = requestedRate.Value;
                amount = WhtCalculator.TaxOn(subject.Amount, rate);
            }
            else
            {
                // No override, or a rate echoed back unchanged — take the computed figure, which
                // is the one that respects the annual threshold.
                rate = suggested.Rate;
                amount = suggested.WhtAmount;
            }

            ValidateTax(subject.Amount, rate, amount, noun);

            var overridden = amount != suggested.WhtAmount;
            if (overridden && string.IsNullOrWhiteSpace(overrideReason))
                throw new InvalidOperationException(
                    $"Withholding tax was changed from the calculated {suggested.WhtAmount:N2}. Give a reason for the override.");

            subject.WhtRate = rate;
            subject.WhtAmount = amount;
            subject.WhtApplied = amount > 0m;
            subject.WhtRateOverridden = overridden;
            subject.WhtOverrideReason = overridden ? overrideReason!.Trim() : null;
            // Snapshotted whether or not tax was withheld: a below-threshold payment still counts
            // towards the section's annual aggregate, so the section has to be recorded.
            subject.WhtTaxSection = category.TaxSection;
            subject.VendorFilerStatusAtEntry = filerStatus;
        }

        /// <summary>
        /// True when this edit cannot legitimately have moved the tax, so the figure already on the
        /// row stands untouched.
        /// <para>
        /// Withheld tax is a snapshot of what was actually deducted and handed to FBR. Only four
        /// inputs can change it: the gross amount, the category, the vendor and the date. Every
        /// other edit — fixing a typo in the description, swapping the attachment, moving the
        /// expense to a project — has to leave it exactly as filed.
        /// </para>
        /// <para>
        /// Without this guard, re-saving an old expense re-runs the annual threshold against
        /// everything paid <em>since</em>. A January payment that was correctly below the allowance
        /// looks taxable once February crosses it, so correcting its spelling in March would either
        /// demand an override reason for a figure that was right all along, or quietly restate tax
        /// for a period that may already be filed.
        /// </para>
        /// </summary>
        private bool KeepsExistingTax(IWithholdingSubject subject, decimal? requestedRate, decimal? requestedAmount)
        {
            if (subject.Id == 0) return false;

            var entry = _context.Entry(subject);
            if (entry.State is EntityState.Detached or EntityState.Added) return false;

            // Both record types name these four identically, which is what lets one guard cover
            // them; the interface is the contract that keeps it that way.
            if (HasChanged(entry, nameof(IWithholdingSubject.Amount))
                || HasChanged(entry, nameof(IWithholdingSubject.CategoryId))
                || HasChanged(entry, nameof(IWithholdingSubject.VendorId))
                || HasChanged(entry, nameof(IWithholdingSubject.Date)))
                return false;

            // Correcting the deduction by hand is still allowed — that is a deliberate change to
            // the tax and goes through the override path below. The amount wins over the rate here
            // for the same reason it does there: it is the figure that will be deposited.
            if (requestedAmount.HasValue) return requestedAmount.Value == subject.WhtAmount;
            if (requestedRate.HasValue) return requestedRate.Value == subject.WhtRate;
            return true;
        }

        private static bool HasChanged(EntityEntry entry, string propertyName)
        {
            var property = entry.Property(propertyName);
            return !Equals(property.OriginalValue, property.CurrentValue);
        }

        private static void ClearWht(IWithholdingSubject subject, FilerStatus filerStatus, string? section)
        {
            subject.WhtApplied = false;
            subject.WhtRate = 0m;
            subject.WhtAmount = 0m;
            subject.WhtRateOverridden = false;
            subject.WhtOverrideReason = null;
            subject.WhtTaxSection = section;
            subject.VendorFilerStatusAtEntry = filerStatus;
        }

        private static void ValidateTax(decimal grossAmount, decimal rate, decimal amount, string noun)
        {
            if (rate < 0m) throw new InvalidOperationException("Withholding rate cannot be negative.");
            if (rate > 100m) throw new InvalidOperationException("Withholding rate cannot exceed 100%.");
            if (amount < 0m) throw new InvalidOperationException("Withholding tax cannot be negative.");
            // Equal is allowed (a 100% deduction nets to zero); more than the payment would make
            // the vendor owe money for having been paid.
            if (amount > grossAmount)
                throw new InvalidOperationException($"Withholding tax cannot exceed the gross amount of the {noun}.");
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
        /// <para>
        /// Counts expenses AND fixed-asset purchases. The allowance belongs to the supplier, not to
        /// the ledger the payment happens to land in: a vendor who sells the company 60,000 of
        /// cement and 60,000 of office furniture has been paid 120,000 of goods and is well past
        /// the 75,000 threshold, even though neither figure crosses it alone.
        /// </para>
        /// </summary>
        private async Task<decimal> YearToDateAsync(
            Vendor? vendor, ExpenseCategory category, DateTime date, int startMonth,
            int? excludeExpenseId, int? excludeAssetPurchaseId, CancellationToken cancellationToken)
        {
            if (vendor == null || category.AnnualThreshold <= 0m)
                return 0m;

            var (start, end) = FinancialYear.Window(date, startMonth);
            var vendorId = vendor.Id;
            var vendorName = vendor.Name;
            var section = category.TaxSection;
            var bySection = !string.IsNullOrWhiteSpace(section);

            var expenses = _context.Expenses.AsNoTracking()
                .Where(e => e.Date >= start && e.Date < end
                    // Payments made before this vendor had a record carry the same name as free
                    // text. They are the same supplier's money, so they count towards the annual
                    // aggregate — without this, the first year after a vendor is created starts
                    // its allowance again from zero and under-withholds.
                    && (e.VendorId == vendorId || (e.VendorId == null && e.Vendor == vendorName))
                    && (bySection ? e.WhtTaxSection == section : e.CategoryId == category.Id)
                    && (excludeExpenseId == null || e.Id != excludeExpenseId.Value));

            var purchases = _context.AssetPurchases.AsNoTracking()
                .Where(p => p.Date >= start && p.Date < end
                    && (p.VendorId == vendorId || (p.VendorId == null && p.Vendor == vendorName))
                    && (bySection ? p.WhtTaxSection == section : p.CategoryId == category.Id)
                    && (excludeAssetPurchaseId == null || p.Id != excludeAssetPurchaseId.Value));

            return (await expenses.SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m)
                + (await purchases.SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m);
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
            // Grouped with the concrete entity in the lambda on both sides. Routing these through a
            // shared generic over IWithholdingSubject reads better and does not translate: EF maps
            // member access against the mapped type, not an interface the type happens to implement.
            var expenseSections = await WithheldExpenses(from, to)
                .GroupBy(e => e.WhtTaxSection)
                .Select(g => new WhtSectionTotalDto
                {
                    TaxSection = g.Key ?? string.Empty,
                    GrossAmount = g.Sum(e => (decimal?)e.Amount) ?? 0m,
                    WhtAmount = g.Sum(e => (decimal?)e.WhtAmount) ?? 0m,
                    PaymentCount = g.Count()
                })
                .ToListAsync(cancellationToken);
            var purchaseSections = await WithheldPurchases(from, to)
                .GroupBy(p => p.WhtTaxSection)
                .Select(g => new WhtSectionTotalDto
                {
                    TaxSection = g.Key ?? string.Empty,
                    GrossAmount = g.Sum(p => (decimal?)p.Amount) ?? 0m,
                    WhtAmount = g.Sum(p => (decimal?)p.WhtAmount) ?? 0m,
                    PaymentCount = g.Count()
                })
                .ToListAsync(cancellationToken);
            var bySection = Merge(expenseSections, purchaseSections);

            var totalWithheldAllTime =
                (await WithheldExpenses(null, null).SumAsync(e => (decimal?)e.WhtAmount, cancellationToken) ?? 0m)
                + (await WithheldPurchases(null, null).SumAsync(p => (decimal?)p.WhtAmount, cancellationToken) ?? 0m);
            var totalDepositedAllTime = await DepositQuery(null, null)
                .SumAsync(d => (decimal?)d.Amount, cancellationToken) ?? 0m;

            // Distinct across both kinds, so a supplier who was paid for a desk and for cement is
            // one vendor on the statement, not two.
            var vendorIds = (await WithheldExpenses(from, to).Select(e => e.VendorId).Distinct().ToListAsync(cancellationToken))
                .Union(await WithheldPurchases(from, to).Select(p => p.VendorId).Distinct().ToListAsync(cancellationToken))
                .Count();

            return new WhtPayableSummaryDto
            {
                WithheldInPeriod = bySection.Sum(s => s.WhtAmount),
                DepositedInPeriod = await DepositQuery(from, to).SumAsync(d => (decimal?)d.Amount, cancellationToken) ?? 0m,
                // A liability is a balance, so it is deliberately not date-filtered: what is still
                // owed to FBR does not change because the user picked a narrower period.
                OutstandingPayable = totalWithheldAllTime - totalDepositedAllTime,
                TotalWithheldAllTime = totalWithheldAllTime,
                TotalDepositedAllTime = totalDepositedAllTime,
                PaymentCount = bySection.Sum(s => s.PaymentCount),
                VendorCount = vendorIds,
                BySection = bySection
                    .Select(s => new WhtSectionTotalDto
                    {
                        TaxSection = string.IsNullOrWhiteSpace(s.TaxSection) ? "Unspecified" : s.TaxSection,
                        GrossAmount = s.GrossAmount,
                        WhtAmount = s.WhtAmount,
                        PaymentCount = s.PaymentCount
                    })
                    .OrderByDescending(s => s.WhtAmount)
                    .ToList()
            };
        }

        /// <summary>
        /// Folds the asset-purchase totals into the expense totals section by section.
        /// <para>
        /// The two sides are grouped separately in SQL and joined here rather than unioned into one
        /// query, because a set operation followed by GROUP BY is exactly the shape EF has to give
        /// up translating. Each side still aggregates in the database — only the handful of section
        /// rows crosses the wire.
        /// </para>
        /// </summary>
        private static List<WhtSectionTotalDto> Merge(
            List<WhtSectionTotalDto> first, List<WhtSectionTotalDto> second) =>
            first.Concat(second)
                .GroupBy(s => s.TaxSection ?? string.Empty, StringComparer.Ordinal)
                .Select(g => new WhtSectionTotalDto
                {
                    TaxSection = g.Key,
                    GrossAmount = g.Sum(s => s.GrossAmount),
                    WhtAmount = g.Sum(s => s.WhtAmount),
                    PaymentCount = g.Sum(s => s.PaymentCount)
                })
                .ToList();

        public async Task<List<WhtVendorLineDto>> GetByVendorAsync(
            DateTime? from, DateTime? to, CancellationToken cancellationToken = default)
        {
            // Grouped by vendor, section *and* the filer status the tax was withheld under. A s.165
            // statement reports each section separately, one supplier can be paid under more than
            // one, and a supplier whose ATL status changed mid-period was genuinely withheld at two
            // different rates — collapsing that into one line at today's status would report a
            // filer rate against a non-filer heading.
            // Both kinds of payment, folded together: one supplier line covers what they were paid
            // for goods whether those goods were consumed or capitalised. Grouped separately in SQL
            // for the same translation reason as the section totals above.
            var expenseRows = await WithheldExpenses(from, to)
                .GroupBy(e => new { e.VendorId, e.WhtTaxSection, e.VendorFilerStatusAtEntry })
                .Select(g => new VendorGroup(
                    g.Key.VendorId,
                    g.Key.WhtTaxSection,
                    g.Key.VendorFilerStatusAtEntry,
                    // Free-text vendors have no record to read a name from; take the snapshot on
                    // any row in the group, which is the name that was on the payment.
                    g.Min(e => e.Vendor),
                    g.Sum(e => (decimal?)e.Amount) ?? 0m,
                    g.Sum(e => (decimal?)e.WhtAmount) ?? 0m,
                    g.Count()))
                .ToListAsync(cancellationToken);
            var purchaseRows = await WithheldPurchases(from, to)
                .GroupBy(p => new { p.VendorId, p.WhtTaxSection, p.VendorFilerStatusAtEntry })
                .Select(g => new VendorGroup(
                    g.Key.VendorId,
                    g.Key.WhtTaxSection,
                    g.Key.VendorFilerStatusAtEntry,
                    g.Min(p => p.Vendor),
                    g.Sum(p => (decimal?)p.Amount) ?? 0m,
                    g.Sum(p => (decimal?)p.WhtAmount) ?? 0m,
                    g.Count()))
                .ToListAsync(cancellationToken);

            var rows = expenseRows.Concat(purchaseRows)
                .GroupBy(r => new { r.VendorId, r.WhtTaxSection, r.VendorFilerStatusAtEntry })
                .Select(g => new VendorGroup(
                    g.Key.VendorId,
                    g.Key.WhtTaxSection,
                    g.Key.VendorFilerStatusAtEntry,
                    g.Select(r => r.FallbackName).FirstOrDefault(n => n != null),
                    g.Sum(r => r.GrossAmount),
                    g.Sum(r => r.WhtAmount),
                    g.Sum(r => r.PaymentCount)))
                .ToList();

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
                        // Name, NTN and CNIC are identity — the current record is the right one to
                        // report. Filer status is not identity: it is the fact that set the rate on
                        // the day, so it comes off the expense snapshot and never moves again.
                        FilerStatus = r.VendorFilerStatusAtEntry,
                        TaxSection = string.IsNullOrWhiteSpace(r.WhtTaxSection) ? null : r.WhtTaxSection,
                        GrossAmount = r.GrossAmount,
                        WhtAmount = r.WhtAmount,
                        NetPaid = r.GrossAmount - r.WhtAmount,
                        PaymentCount = r.PaymentCount
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
                   .Append(line.PaymentCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                   .Append(line.GrossAmount.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                   .Append(line.WhtAmount.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                   .Append(line.NetPaid.ToString("0.00", CultureInfo.InvariantCulture))
                   .AppendLine();
            }
            csv.Append("Total,,,,,")
               .Append(lines.Sum(l => l.PaymentCount).ToString(CultureInfo.InvariantCulture)).Append(',')
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

        private sealed record VendorGroup(
            int? VendorId, string? WhtTaxSection, FilerStatus VendorFilerStatusAtEntry,
            string? FallbackName, decimal GrossAmount, decimal WhtAmount, int PaymentCount);

        private IQueryable<Expense> WithheldExpenses(DateTime? from, DateTime? to)
        {
            var query = _context.Expenses.AsNoTracking().Where(e => e.WhtAmount > 0m);
            if (from.HasValue) query = query.Where(e => e.Date >= from.Value.Date);
            if (to.HasValue) query = query.Where(e => e.Date < to.Value.Date.AddDays(1));
            return query;
        }

        /// <summary>Tax withheld from fixed-asset purchases. It is owed to FBR on exactly the same
        /// terms as tax withheld from an expense, so every payable figure has to count it — leaving
        /// it out would understate the liability by whatever was deducted from capital suppliers.</summary>
        private IQueryable<AssetPurchase> WithheldPurchases(DateTime? from, DateTime? to)
        {
            var query = _context.AssetPurchases.AsNoTracking().Where(p => p.WhtAmount > 0m);
            if (from.HasValue) query = query.Where(p => p.Date >= from.Value.Date);
            if (to.HasValue) query = query.Where(p => p.Date < to.Value.Date.AddDays(1));
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

        public Task<WhtDepositDto> CreateDepositAsync(
            SaveWhtDepositDto dto, int? adminUserId, CancellationToken cancellationToken = default) =>
            ExecuteResilientlyAsync(() => CreateDepositCoreAsync(dto, adminUserId, cancellationToken));

        private async Task<WhtDepositDto> CreateDepositCoreAsync(
            SaveWhtDepositDto dto, int? adminUserId, CancellationToken cancellationToken)
        {
            // Serializable, because the "never more than is owed" check reads the whole withheld and
            // deposited history and then writes against what it read. Two admins clearing the same
            // liability at the same moment would otherwise both see the same remaining balance and
            // both be allowed through, leaving Tax Payable negative.
            await using var guard = await BeginGuardAsync(cancellationToken);
            var committed = false;
            try
            {
                var depositDate = await FinanceDateRules.ResolveAsync(
                    _context, dto.DepositDate, "Deposit date", cancellationToken);
                await ValidateDepositAsync(dto, null, cancellationToken);
                await _accountService.EnsureSelectableAsync(dto.FinanceAccountId, null, cancellationToken);

                var deposit = new WhtDeposit
                {
                    FinanceAccountId = dto.FinanceAccountId,
                    Amount = dto.Amount,
                    DepositDate = depositDate,
                    ChallanNumber = Clean(dto.ChallanNumber),
                    PeriodFrom = dto.PeriodFrom?.Date,
                    PeriodTo = dto.PeriodTo?.Date,
                    Notes = Clean(dto.Notes),
                    CreatedByUserId = adminUserId,
                    CreatedAt = DateTime.UtcNow
                };
                _context.WhtDeposits.Add(deposit);
                await _context.SaveChangesAsync(cancellationToken);
                var result = await GetDepositAsync(deposit.Id, cancellationToken);
                if (guard != null) await guard.CommitAsync(cancellationToken);
                committed = true;
                return result;
            }
            catch
            {
                if (guard != null && !committed) await guard.RollbackAsync(cancellationToken);
                throw;
            }
        }

        public Task<WhtDepositDto> UpdateDepositAsync(
            int id, SaveWhtDepositDto dto, CancellationToken cancellationToken = default) =>
            ExecuteResilientlyAsync(() => UpdateDepositCoreAsync(id, dto, cancellationToken));

        private async Task<WhtDepositDto> UpdateDepositCoreAsync(
            int id, SaveWhtDepositDto dto, CancellationToken cancellationToken)
        {
            await using var guard = await BeginGuardAsync(cancellationToken);
            var committed = false;
            try
            {
                var deposit = await _context.WhtDeposits.SingleOrDefaultAsync(d => d.Id == id, cancellationToken)
                    ?? throw new InvalidOperationException("WHT deposit not found.");
                // An omitted date keeps the one already recorded, so the amount on a deposit made
                // before the opening-balance baseline can still be corrected in place.
                var depositDate = dto.DepositDate.HasValue
                    ? await FinanceDateRules.ResolveAsync(_context, dto.DepositDate, "Deposit date", cancellationToken)
                    : deposit.DepositDate;
                await ValidateDepositAsync(dto, id, cancellationToken);
                ApplyConcurrencyToken(deposit, dto.ConcurrencyToken);
                await _accountService.EnsureSelectableAsync(dto.FinanceAccountId, deposit.FinanceAccountId, cancellationToken);

                deposit.FinanceAccountId = dto.FinanceAccountId;
                deposit.Amount = dto.Amount;
                deposit.DepositDate = depositDate;
                deposit.ChallanNumber = Clean(dto.ChallanNumber);
                deposit.PeriodFrom = dto.PeriodFrom?.Date;
                deposit.PeriodTo = dto.PeriodTo?.Date;
                deposit.Notes = Clean(dto.Notes);

                await _context.SaveChangesAsync(cancellationToken);
                var result = await GetDepositAsync(id, cancellationToken);
                if (guard != null) await guard.CommitAsync(cancellationToken);
                committed = true;
                return result;
            }
            catch
            {
                if (guard != null && !committed) await guard.RollbackAsync(cancellationToken);
                throw;
            }
        }

        /// <summary>
        /// Deleting a deposit puts the tax straight back onto the payable, so it is a financial
        /// movement in its own right and takes the same stale-write protection as editing one.
        /// Without the token an admin could delete the version they were looking at moments after
        /// someone else corrected its amount.
        /// </summary>
        public async Task DeleteDepositAsync(
            int id, string? concurrencyToken = null, CancellationToken cancellationToken = default)
        {
            var deposit = await _context.WhtDeposits.SingleOrDefaultAsync(d => d.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("WHT deposit not found.");
            ApplyConcurrencyToken(deposit, concurrencyToken);
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

            // Tax Payable is withheld minus deposited. Handing FBR more than was ever withheld drives
            // that liability negative — a credit balance on a liability account, which is not a state
            // this business has: DAMS deposits tax it has already deducted from a supplier, it does
            // not make advance tax payments. Refusing the excess here is what stops the Balance Sheet,
            // the Trial Balance and the payable summary from having to explain a negative liability.
            var outstanding = await OutstandingPayableAsync(excludingId, cancellationToken);
            if (dto.Amount > outstanding)
                throw new InvalidOperationException(outstanding <= 0m
                    ? "There is no withheld tax outstanding to deposit. Record the expenses the tax was deducted from first."
                    : $"Deposit exceeds the withholding tax outstanding. At most {outstanding:N2} can be deposited.");
        }

        /// <summary>
        /// Refuses a correction to a source record that would leave less tax withheld than has
        /// already been handed to FBR.
        /// <para>
        /// <see cref="ValidateDepositAsync"/> guards this invariant from the deposit side: never pay
        /// over more than was withheld. It can be broken from the other side just as easily —
        /// withhold 10,000, deposit 10,000, then delete or reduce the expense the tax came from, and
        /// Tax Payable is left at minus 10,000. A negative liability is not a state this business
        /// has: the money really did go to FBR, so the record it was deducted from cannot simply
        /// vanish underneath it.
        /// </para>
        /// <para>
        /// The change is signed, and only a reduction is checked: raising the tax or adding a record
        /// can never uncover a deposit. The caller passes the delta rather than the new figure
        /// because the stored total is read here, inside the caller's serialisable window, where the
        /// deposit path cannot slip between the read and the write.
        /// </para>
        /// </summary>
        public async Task EnsureDepositsStayCoveredAsync(
            decimal withheldChange, CancellationToken cancellationToken = default)
        {
            if (withheldChange >= 0m) return;
            var withheld = (await WithheldExpenses(null, null).SumAsync(e => (decimal?)e.WhtAmount, cancellationToken) ?? 0m)
                + (await WithheldPurchases(null, null).SumAsync(p => (decimal?)p.WhtAmount, cancellationToken) ?? 0m);
            var deposited = await _context.WhtDeposits.AsNoTracking()
                .SumAsync(d => (decimal?)d.Amount, cancellationToken) ?? 0m;
            var proposed = Math.Round(withheld + withheldChange, 2, MidpointRounding.AwayFromZero);
            if (proposed < Math.Round(deposited, 2, MidpointRounding.AwayFromZero))
                throw new InvalidOperationException(
                    $"This change would leave {proposed:N2} of withholding tax recorded against "
                    + $"{deposited:N2} already deposited with FBR, which would make Tax Payable negative. "
                    + "Correct or remove the FBR deposit first, then change this record.");
        }

        /// <summary>
        /// Withheld tax that has not yet been handed to FBR, across expenses AND asset purchases — the
        /// same figure the payable summary and the Tax Payable account balance show. All-time on
        /// purpose: the liability is a running balance, not a period total, so an August deposit can
        /// legitimately clear tax withheld in July.
        /// </summary>
        private async Task<decimal> OutstandingPayableAsync(int? excludingDepositId, CancellationToken cancellationToken)
        {
            var withheld = (await WithheldExpenses(null, null).SumAsync(e => (decimal?)e.WhtAmount, cancellationToken) ?? 0m)
                + (await WithheldPurchases(null, null).SumAsync(p => (decimal?)p.WhtAmount, cancellationToken) ?? 0m);
            var deposited = await _context.WhtDeposits.AsNoTracking()
                .Where(d => !excludingDepositId.HasValue || d.Id != excludingDepositId.Value)
                .SumAsync(d => (decimal?)d.Amount, cancellationToken) ?? 0m;
            return Math.Round(withheld - deposited, 2, MidpointRounding.AwayFromZero);
        }

        // The same shape LoanService uses: a serializable window around a read-then-write, skipped on
        // a non-relational provider and when the caller already owns a transaction.
        private async Task<IDbContextTransaction?> BeginGuardAsync(CancellationToken cancellationToken)
        {
            if (!_context.Database.IsRelational() || _context.Database.CurrentTransaction != null) return null;
            return await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        }

        private Task<T> ExecuteResilientlyAsync<T>(Func<Task<T>> operation) =>
            _context.Database.CreateExecutionStrategy().ExecuteAsync(operation);

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
