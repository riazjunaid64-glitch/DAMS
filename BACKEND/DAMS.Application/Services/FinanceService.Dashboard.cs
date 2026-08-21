using DAMS.Application.DTOs.FinanceDtos;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    /// <summary>
    /// Everything the Finance dashboard screen draws — the cards, the revenue/expense trend and the
    /// revenue-by-project split — answered in ONE request against ONE set of date bounds.
    /// <para>
    /// It exists because the screen used to assemble itself client-side out of the summary endpoint:
    /// one full summary computation per chart bucket, another per project, one for the distribution
    /// total and one for the cards. Twelve buckets and ten projects meant twenty-four summaries, and
    /// a summary is not one aggregate — it is twenty-odd of them. The database work therefore grew
    /// with the project list, without a cap, for a screen an admin refreshes all day. Here each
    /// source is read ONCE, grouped by date in SQL, and bucketed in memory, so the query count is
    /// flat in both the number of buckets and the number of projects.
    /// </para>
    /// <para>
    /// The second reason is agreement. The buckets are built here, from the same bounds the cards
    /// use, so the chart cannot end up describing a different period from the figures above it.
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
            var toExclusive = toValue?.AddDays(1);
            EnsureFilterRange(fromValue, toValue);

            var summary = await GetSummaryAsync(projectId, fromValue, toValue, accountId, unassigned, cancellationToken);

            // One grouped read per source, over the whole window, rather than one summary per bucket.
            var revenuePoints = await RevenueByDateAsync(projectId, fromValue, toExclusive, accountId, unassigned, cancellationToken);
            var expensePoints = await ExpenseByDateAsync(projectId, fromValue, toExclusive, accountId, unassigned, cancellationToken);

            var trend = BuildTrend(fromValue, toValue, revenuePoints, expensePoints);
            var distribution = await DistributionAsync(projectId, fromValue, toExclusive, accountId, unassigned,
                summary.TotalRevenue, cancellationToken);

            return new FinanceDashboardDataDto { Summary = summary, Trend = trend, Distribution = distribution };
        }

        // ── Trend ────────────────────────────────────────────────────────────────────
        // Every component of the two cards, dated exactly as the cards date it, so a bar and the
        // card above it are the same arithmetic over a narrower window.

        private async Task<List<DateAmount>> RevenueByDateAsync(int? projectId, DateTime? fromValue,
            DateTime? toExclusive, int? accountId, bool unassigned, CancellationToken cancellationToken)
        {
            var points = new List<DateAmount>();
            points.AddRange((await SaleRecognitionQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .GroupBy(r => r.RecognitionDate)
                .Select(g => new { Date = g.Key, Amount = g.Sum(r => r.NetSaleValue) })
                .ToListAsync(cancellationToken)).Select(x => new DateAmount(x.Date, x.Amount)));
            points.AddRange((await RetainedCancellationQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .GroupBy(s => s.CancellationDate)
                .Select(g => new { Date = g.Key, Amount = g.Sum(s => s.RetainedAmount) })
                .ToListAsync(cancellationToken)).Select(x => new DateAmount(x.Date, x.Amount)));
            points.AddRange((await ManualQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .GroupBy(r => r.Date)
                .Select(g => new { Date = g.Key, Amount = g.Sum(r => r.Amount) })
                .ToListAsync(cancellationToken)).Select(x => new DateAmount(x.Date, x.Amount)));
            return points;
        }

        private async Task<List<DateAmount>> ExpenseByDateAsync(int? projectId, DateTime? fromValue,
            DateTime? toExclusive, int? accountId, bool unassigned, CancellationToken cancellationToken)
        {
            var points = new List<DateAmount>();
            points.AddRange((await ExpenseQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .GroupBy(e => e.Date)
                .Select(g => new { Date = g.Key, Amount = g.Sum(e => e.Amount) })
                .ToListAsync(cancellationToken)).Select(x => new DateAmount(x.Date, x.Amount)));
            points.AddRange((await CommissionPayoutQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .GroupBy(p => p.PaymentDate)
                .Select(g => new { Date = g.Key, Amount = g.Sum(p => p.Amount) })
                .ToListAsync(cancellationToken)).Select(x => new DateAmount(x.Date, x.Amount)));
            points.AddRange((await CommissionReversalQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .GroupBy(r => r.ReversedAt)
                .Select(g => new { Date = g.Key, Amount = g.Sum(r => r.Amount) })
                .ToListAsync(cancellationToken)).Select(x => new DateAmount(x.Date, -x.Amount)));
            points.AddRange((await CashRebateQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .GroupBy(d => d.AppliedAt)
                .Select(g => new { Date = g.Key, Amount = g.Sum(d => d.Amount) })
                .ToListAsync(cancellationToken)).Select(x => new DateAmount(x.Date, x.Amount)));
            points.AddRange((await CashRebateReversalQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .GroupBy(r => r.ReversedAt)
                .Select(g => new { Date = g.Key, Amount = g.Sum(r => r.Amount) })
                .ToListAsync(cancellationToken)).Select(x => new DateAmount(x.Date, -x.Amount)));
            // Dated at the later of the credit and the recognition it reduces, matching the summary
            // and both drill-downs: a credit is not a cost before the sale it writes down is income.
            points.AddRange((await NonCashCreditQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .GroupBy(d => d.AppliedAt < d.Rebate.Booking.SaleRecognition!.RecognitionDate.AddDays(1)
                    ? d.Rebate.Booking.SaleRecognition!.RecognitionDate : d.AppliedAt)
                .Select(g => new { Date = g.Key, Amount = g.Sum(d => d.Amount) })
                .ToListAsync(cancellationToken)).Select(x => new DateAmount(x.Date, x.Amount)));
            points.AddRange((await NonCashCreditReversalQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .GroupBy(r => r.ReversedAt < r.Disbursement.Rebate.Booking.SaleRecognition!.RecognitionDate.AddDays(1)
                    ? r.Disbursement.Rebate.Booking.SaleRecognition!.RecognitionDate : r.ReversedAt)
                .Select(g => new { Date = g.Key, Amount = g.Sum(r => r.Amount) })
                .ToListAsync(cancellationToken)).Select(x => new DateAmount(x.Date, -x.Amount)));
            points.AddRange((await LoanInterestQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .GroupBy(t => t.Date)
                .Select(g => new { Date = g.Key, Amount = g.Sum(t => t.InterestAmount) })
                .ToListAsync(cancellationToken)).Select(x => new DateAmount(x.Date, x.Amount)));
            // Fixed assets are inside Total Expenses and inside Net Profit, so they are inside the
            // expense bar too. A chart that left them out would not add up to the card above it.
            points.AddRange((await FixedAssetChargeQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .GroupBy(p => p.Date)
                .Select(g => new { Date = g.Key, Amount = g.Sum(p => p.Amount) })
                .ToListAsync(cancellationToken)).Select(x => new DateAmount(x.Date, x.Amount)));
            return points;
        }

        private static List<FinanceTrendBucketDto> BuildTrend(
            DateTime? fromValue, DateTime? toValue, List<DateAmount> revenue, List<DateAmount> expense)
        {
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

        private async Task<List<FinanceDistributionSliceDto>> DistributionAsync(
            int? projectId, DateTime? fromValue, DateTime? toExclusive, int? accountId, bool unassigned,
            decimal totalRevenue, CancellationToken cancellationToken)
        {
            // One grouped query per revenue source — NOT one summary per project. The old shape added
            // a whole summary computation for every project on the books, which is the cost that grew
            // without a bound as the client added projects.
            var byProject = new List<(int? ProjectId, decimal Amount)>();
            byProject.AddRange((await SaleRecognitionQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .GroupBy(r => (int?)r.Booking.Unit.ProjectId)
                .Select(g => new { g.Key, Amount = g.Sum(r => r.NetSaleValue) })
                .ToListAsync(cancellationToken)).Select(x => (x.Key, x.Amount)));
            byProject.AddRange((await RetainedCancellationQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .GroupBy(s => (int?)s.Booking.Unit.ProjectId)
                .Select(g => new { g.Key, Amount = g.Sum(s => s.RetainedAmount) })
                .ToListAsync(cancellationToken)).Select(x => (x.Key, x.Amount)));
            byProject.AddRange((await ManualQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .GroupBy(r => r.ProjectId)
                .Select(g => new { g.Key, Amount = g.Sum(r => r.Amount) })
                .ToListAsync(cancellationToken)).Select(x => (x.Key, x.Amount)));

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

            var denominator = totalRevenue > 0m ? totalRevenue : 1m;
            foreach (var slice in slices) slice.Percent = Money(slice.Revenue / denominator * 100m);
            return slices;
        }

        /// <summary>
        /// A dashboard date filter is either both ends or neither. One end alone, or a From after its
        /// To, used to be accepted by the cards and quietly re-interpreted by the chart — the screen
        /// then showed a total for one period beside bars for another, with nothing saying so.
        /// </summary>
        public static void EnsureFilterRange(DateTime? from, DateTime? to)
        {
            if (from.HasValue != to.HasValue)
                throw new InvalidOperationException(
                    "Give both a From and a To date, or leave both empty. A half-open range would be "
                    + "read one way by the totals and another way by the charts.");
            if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
                throw new InvalidOperationException("From date cannot be after To date.");
        }

        private readonly record struct DateAmount(DateTime Date, decimal Amount);
    }
}
