using DAMS.Application.DTOs.FinanceDtos;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    /// <summary>
    /// Everything the Finance dashboard screen draws — the cards, the revenue/expense trend and the
    /// revenue-by-project split — answered in ONE request, over ONE set of date bounds, from ONE set
    /// of database reads.
    /// <para>
    /// It exists because the screen used to assemble itself client-side out of the summary endpoint:
    /// one full summary computation per chart bucket, another per project, one for the distribution
    /// total and one for the cards. Twelve buckets and ten projects meant twenty-four summaries, and
    /// a summary is not one aggregate — it is twenty-odd of them.
    /// </para>
    /// <para>
    /// Collapsing that into one HTTP call was only half the fix. Each of the three sections still
    /// read the same tables for itself: the cards summed each source, the trend grouped it by date,
    /// the pie grouped it by project — three passes over the same rows. Now every source is read
    /// exactly ONCE, grouped by (date, project) in SQL, and the cards, the bars and the slices are
    /// all folded out of those same rows in memory. The query count is therefore flat in the number
    /// of buckets, flat in the number of projects, and a bounded constant
    /// (<see cref="PeriodAggregateQueryCount"/>) in the number of sources — and the three sections
    /// cannot disagree, because there is no second reading of anything for them to disagree about.
    /// </para>
    /// <para>
    /// What comes back is one row per (date, project) pair that actually has activity, not one per
    /// transaction, so the payload is bounded by the shape of the calendar rather than by trading
    /// volume. The SQL Server suite asserts the command count so a future edit that reintroduces a
    /// per-source or per-project pass fails a test rather than a benchmark.
    /// </para>
    /// </summary>
    public partial class FinanceService
    {
        /// <summary>At most this many bars. The granularity is widened until the range fits — never
        /// by dropping buckets, which would leave the periods between the drawn bars missing from a
        /// chart that still claims to cover the whole range.</summary>
        private const int MaxTrendBuckets = 12;

        /// <summary>How many project slices the pie names individually before the rest are collapsed
        /// into one "Other Projects" slice.</summary>
        private const int NamedDistributionSlices = 4;

        public async Task<FinanceDashboardDataDto> GetDashboardAsync(
            int? projectId, DateTime? from, DateTime? to, int? accountId = null, bool unassigned = false,
            CancellationToken cancellationToken = default)
        {
            var fromValue = from?.Date;
            var toValue = to?.Date;
            EnsureFilterRange(fromValue, toValue);
            var toExclusive = ExclusiveEnd(toValue);

            // ONE read set. The cards, the bars and the slices below are three foldings of it.
            var aggregates = await LoadPeriodAggregatesAsync(
                projectId, fromValue, toExclusive, accountId, unassigned, cancellationToken);

            var summary = await BuildSummaryAsync(
                aggregates, fromValue, toValue, toExclusive, projectId, accountId, unassigned, cancellationToken);
            var trend = BuildTrend(fromValue, toValue, aggregates);
            var distribution = await DistributionAsync(aggregates, cancellationToken);

            return new FinanceDashboardDataDto { Summary = summary, Trend = trend, Distribution = distribution };
        }

        // ── The shared read set ──────────────────────────────────────────────────────
        // One grouped query per financial source. The key is (posting date, project) because that is
        // the coarsest shape all three sections can be derived from: the cards need neither part, the
        // trend needs the date, the pie needs the project. Grouping once by both costs one pass;
        // deriving the three separately cost three.

        /// <summary>One (date, project) group of one financial source.</summary>
        private readonly record struct Slice(DateTime Date, int? ProjectId, decimal Amount);

        /// <summary>
        /// Every source the dashboard cards, chart and pie are built from, read once each. Reversals
        /// are kept as their own positive-amount lists rather than pre-netted, because the cards
        /// subtract them while the drill-down shows each as its own row.
        /// </summary>
        private sealed class PeriodAggregates
        {
            public List<Slice> RecognisedSales { get; init; } = [];
            public List<Slice> RetainedCancellations { get; init; } = [];
            public List<Slice> ManualRevenue { get; init; } = [];
            public List<Slice> OrdinaryExpenses { get; init; } = [];
            public List<Slice> CommissionPayouts { get; init; } = [];
            public List<Slice> CommissionReversals { get; init; } = [];
            public List<Slice> CashRebates { get; init; } = [];
            public List<Slice> CashRebateReversals { get; init; } = [];
            public List<Slice> NonCashCredits { get; init; } = [];
            public List<Slice> NonCashCreditReversals { get; init; } = [];
            public List<Slice> LoanInterest { get; init; } = [];
            public List<Slice> FixedAssetPurchases { get; init; } = [];

            /// <summary>Withheld from expenses and from ALL asset purchases — work-in-progress rows
            /// included, because they raise the same payable to FBR even though they are outside the
            /// charge to profit. That is why it is not folded out of
            /// <see cref="FixedAssetPurchases"/>.</summary>
            public decimal WhtWithheld { get; init; }

            public decimal AutomaticRevenue => Total(RecognisedSales) + Total(RetainedCancellations);
            public decimal ManualRevenueTotal => Total(ManualRevenue);
            public decimal TotalRevenue => AutomaticRevenue + ManualRevenueTotal;
            public decimal FixedAssetCharge => Total(FixedAssetPurchases);

            /// <summary>Every cost of the period, each source net of its reversals, fixed assets
            /// included. The card, the expense bars and the Total Expenses drill-down are all this
            /// same arithmetic — the first over everything, the second over one bucket, the third
            /// row by row.</summary>
            public decimal TotalExpenses =>
                Total(OrdinaryExpenses)
                + Total(CommissionPayouts) - Total(CommissionReversals)
                + Total(CashRebates) - Total(CashRebateReversals)
                + Total(NonCashCredits) - Total(NonCashCreditReversals)
                + Total(LoanInterest) + FixedAssetCharge;

            /// <summary>Revenue slices, all sources together.</summary>
            public IEnumerable<Slice> RevenueSlices =>
                RecognisedSales.Concat(RetainedCancellations).Concat(ManualRevenue);

            /// <summary>Cost slices, signed: a reversal carries a negative amount, so a bucket
            /// holding only a reversal lowers the bar exactly as it lowers the card.</summary>
            public IEnumerable<Slice> CostSlices =>
                OrdinaryExpenses
                    .Concat(CommissionPayouts).Concat(Negated(CommissionReversals))
                    .Concat(CashRebates).Concat(Negated(CashRebateReversals))
                    .Concat(NonCashCredits).Concat(Negated(NonCashCreditReversals))
                    .Concat(LoanInterest).Concat(FixedAssetPurchases);

            private static decimal Total(List<Slice> slices) => slices.Sum(s => s.Amount);

            private static IEnumerable<Slice> Negated(List<Slice> slices) =>
                slices.Select(s => new Slice(s.Date, s.ProjectId, -s.Amount));
        }

        /// <summary>
        /// How many database round trips <see cref="LoadPeriodAggregatesAsync"/> makes: twelve
        /// financial sources plus the asset-purchase withholding total. A constant, named here so the
        /// SQL Server command-count test asserts against the contract rather than against a number
        /// somebody has to remember to update.
        /// </summary>
        internal const int PeriodAggregateQueryCount = 13;

        private async Task<PeriodAggregates> LoadPeriodAggregatesAsync(
            int? projectId, DateTime? fromValue, DateTime? toExclusive, int? accountId, bool unassigned,
            CancellationToken cancellationToken)
        {
            // Revenue DAMS recognises by itself. Deliberately NOT "customer receipts minus refunds":
            // cash taken before possession is a deposit the company owes back, so it was never
            // income. What the business earned is the sale, recognised once at possession, plus
            // whatever it keeps when a booking is cancelled.
            var recognisedSales = await SaleRecognitionQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .GroupBy(r => new { Date = r.RecognitionDate, ProjectId = (int?)r.Booking.Unit.ProjectId })
                .Select(g => new { g.Key.Date, g.Key.ProjectId, Amount = g.Sum(r => r.NetSaleValue) })
                .ToListAsync(cancellationToken);
            var retainedCancellations = await RetainedCancellationQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .GroupBy(s => new { Date = s.CancellationDate, ProjectId = (int?)s.Booking.Unit.ProjectId })
                .Select(g => new { g.Key.Date, g.Key.ProjectId, Amount = g.Sum(s => s.RetainedAmount) })
                .ToListAsync(cancellationToken);
            var manualRevenue = await ManualQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .GroupBy(r => new { r.Date, r.ProjectId })
                .Select(g => new { g.Key.Date, g.Key.ProjectId, Amount = g.Sum(r => r.Amount) })
                .ToListAsync(cancellationToken);

            // GROSS. The full invoice is the business cost, whatever was withheld from the payment,
            // so the expense and profit figures are unaffected by withholding. The withheld amount
            // rides along in the same grouped read rather than costing a second one.
            var expenses = await ExpenseQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .GroupBy(e => new { e.Date, e.ProjectId })
                .Select(g => new { g.Key.Date, g.Key.ProjectId, Amount = g.Sum(e => e.Amount), Wht = g.Sum(e => e.WhtAmount) })
                .ToListAsync(cancellationToken);
            // ALL purchases, not only the fixed-asset ones: withholding on a work-in-progress
            // purchase is owed to FBR just the same, and the Tax Payable account and the WHT screen
            // both count it. Its own query for exactly that reason — it is a wider row set than the
            // charge to profit below, and merging the two would understate one of them.
            var purchaseWht = await AssetPurchaseQuery(projectId, fromValue, toExclusive, null, accountId, unassigned)
                .SumAsync(p => (decimal?)p.WhtAmount, cancellationToken) ?? 0m;

            var commissionPayouts = await CommissionPayoutQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .GroupBy(p => new { Date = p.PaymentDate, ProjectId = (int?)p.Commission.Booking.Unit.ProjectId })
                .Select(g => new { g.Key.Date, g.Key.ProjectId, Amount = g.Sum(p => p.Amount) })
                .ToListAsync(cancellationToken);
            var commissionReversals = await CommissionReversalQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .GroupBy(r => new { Date = r.ReversedAt, ProjectId = (int?)r.Payout.Commission.Booking.Unit.ProjectId })
                .Select(g => new { g.Key.Date, g.Key.ProjectId, Amount = g.Sum(r => r.Amount) })
                .ToListAsync(cancellationToken);
            var cashRebates = await CashRebateQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .GroupBy(d => new { Date = d.AppliedAt, ProjectId = (int?)d.Rebate.Booking.Unit.ProjectId })
                .Select(g => new { g.Key.Date, g.Key.ProjectId, Amount = g.Sum(d => d.Amount) })
                .ToListAsync(cancellationToken);
            var cashRebateReversals = await CashRebateReversalQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .GroupBy(r => new { Date = r.ReversedAt, ProjectId = (int?)r.Disbursement.Rebate.Booking.Unit.ProjectId })
                .Select(g => new { g.Key.Date, g.Key.ProjectId, Amount = g.Sum(r => r.Amount) })
                .ToListAsync(cancellationToken);

            // Credits granted against an already-recognised sale: the slice of recognised revenue the
            // buyer will never pay. Dated at the later of the credit and the recognition it reduces —
            // a credit is not a cost before the sale it writes down is income — which makes the
            // grouping key a CASE expression. That is what the SQL Server test for this exists for:
            // the in-memory provider will evaluate a key SQL Server might refuse to translate.
            var nonCashCredits = await NonCashCreditQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .GroupBy(d => new
                {
                    Date = d.AppliedAt < d.Rebate.Booking.SaleRecognition!.RecognitionDate.AddDays(1)
                        ? d.Rebate.Booking.SaleRecognition!.RecognitionDate : d.AppliedAt,
                    ProjectId = (int?)d.Rebate.Booking.Unit.ProjectId
                })
                .Select(g => new { g.Key.Date, g.Key.ProjectId, Amount = g.Sum(d => d.Amount) })
                .ToListAsync(cancellationToken);
            var nonCashCreditReversals = await NonCashCreditReversalQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .GroupBy(r => new
                {
                    Date = r.ReversedAt < r.Disbursement.Rebate.Booking.SaleRecognition!.RecognitionDate.AddDays(1)
                        ? r.Disbursement.Rebate.Booking.SaleRecognition!.RecognitionDate : r.ReversedAt,
                    ProjectId = (int?)r.Disbursement.Rebate.Booking.Unit.ProjectId
                })
                .Select(g => new { g.Key.Date, g.Key.ProjectId, Amount = g.Sum(r => r.Amount) })
                .ToListAsync(cancellationToken);

            // Loans are company-wide until project attribution is designed, so the slice carries no
            // project — it belongs to no pie slice, and the query returns nothing at all once a
            // project is selected.
            var loanInterest = await LoanInterestQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .GroupBy(t => t.Date)
                .Select(g => new { Date = g.Key, Amount = g.Sum(t => t.InterestAmount) })
                .ToListAsync(cancellationToken);

            // Fixed assets bought in the period, at gross cost, INSIDE the expense total. Buying an
            // asset spends the money, and the client's confirmed rule is that the period bears that
            // spending — so the cost reaches Net Profit by the ordinary route rather than being held
            // outside it as a second figure to reconcile. The formal P&L applies the same rule to the
            // same rows. The asset itself is untouched: it stays on the Balance Sheet at cost, which
            // is why that statement cannot carry this charge and discloses it instead.
            var fixedAssets = await FixedAssetChargeQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .GroupBy(p => new { p.Date, p.ProjectId })
                .Select(g => new { g.Key.Date, g.Key.ProjectId, Amount = g.Sum(p => p.Amount) })
                .ToListAsync(cancellationToken);

            return new PeriodAggregates
            {
                RecognisedSales = recognisedSales.Select(x => new Slice(x.Date, x.ProjectId, x.Amount)).ToList(),
                RetainedCancellations = retainedCancellations.Select(x => new Slice(x.Date, x.ProjectId, x.Amount)).ToList(),
                ManualRevenue = manualRevenue.Select(x => new Slice(x.Date, x.ProjectId, x.Amount)).ToList(),
                OrdinaryExpenses = expenses.Select(x => new Slice(x.Date, x.ProjectId, x.Amount)).ToList(),
                WhtWithheld = expenses.Sum(x => x.Wht) + purchaseWht,
                CommissionPayouts = commissionPayouts.Select(x => new Slice(x.Date, x.ProjectId, x.Amount)).ToList(),
                CommissionReversals = commissionReversals.Select(x => new Slice(x.Date, x.ProjectId, x.Amount)).ToList(),
                CashRebates = cashRebates.Select(x => new Slice(x.Date, x.ProjectId, x.Amount)).ToList(),
                CashRebateReversals = cashRebateReversals.Select(x => new Slice(x.Date, x.ProjectId, x.Amount)).ToList(),
                NonCashCredits = nonCashCredits.Select(x => new Slice(x.Date, x.ProjectId, x.Amount)).ToList(),
                NonCashCreditReversals = nonCashCreditReversals.Select(x => new Slice(x.Date, x.ProjectId, x.Amount)).ToList(),
                LoanInterest = loanInterest.Select(x => new Slice(x.Date, null, x.Amount)).ToList(),
                FixedAssetPurchases = fixedAssets.Select(x => new Slice(x.Date, x.ProjectId, x.Amount)).ToList()
            };
        }

        // ── Trend ────────────────────────────────────────────────────────────────────

        private static List<FinanceTrendBucketDto> BuildTrend(
            DateTime? fromValue, DateTime? toValue, PeriodAggregates aggregates)
        {
            var revenue = aggregates.RevenueSlices.ToList();
            var expense = aggregates.CostSlices.ToList();

            // With no range selected the cards cover everything, so the chart has to as well. The
            // extent comes from the rows already fetched — asking the database for a min and a max
            // would be two more round trips for something the data in hand already states.
            var all = revenue.Concat(expense).ToList();
            var start = fromValue ?? (all.Count > 0 ? all.Min(p => p.Date).Date : (DateTime?)null);
            var end = toValue ?? (all.Count > 0 ? all.Max(p => p.Date).Date : (DateTime?)null);
            if (start is null || end is null || end < start) return [];

            return BuildBucketWindows(start.Value, end.Value).Select(b => new FinanceTrendBucketDto
            {
                Label = b.Label,
                From = b.From,
                To = b.To,
                Revenue = Money(revenue.Where(p => p.Date.Date >= b.From && p.Date.Date <= b.To).Sum(p => p.Amount)),
                Expense = Money(expense.Where(p => p.Date.Date >= b.From && p.Date.Date <= b.To).Sum(p => p.Amount))
            }).ToList();
        }

        /// <summary>
        /// Contiguous, non-overlapping windows covering <paramref name="start"/> to
        /// <paramref name="end"/> inclusive — every day in the range belongs to exactly one of them.
        /// <para>
        /// The granularity widens until at most <see cref="MaxTrendBuckets"/> windows are needed.
        /// That is the whole point: a long range is AGGREGATED into fewer, wider bars, never sampled
        /// down to every n-th bar. Sampling leaves the skipped months out of a chart that still sits
        /// under a card covering them, so the bars and the total silently describe different periods.
        /// </para>
        /// <para>
        /// Each candidate width is measured rather than assumed, because a calendar block count does
        /// not follow from a day count: 365 days starting mid-March touches thirteen months.
        /// </para>
        /// </summary>
        private static List<(string Label, DateTime From, DateTime To)> BuildBucketWindows(DateTime start, DateTime end)
        {
            var spanDays = (end - start).Days + 1;
            if (spanDays <= MaxTrendBuckets) return DayBuckets(start, end);
            if (spanDays <= MaxTrendBuckets * 7) return WeekBuckets(start, end);
            foreach (var months in new[] { 1, 3, 6, 12 })
            {
                var blocks = MonthBuckets(start, end, months);
                if (blocks.Count <= MaxTrendBuckets) return blocks;
            }
            // Longer than twelve years: whole-year blocks widened until they fit.
            var years = end.Year - start.Year + 1;
            return MonthBuckets(start, end, 12 * (int)Math.Ceiling(years / (double)MaxTrendBuckets));
        }

        private static List<(string Label, DateTime From, DateTime To)> DayBuckets(DateTime start, DateTime end)
        {
            var buckets = new List<(string, DateTime, DateTime)>();
            for (var day = start; day <= end; day = day.AddDays(1))
                buckets.Add((day.ToString("d MMM"), day, day));
            return buckets;
        }

        private static List<(string Label, DateTime From, DateTime To)> WeekBuckets(DateTime start, DateTime end)
        {
            var buckets = new List<(string, DateTime, DateTime)>();
            var index = 1;
            for (var weekStart = start; weekStart <= end; weekStart = weekStart.AddDays(7))
            {
                var weekEnd = weekStart.AddDays(6);
                buckets.Add(("Wk " + index, weekStart, weekEnd > end ? end : weekEnd));
                index++;
            }
            return buckets;
        }

        /// <summary>
        /// Calendar-aligned month blocks. Aligned so that a 3-month block is a real calendar quarter
        /// and a 12-month block a real calendar year — a chart whose "Jan–Mar" bar actually ran from
        /// the 14th of February would be worse than useless for comparing one period against another.
        /// The first and last blocks are clipped to the requested range, so no day outside it is ever
        /// counted and no day inside it is ever missed.
        /// </summary>
        private static List<(string Label, DateTime From, DateTime To)> MonthBuckets(DateTime start, DateTime end, int months)
        {
            var buckets = new List<(string, DateTime, DateTime)>();
            var anchor = months >= 12
                ? new DateTime(start.Year, 1, 1)
                : new DateTime(start.Year, (start.Month - 1) / months * months + 1, 1);
            for (var blockStart = anchor; blockStart <= end; blockStart = blockStart.AddMonths(months))
            {
                var blockEnd = blockStart.AddMonths(months).AddDays(-1);
                buckets.Add((MonthBlockLabel(blockStart, months),
                    blockStart < start ? start : blockStart,
                    blockEnd > end ? end : blockEnd));
            }
            return buckets;
        }

        private static string MonthBlockLabel(DateTime blockStart, int months) => months switch
        {
            1 => blockStart.ToString("MMM yy"),
            12 => blockStart.ToString("yyyy"),
            > 12 => $"{blockStart:yyyy}–{blockStart.AddMonths(months - 1):yyyy}",
            _ => $"{blockStart:MMM}–{blockStart.AddMonths(months - 1):MMM yy}"
        };

        // ── Revenue by project ───────────────────────────────────────────────────────

        /// <summary>
        /// The revenue card, split by project. Folded out of the SAME slices the card is summed from
        /// — not read again per project, which is the cost that used to grow without a bound as the
        /// client added developments. One extra query, for the names of the projects that appeared.
        /// </summary>
        private async Task<List<FinanceDistributionSliceDto>> DistributionAsync(
            PeriodAggregates aggregates, CancellationToken cancellationToken)
        {
            var byProject = aggregates.RevenueSlices.ToList();
            var ids = byProject.Where(x => x.ProjectId.HasValue).Select(x => x.ProjectId!.Value).Distinct().ToList();
            var names = ids.Count == 0
                ? []
                : await _context.Projects.AsNoTracking().Where(p => ids.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, p => p.ProjectName, cancellationToken);

            var slices = byProject
                .GroupBy(x => x.ProjectId)
                .Select(g => new FinanceDistributionSliceDto
                {
                    // Revenue that belongs to no project is real revenue — manual entries recorded
                    // against the business rather than a development — so it is named and shown
                    // rather than dropped, which would leave the slices short of the card above.
                    Name = g.Key.HasValue
                        ? names.TryGetValue(g.Key.Value, out var name) ? name : "Project " + g.Key.Value
                        : "General Operations",
                    Revenue = Money(g.Sum(x => x.Amount))
                })
                .Where(s => s.Revenue > 0m)
                .OrderByDescending(s => s.Revenue)
                .ToList();

            if (slices.Count > NamedDistributionSlices)
            {
                var restRevenue = Money(slices.Skip(NamedDistributionSlices).Sum(s => s.Revenue));
                slices = slices.Take(NamedDistributionSlices).ToList();
                if (restRevenue > 0m)
                    slices.Add(new FinanceDistributionSliceDto { Name = "Other Projects", Revenue = restRevenue });
            }

            var total = Money(aggregates.TotalRevenue);
            var denominator = total > 0m ? total : 1m;
            foreach (var slice in slices) slice.Percent = Money(slice.Revenue / denominator * 100m);
            return slices;
        }

        // ── Date bounds ──────────────────────────────────────────────────────────────

        /// <summary>
        /// The bounds every dashboard, summary and drill-down date filter has to satisfy. Shared so
        /// the cards, the bars and the tables cannot read the same querystring differently.
        /// <para>
        /// Three separate failures, one rule. A half-open range (<c>From</c> without <c>To</c>) used
        /// to be read as "from that date" by the cards and as "all time" by the chart. A backwards
        /// range was quietly re-interpreted as the financial year by the chart while the cards
        /// honoured the inversion. And a date outside SQL Server's <c>datetime</c> range — or a
        /// <c>To</c> of 31 Dec 9999, whose exclusive end cannot be represented at all — reached the
        /// database or the arithmetic and came back to the operator as a 500. A filter the server
        /// cannot answer is a bad request, not a fault.
        /// </para>
        /// </summary>
        public static void EnsureFilterRange(DateTime? from, DateTime? to)
        {
            if (from.HasValue != to.HasValue)
                throw new InvalidOperationException(
                    "Give both a From and a To date, or leave both empty. A half-open range would be "
                    + "read one way by the totals and another way by the charts.");
            EnsureFilterBound(from, "From date");
            EnsureFilterBound(to, "To date");
            if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
                throw new InvalidOperationException("From date cannot be after To date.");
        }

        /// <summary>The earliest day a filter may start on: SQL Server's <c>datetime</c> floor.</summary>
        internal static DateTime MinFilterDate => FinanceDateRules.SqlMin;

        /// <summary>The latest day a filter may end on: one short of <see cref="DateTime.MaxValue"/>'s
        /// date, because every query compares against <c>To + 1 day</c>.</summary>
        internal static readonly DateTime MaxFilterDate = DateTime.MaxValue.Date.AddDays(-1);

        private static void EnsureFilterBound(DateTime? value, string field)
        {
            if (!value.HasValue) return;
            var date = value.Value.Date;
            if (date < FinanceDateRules.SqlMin)
                throw new InvalidOperationException(
                    $"{field} cannot be before {FinanceDateRules.SqlMin:dd MMM yyyy}. SQL Server cannot store it.");
            if (date > MaxFilterDate)
                throw new InvalidOperationException(
                    $"{field} cannot be after {MaxFilterDate:dd MMM yyyy}.");
        }

        /// <summary>
        /// The exclusive upper bound of a filter: the day after <paramref name="to"/>. Every caller
        /// goes through this rather than calling <c>AddDays(1)</c> itself, so the one date where that
        /// overflows is refused as a bad request instead of throwing an
        /// <see cref="ArgumentOutOfRangeException"/> out of a query builder.
        /// </summary>
        internal static DateTime? ExclusiveEnd(DateTime? to)
        {
            if (!to.HasValue) return null;
            EnsureFilterBound(to, "To date");
            return to.Value.Date.AddDays(1);
        }
    }
}
