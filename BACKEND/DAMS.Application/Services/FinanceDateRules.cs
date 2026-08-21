using DAMS.Application.Common;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    /// <summary>
    /// The three bounds every financial posting date has to satisfy, in one place.
    /// <para>
    /// This is not a formatting rule — the whole derived-balance model depends on it. An account's
    /// balance is <c>OpeningBalance + every movement ever recorded</c>, and the reports read the
    /// committed opening balance as an AS-AT baseline: the P&amp;L and Trial Balance windows start at
    /// <c>AsAtDate</c> precisely because everything before it is already inside the figure the
    /// accountant typed. A row dated before that baseline is therefore counted twice — once inside
    /// the opening balance and once as a movement — and, because the P&amp;L window excludes it while
    /// the cash movement does not, it also puts the Balance Sheet out by its own amount.
    /// </para>
    /// <para>
    /// A future-dated row is the mirror image: nothing filters movements to "on or before today", so
    /// next month's payment reduces today's outstanding balance and today's bank balance the moment
    /// it is saved. Either post-dating is genuinely scheduled (a different feature, with its own
    /// pending state) or it is not allowed; a halfway house that silently moves today's figures is
    /// the one option that cannot be defended to an auditor.
    /// </para>
    /// <para>
    /// Loans and staff-cash transfers have always enforced exactly this. Expenses, revenue, asset
    /// purchases, customer receipts and FBR deposits did not, which is the inconsistency this type
    /// removes — same rule, same wording, one implementation.
    /// </para>
    /// </summary>
    internal static class FinanceDateRules
    {
        /// <summary>SQL Server's <c>datetime</c> floor, and the Trial Balance's own lower bound. A
        /// date the database accepts but no report can address is a row that silently never
        /// appears anywhere.</summary>
        public static readonly DateTime SqlMin = new(1753, 1, 1);

        /// <summary>
        /// Where the boundary day itself falls, stated once because "as at 31 July" alone does not
        /// say it. <c>AsAtDate</c> is the FIRST day DAMS records movements for: the opening figures
        /// are the position at the start of that day, the P&amp;L and Trial Balance windows open on it,
        /// and a posting dated on it is therefore new activity, not part of the baseline. Hence the
        /// bound below is <c>&lt;</c> and not <c>&lt;=</c>, and <see cref="PreBaselineEventsAsync"/>
        /// looks for records strictly before the same instant. All three have to agree; if the
        /// accountant means the balances to INCLUDE the boundary day, the go-live date is the next
        /// day, not a different comparison here.
        /// </summary>
        public const string BoundaryConvention =
            "Opening balances are the position at the START of the go-live date. That date is the "
            + "first day DAMS records movements for, so entries dated on it are new activity.";

        /// <summary>
        /// The committed opening-balance date, or null when the client has not committed a baseline
        /// yet. Only a committed set counts: a draft can still be edited to any date.
        /// </summary>
        public static Task<DateTime?> BaselineAsync(AppDbContext context, CancellationToken cancellationToken) =>
            context.OpeningBalanceSets.AsNoTracking().Where(s => s.CommittedAt != null)
                .OrderByDescending(s => s.AsAtDate)
                .Select(s => (DateTime?)s.AsAtDate)
                .FirstOrDefaultAsync(cancellationToken);

        /// <summary>
        /// Validates and normalises the business date a financial record belongs to. Omitting the
        /// date means today — and today is the PAKISTAN date, never the UTC one.
        /// </summary>
        public static async Task<DateTime> ResolveAsync(
            AppDbContext context, DateTime? date, string field, CancellationToken cancellationToken)
        {
            var value = date?.Date ?? PakistanTime.Today;
            await EnsureAsync(context, value, field, cancellationToken);
            return value;
        }

        /// <summary>
        /// The same rules for a column that keeps its time of day (a receipt's <c>PaidAt</c>): the
        /// bounds are judged on the date part, and the instant itself is preserved so the receipt
        /// still says when the cash was taken.
        /// </summary>
        public static async Task<DateTime> ResolveInstantAsync(
            AppDbContext context, DateTime? instant, string field, CancellationToken cancellationToken)
        {
            var value = instant ?? PakistanTime.Now;
            await EnsureAsync(context, value.Date, field, cancellationToken);
            return value;
        }

        /// <summary>
        /// The two bounds that apply to a date with no baseline behind it — the go-live date itself.
        /// <para>
        /// The baseline cannot be judged against the baseline, so <see cref="EnsureAsync"/> is the
        /// wrong rule for it; but the other two bounds matter more here than anywhere else. A future
        /// go-live date is committed as the position at the start of a day that has not happened,
        /// and every posting between today and that date is then rejected for being "before the
        /// committed opening balance date" — the business is locked out of its own finance module
        /// until the calendar catches up.
        /// </para>
        /// </summary>
        public static void EnsureBaselineDate(DateTime date, string field)
        {
            var value = date.Date;
            if (value < SqlMin)
                throw new InvalidOperationException($"{field} cannot be before {SqlMin:dd MMM yyyy}.");
            if (value > PakistanTime.Today)
                throw new InvalidOperationException(
                    $"{field} cannot be in the future. Opening balances are the position at the start of "
                    + "a day that has already begun, and every entry dated before the go-live date is "
                    + "refused — so a future date would block all finance entry until it arrives.");
        }

        /// <summary>Validates an already-resolved business date. Use when the caller owns the value.</summary>
        public static async Task EnsureAsync(
            AppDbContext context, DateTime date, string field, CancellationToken cancellationToken)
        {
            var value = date.Date;
            if (value < SqlMin)
                throw new InvalidOperationException($"{field} cannot be before {SqlMin:dd MMM yyyy}.");
            if (value > PakistanTime.Today)
                throw new InvalidOperationException($"{field} cannot be in the future.");
            var baseline = await BaselineAsync(context, cancellationToken);
            if (baseline.HasValue && value < baseline.Value.Date)
                throw new InvalidOperationException(
                    $"{field} cannot be before the committed opening balance date ({baseline:dd MMM yyyy}). "
                    + "Everything up to that date is already inside the opening balances.");
        }

        /// <summary>One source of financial records found on the wrong side of a proposed baseline.</summary>
        public sealed record PreBaselineEvents(string Label, int Count, DateTime Earliest);

        /// <summary>
        /// Every financial record already in DAMS dated before a proposed baseline, by source.
        /// <para>
        /// <see cref="EnsureAsync"/> stops a pre-baseline row being CREATED, which is only half the
        /// problem: the baseline itself is usually committed onto a database that already holds
        /// history — a pilot month, a migration, real trading before the accountant produced the
        /// cutover trial balance. Those rows are not rejected by anything, because they were legal
        /// when they were written. The reports then add them on top of an opening figure that already
        /// contains them, and every one is counted twice. Nothing downstream can detect this: both
        /// numbers are individually correct, so the Balance Sheet still balances while being wrong.
        /// </para>
        /// <para>
        /// The only place the question can be settled is the commit, which is why this is a
        /// precondition there rather than a warning on a report.
        /// </para>
        /// </summary>
        public static async Task<List<PreBaselineEvents>> PreBaselineEventsAsync(
            AppDbContext context, DateTime asAt, CancellationToken cancellationToken)
        {
            var cutover = asAt.Date;
            // Each source names its own posting date — the same column the reports sum it by, so this
            // covers exactly the set of rows that would be double counted, no more and no less.
            var probes = new[]
            {
                await ProbeAsync("customer receipts", context.Payments
                    .Where(p => p.PaidAt < cutover).Select(p => p.PaidAt), cancellationToken),
                await ProbeAsync("recognised sales", context.BookingSaleRecognitions
                    .Where(r => r.RecognitionDate < cutover).Select(r => r.RecognitionDate), cancellationToken),
                await ProbeAsync("manual revenue entries", context.ManualRevenues
                    .Where(r => r.Date < cutover).Select(r => r.Date), cancellationToken),
                await ProbeAsync("expenses", context.Expenses
                    .Where(e => e.Date < cutover).Select(e => e.Date), cancellationToken),
                await ProbeAsync("fixed-asset purchases", context.AssetPurchases
                    .Where(p => p.Date < cutover).Select(p => p.Date), cancellationToken),
                await ProbeAsync("commission payouts", context.CommissionPayouts
                    .Where(p => p.PaymentDate < cutover).Select(p => p.PaymentDate), cancellationToken),
                await ProbeAsync("commission reversals", context.CommissionPayoutReversals
                    .Where(r => r.ReversedAt < cutover).Select(r => r.ReversedAt), cancellationToken),
                await ProbeAsync("rebate disbursements", context.RebateDisbursements
                    .Where(d => d.AppliedAt < cutover).Select(d => d.AppliedAt), cancellationToken),
                await ProbeAsync("rebate reversals", context.RebateDisbursementReversals
                    .Where(r => r.ReversedAt < cutover).Select(r => r.ReversedAt), cancellationToken),
                await ProbeAsync("withholding tax deposits", context.WhtDeposits
                    .Where(d => d.DepositDate < cutover).Select(d => d.DepositDate), cancellationToken),
                await ProbeAsync("capital transactions", context.CapitalTransactions
                    .Where(t => t.Date < cutover).Select(t => t.Date), cancellationToken),
                await ProbeAsync("loan transactions", context.LoanTransactions
                    .Where(t => t.Date < cutover).Select(t => t.Date), cancellationToken),
                await ProbeAsync("staff cash transfers", context.StaffCashTransfers
                    .Where(t => t.Date < cutover).Select(t => t.Date), cancellationToken),
                await ProbeAsync("booking cancellations", context.BookingCancellationSettlements
                    .Where(s => s.CancellationDate < cutover).Select(s => s.CancellationDate), cancellationToken),
                await ProbeAsync("cancellation refunds", context.BookingCancellationRefunds
                    .Where(r => r.PaidAt < cutover).Select(r => r.PaidAt), cancellationToken),
            };
            return probes.Where(p => p != null).Select(p => p!).ToList();
        }

        private static async Task<PreBaselineEvents?> ProbeAsync(
            string label, IQueryable<DateTime> dates, CancellationToken cancellationToken)
        {
            var found = await dates.GroupBy(_ => 1)
                .Select(g => new { Count = g.Count(), Earliest = g.Min() })
                .FirstOrDefaultAsync(cancellationToken);
            return found == null ? null : new PreBaselineEvents(label, found.Count, found.Earliest);
        }
    }
}
