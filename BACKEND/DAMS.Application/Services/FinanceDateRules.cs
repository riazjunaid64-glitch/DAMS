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
    }
}
