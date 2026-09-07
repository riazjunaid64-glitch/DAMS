using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public partial class FinanceService
    {
        /// <summary>
        /// The Trial Balance position of ONE row, as at one date, without building the report.
        /// <para>
        /// Opening an account's details used to rebuild the entire Trial Balance to read a single row
        /// out of it, so every source in the system — payments, revenue, expenses, assets, commissions,
        /// rebates, tax, capital, loans, staff cash, refunds, deposits, receivables and the whole
        /// P&amp;L — was aggregated for every account and then thrown away. This asks the same
        /// primitives for the one row instead: the account-side and P&amp;L-side loaders are shared with
        /// the report and are handed a <see cref="TrialSourceScope"/> that narrows their queries, so
        /// the accounting rules live in exactly one place and only the reach of the SQL changes.
        /// </para>
        /// <para>
        /// Returns null when the report would not have carried the row at all — an account key that
        /// names nothing, a P&amp;L head with no movement, or an allocation row under a project filter.
        /// The caller turns that into the same "not available for these filters" refusal the lookup
        /// against the full report used to produce.
        /// </para>
        /// </summary>
        private async Task<TrialBalanceRowDto?> BuildTrialBalanceRowAsync(
            TrialBalanceKey key,
            int? projectId,
            DateTime date,
            DateTime? openingDate,
            CancellationToken cancellationToken)
        {
            var finalEnd = date.AddDays(1);
            return key.Kind switch
            {
                TrialBalanceKeyKind.PhysicalAccount =>
                    await BuildPhysicalTrialRowAsync(key, projectId, date, finalEnd, openingDate, cancellationToken),
                TrialBalanceKeyKind.PnlLine =>
                    await BuildPnlTrialRowAsync(key, projectId, date, finalEnd, openingDate, cancellationToken),
                _ => await BuildAllocationTrialRowAsync(key, projectId, finalEnd, cancellationToken),
            };
        }

        /// <summary>
        /// One physical account. Reproduces the report's fold for that account exactly: the opening
        /// baseline, the one delta bucket its type is paid out of, and the same rounding.
        /// </summary>
        private async Task<TrialBalanceRowDto?> BuildPhysicalTrialRowAsync(
            TrialBalanceKey key, int? projectId, DateTime date, DateTime finalEnd,
            DateTime? openingDate, CancellationToken cancellationToken)
        {
            var template = await _context.FinanceAccounts.AsNoTracking()
                .Where(a => a.Id == key.AccountId)
                .Select(a => new AccountSnapshot
                {
                    Id = a.Id,
                    Name = a.Name,
                    LedgerCode = a.LedgerCode,
                    Type = a.Type,
                    SystemRole = a.SystemRole,
                    DisplayOrder = a.DisplayOrder,
                    Balance = a.OpeningBalance
                }).SingleOrDefaultAsync(cancellationToken);
            if (template is null) return null;

            var templates = new List<AccountSnapshot> { template };
            // The report reads capital balances out of the partner bucket and every other account out
            // of the normal one, so only one of the two is worth loading here.
            var kind = template.Type == FinanceAccountType.Capital
                ? TrialAccountDeltaKind.CapitalPartner
                : TrialAccountDeltaKind.Normal;

            // Two accounts are built from P&L rows rather than from a cash movement: the receivable is
            // raised by recognised sales and relieved by non-cash credits, and Commission Payable is
            // raised by the accrual. No other account reads the P&L at all.
            var pnlSources = new HashSet<TrialPnlSource>();
            if (template.SystemRole == FinanceSystemAccountRole.CustomerReceivables)
            {
                pnlSources.Add(TrialPnlSource.UnitSales);
                pnlSources.Add(TrialPnlSource.NonCashCredits);
            }
            if (template.SystemRole == FinanceSystemAccountRole.CommissionPayable)
                pnlSources.Add(TrialPnlSource.CommissionAccrual);

            var scope = new TrialSourceScope(template.Id, kind, pnlSources, null);
            var pnlDeltas = pnlSources.Count == 0
                ? []
                : await LoadTrialPnlDeltasAsync(projectId, finalEnd, scope, cancellationToken);
            var accountDeltas = await LoadTrialAccountDeltasAsync(
                projectId, finalEnd, templates, scope, cancellationToken);
            await AddCustomerTrialDeltasAsync(
                projectId, finalEnd, templates, pnlDeltas, accountDeltas, cancellationToken);
            AddCommissionPayableTrialDeltas(
                templates.Where(a => a.SystemRole == FinanceSystemAccountRole.CommissionPayable)
                    .Select(a => a.Id).ToList(),
                pnlDeltas, accountDeltas);

            // The date bound matters as much as the account: a non-cash credit is dated at the sale it
            // belongs to, which can fall after the day it was applied and therefore after this column.
            var movement = accountDeltas
                .Where(d => d.AccountId == template.Id && d.Kind == kind && d.Date < finalEnd)
                .Sum(d => d.Amount);

            var opening = !projectId.HasValue
                && (!openingDate.HasValue || openingDate.Value <= date)
                    ? template.Balance
                    : 0m;
            var balance = opening + movement;
            // Matches the report: non-capital balances round here, capital balances round in the cell.
            if (template.Type != FinanceAccountType.Capital) balance = Money(balance);

            return TrialRow(key.AccountKey, template.Id, template.LedgerCode, template.Name,
                template.Type, DebitOf(template.Type, balance), CreditOf(template.Type, balance));
        }

        /// <summary>
        /// One virtual income or expense head. Only the source that can produce this key is read, and
        /// the same start rule the report applies — the opening baseline when there is one, otherwise
        /// the SQL floor — decides which movements count.
        /// </summary>
        private async Task<TrialBalanceRowDto?> BuildPnlTrialRowAsync(
            TrialBalanceKey key, int? projectId, DateTime date, DateTime finalEnd,
            DateTime? openingDate, CancellationToken cancellationToken)
        {
            var scope = new TrialSourceScope(null, null, new HashSet<TrialPnlSource> { key.Source }, key.Line);
            var deltas = await LoadTrialPnlDeltasAsync(projectId, finalEnd, scope, cancellationToken);

            var pnlStart = openingDate.HasValue && openingDate.Value <= date ? openingDate.Value : SqlStart;
            var balances = new Dictionary<TrialPnlKey, ReportLine>();
            foreach (var delta in deltas)
            {
                if (delta.Date < pnlStart || delta.Date >= finalEnd) continue;
                AddTrialPnlDelta(balances, delta);
            }

            var period = BuildTrialPnlPeriod(balances);
            var lines = key.IsIncome ? period.Income : period.Expenses;
            // The report writes these into a dictionary keyed by the line key alone, walking the list
            // in the order BuildTrialPnlPeriod returns, so a repeated key keeps the last one written.
            var line = lines.LastOrDefault(candidate => candidate.Key == key.PnlKey);
            if (line is null) return null;

            // A contra head carries a negative amount, and that has to cross to the opposite column
            // rather than sit as a negative cell — the same rule the report applies.
            var debit = key.IsIncome
                ? (line.Amount < 0m ? -line.Amount : 0m)
                : (line.Amount < 0m ? 0m : line.Amount);
            var credit = key.IsIncome
                ? (line.Amount < 0m ? 0m : line.Amount)
                : (line.Amount < 0m ? -line.Amount : 0m);
            return TrialRow(key.AccountKey, VirtualId(key.AccountKey), null, line.Name,
                FinanceAccountType.Other, debit, credit);
        }

        /// <summary>
        /// The allocated profit / loss row. The report only carries it for the whole business and only
        /// once something has actually been allocated.
        /// </summary>
        private async Task<TrialBalanceRowDto?> BuildAllocationTrialRowAsync(
            TrialBalanceKey key, int? projectId, DateTime finalEnd, CancellationToken cancellationToken)
        {
            if (projectId.HasValue) return null;

            var totals = await _context.CapitalTransactions.AsNoTracking()
                .Where(t => t.Date < finalEnd
                    && (t.Type == CapitalTransactionType.ProfitShare
                        || t.Type == CapitalTransactionType.LossShare))
                .GroupBy(t => t.Type)
                .Select(g => new { Type = g.Key, Amount = g.Sum(t => t.Amount) })
                .ToListAsync(cancellationToken);

            var allocatedProfit = totals
                .Where(t => t.Type == CapitalTransactionType.ProfitShare).Sum(t => t.Amount);
            var allocatedLoss = totals
                .Where(t => t.Type == CapitalTransactionType.LossShare).Sum(t => t.Amount);
            if (allocatedProfit == 0m && allocatedLoss == 0m) return null;

            var net = Money(allocatedProfit - allocatedLoss);
            return TrialRow(key.AccountKey, VirtualId(key.AccountKey), null, "Allocated profit / loss",
                FinanceAccountType.Capital, net > 0m ? net : 0m, net < 0m ? -net : 0m);
        }

        private static decimal DebitOf(FinanceAccountType type, decimal balance)
        {
            var debitNormal = AccountBalanceDirection.IsDebitNormal(type);
            return balance >= 0m ? (debitNormal ? balance : 0m) : (debitNormal ? 0m : -balance);
        }

        private static decimal CreditOf(FinanceAccountType type, decimal balance)
        {
            var debitNormal = AccountBalanceDirection.IsDebitNormal(type);
            return balance >= 0m ? (debitNormal ? 0m : balance) : (debitNormal ? -balance : 0m);
        }

        /// <summary>Built through the report's own row builder so the shape cannot drift from it.</summary>
        private static TrialBalanceRowDto TrialRow(
            string accountKey, int accountId, string? ledgerCode, string name,
            FinanceAccountType type, decimal debit, decimal credit)
        {
            var builder = new TrialRowBuilder(accountKey, accountId, ledgerCode, name, type);
            builder.Add(Money(debit), Money(credit));
            return builder.Build();
        }

        private enum TrialBalanceKeyKind { PhysicalAccount, PnlLine, Allocation }

        /// <summary>
        /// An opaque Trial Balance account key, resolved to what has to be calculated for it. Parsing
        /// it up front is also what validates it — an unrecognised key is refused before any query runs.
        /// </summary>
        private sealed record TrialBalanceKey(
            TrialBalanceKeyKind Kind,
            string AccountKey,
            int AccountId,
            bool IsIncome,
            string PnlKey,
            TrialPnlLine? Line,
            TrialPnlSource Source);

        private static bool TryParseTrialBalanceKey(string accountKey, out TrialBalanceKey key)
        {
            key = new TrialBalanceKey(TrialBalanceKeyKind.Allocation, accountKey, 0, false,
                string.Empty, null, TrialPnlSource.ManualRevenue);

            if (TryPhysicalAccountId(accountKey, out var accountId))
            {
                key = key with { Kind = TrialBalanceKeyKind.PhysicalAccount, AccountId = accountId };
                return true;
            }
            if (accountKey == "EQ:allocated") return true;

            var isIncome = accountKey.StartsWith("I:", StringComparison.Ordinal);
            if (!isIncome && !accountKey.StartsWith("E:", StringComparison.Ordinal)) return false;
            var pnlKey = accountKey[2..];
            if (pnlKey.Length == 0) return false;

            // Each fixed key belongs to exactly one source. Everything else is a category head, whose
            // key is always "{category or U}:{name}" and so can never collide with the fixed ones.
            var source = isIncome
                ? pnlKey switch
                {
                    "unit-sales" => TrialPnlSource.UnitSales,
                    "cancellation-retained" => TrialPnlSource.CancellationRetained,
                    _ => TrialPnlSource.ManualRevenue
                }
                : pnlKey switch
                {
                    "commission-expense" => TrialPnlSource.CommissionAccrual,
                    "cash-rebates" => TrialPnlSource.CashRebates,
                    "non-cash-credits" => TrialPnlSource.NonCashCredits,
                    "loan-interest" => TrialPnlSource.LoanInterest,
                    _ => TrialPnlSource.Expenses
                };

            TrialPnlLine? line = null;
            if (source is TrialPnlSource.ManualRevenue or TrialPnlSource.Expenses)
            {
                // The category prefix is written first, so the first colon is always the separator
                // even when the head's own name contains one.
                var separator = pnlKey.IndexOf(':');
                if (separator <= 0 || separator == pnlKey.Length - 1) return false;
                var prefix = pnlKey[..separator];
                int? categoryId;
                if (prefix == "U") categoryId = null;
                else if (int.TryParse(prefix, out var parsed)) categoryId = parsed;
                else return false;
                line = new TrialPnlLine(categoryId, pnlKey[(separator + 1)..]);
            }

            key = key with
            {
                Kind = TrialBalanceKeyKind.PnlLine,
                IsIncome = isIncome,
                PnlKey = pnlKey,
                Line = line,
                Source = source
            };
            return true;
        }

        /// <summary>
        /// How much of the Trial Balance sources a load has to read. <see cref="Everything"/> is the
        /// full report; a Details request narrows to one row and every source query applies the
        /// narrowing itself rather than being filtered after the fact.
        /// </summary>
        private sealed record TrialSourceScope(
            int? AccountId,
            TrialAccountDeltaKind? Kind,
            IReadOnlySet<TrialPnlSource>? PnlSources,
            TrialPnlLine? PnlLine)
        {
            public static readonly TrialSourceScope Everything = new(null, null, null, null);
            public bool Wants(TrialAccountDeltaKind kind) => Kind is null || Kind == kind;
            public bool Wants(TrialPnlSource source) => PnlSources is null || PnlSources.Contains(source);
        }

        /// <summary>The category head a virtual P&amp;L row names, as its source query can filter it.</summary>
        private sealed record TrialPnlLine(int? CategoryId, string Name);

        private enum TrialPnlSource
        {
            ManualRevenue,
            UnitSales,
            CancellationRetained,
            Expenses,
            CommissionAccrual,
            CashRebates,
            NonCashCredits,
            LoanInterest
        }
    }
}
