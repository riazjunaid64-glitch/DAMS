using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public partial class FinanceService
    {
        /// <summary>
        /// Loads every source once up to the final requested column, then folds dated deltas across
        /// the requested cut-offs. The former implementation rebuilt the same cumulative snapshot
        /// and P&amp;L for every column, so 61 columns issued roughly sixty-one times the SQL of one.
        /// </summary>
        private async Task<Dictionary<DateTime, TrialBalanceColumnData>> LoadTrialBalanceColumnsAsync(
            int? projectId,
            IReadOnlyList<DateTime> dates,
            DateTime? openingDate,
            CancellationToken cancellationToken)
        {
            var finalEnd = dates[^1].AddDays(1);
            var templates = await _context.FinanceAccounts.AsNoTracking()
                .OrderBy(a => a.DisplayOrder).ThenBy(a => a.Name)
                .Select(a => new AccountSnapshot
                {
                    Id = a.Id,
                    Name = a.Name,
                    LedgerCode = a.LedgerCode,
                    Type = a.Type,
                    SystemRole = a.SystemRole,
                    DisplayOrder = a.DisplayOrder,
                    Balance = a.OpeningBalance
                }).ToListAsync(cancellationToken);

            var accountDeltas = await LoadTrialAccountDeltasAsync(
                projectId, finalEnd, templates, TrialSourceScope.Everything, cancellationToken);
            var pnlDeltas = await LoadTrialPnlDeltasAsync(
                projectId, finalEnd, TrialSourceScope.Everything, cancellationToken);
            await AddCustomerTrialDeltasAsync(
                projectId, finalEnd, templates, pnlDeltas, accountDeltas, cancellationToken);
            AddCommissionPayableTrialDeltas(
                templates.Where(a => a.SystemRole == FinanceSystemAccountRole.CommissionPayable)
                    .Select(a => a.Id).ToList(),
                pnlDeltas, accountDeltas);

            var allocationDeltas = !projectId.HasValue
                ? await _context.CapitalTransactions.AsNoTracking()
                    .Where(t => t.Date < finalEnd
                        && (t.Type == CapitalTransactionType.ProfitShare
                            || t.Type == CapitalTransactionType.LossShare))
                    .GroupBy(t => new { t.Date, t.Type })
                    .Select(g => new TrialAllocationDelta
                    {
                        Date = g.Key.Date,
                        Type = g.Key.Type,
                        Amount = g.Sum(t => t.Amount)
                    }).ToListAsync(cancellationToken)
                : [];

            var normalBalances = new Dictionary<int, decimal>();
            var capitalBalances = new Dictionary<int, decimal>();
            var orderedAccountDeltas = accountDeltas.OrderBy(d => d.Date).ToList();
            var orderedAllocations = allocationDeltas.OrderBy(d => d.Date).ToList();
            var orderedPnlDeltas = pnlDeltas.OrderBy(d => d.Date).ToList();
            var allPnlBalances = new Dictionary<TrialPnlKey, ReportLine>();
            var baselinePnlBalances = new Dictionary<TrialPnlKey, ReportLine>();
            var accountIndex = 0;
            var allocationIndex = 0;
            var pnlIndex = 0;
            var allocatedProfit = 0m;
            var allocatedLoss = 0m;
            var result = new Dictionary<DateTime, TrialBalanceColumnData>();

            foreach (var columnDate in dates)
            {
                var end = columnDate.AddDays(1);
                while (accountIndex < orderedAccountDeltas.Count
                    && orderedAccountDeltas[accountIndex].Date < end)
                {
                    var delta = orderedAccountDeltas[accountIndex++];
                    var balances = delta.Kind == TrialAccountDeltaKind.CapitalPartner
                        ? capitalBalances
                        : normalBalances;
                    balances[delta.AccountId] = balances.GetValueOrDefault(delta.AccountId) + delta.Amount;
                }
                while (allocationIndex < orderedAllocations.Count
                    && orderedAllocations[allocationIndex].Date < end)
                {
                    var allocation = orderedAllocations[allocationIndex++];
                    if (allocation.Type == CapitalTransactionType.ProfitShare)
                        allocatedProfit += allocation.Amount;
                    else
                        allocatedLoss += allocation.Amount;
                }
                while (pnlIndex < orderedPnlDeltas.Count
                    && orderedPnlDeltas[pnlIndex].Date < end)
                {
                    var delta = orderedPnlDeltas[pnlIndex++];
                    if (delta.Date >= SqlStart)
                        AddTrialPnlDelta(allPnlBalances, delta);
                    if (openingDate.HasValue && delta.Date >= openingDate.Value)
                        AddTrialPnlDelta(baselinePnlBalances, delta);
                }

                var accounts = templates.Select(template =>
                {
                    var opening = !projectId.HasValue
                        && (!openingDate.HasValue || openingDate.Value <= columnDate)
                            ? template.Balance
                            : 0m;
                    var movement = template.Type == FinanceAccountType.Capital
                        ? capitalBalances.GetValueOrDefault(template.Id)
                        : normalBalances.GetValueOrDefault(template.Id);
                    var balance = opening + movement;
                    // AccountSnapshotsAsync rounds non-capital balances at this point; capital
                    // balances flow unrounded into the Trial Balance and are rounded by its cells.
                    if (template.Type != FinanceAccountType.Capital) balance = Money(balance);
                    return new AccountSnapshot
                    {
                        Id = template.Id,
                        Name = template.Name,
                        LedgerCode = template.LedgerCode,
                        Type = template.Type,
                        SystemRole = template.SystemRole,
                        DisplayOrder = template.DisplayOrder,
                        Balance = balance
                    };
                }).ToList();

                var pnlBalances = openingDate.HasValue && openingDate.Value <= columnDate
                    ? baselinePnlBalances
                    : allPnlBalances;
                result[columnDate] = new TrialBalanceColumnData
                {
                    Accounts = accounts,
                    Pnl = BuildTrialPnlPeriod(pnlBalances),
                    NetAllocation = allocatedProfit - allocatedLoss,
                    HasAllocation = allocatedProfit != 0m || allocatedLoss != 0m
                };
            }

            return result;
        }

        /// <summary>
        /// Every account-side source, in one place, for whichever accounts <paramref name="accounts"/>
        /// holds. The Trial Balance passes all of them; a Details request passes only the row it was
        /// asked for, and <paramref name="scope"/> then narrows each query to that account in SQL and
        /// skips the sources that cannot reach it.
        /// <para>
        /// Two properties keep the narrowed load honest. Each filter is a predicate on the very column
        /// the group is already keyed by — never a restatement of an accounting rule — and the
        /// role-driven fan-ins (withheld tax to Tax Payable, refunds to Customer Refund Payable,
        /// payouts to Commission Payable) still read their id lists out of <paramref name="accounts"/>.
        /// A single-account load therefore takes the same branches the full report takes for that
        /// account, and a source added here reaches both callers.
        /// </para>
        /// </summary>
        private async Task<List<TrialAccountDelta>> LoadTrialAccountDeltasAsync(
            int? projectId,
            DateTime finalEnd,
            IReadOnlyList<AccountSnapshot> accounts,
            TrialSourceScope scope,
            CancellationToken cancellationToken)
        {
            var deltas = new List<TrialAccountDelta>();
            var taxAccountIds = accounts.Where(a => a.SystemRole == FinanceSystemAccountRole.TaxPayable)
                .Select(a => a.Id).ToArray();
            var refundAccountIds = accounts.Where(a => a.SystemRole == FinanceSystemAccountRole.CustomerRefundPayable)
                .Select(a => a.Id).ToArray();
            var commissionPayableIds = accounts.Where(a => a.SystemRole == FinanceSystemAccountRole.CommissionPayable)
                .Select(a => a.Id).ToArray();

            // A capital account's balance is folded from the partner-transaction source alone, and every
            // other account's from the rest — the fold reads one bucket or the other, never both, so a
            // targeted load reads only the bucket its account will actually be paid out of.
            var normal = scope.Wants(TrialAccountDeltaKind.Normal);
            var capitalPartner = scope.Wants(TrialAccountDeltaKind.CapitalPartner);
            var onlyId = scope.AccountId;
            // A source that fans INTO the requested account has to be read whole: a Tax Payable row is
            // built from the tax withheld on every expense, not from the expenses paid out of itself.
            // Where the requested account is not that fan-in target, the id narrows the source in SQL.
            var expenseId = taxAccountIds.Length > 0 ? null : onlyId;
            var commissionId = commissionPayableIds.Length > 0 ? null : onlyId;
            var refundId = refundAccountIds.Length > 0 ? null : onlyId;

            // Read first, and on its own, so that everything after it is the Normal-kind half and a
            // capital account can leave the whole of it unread.
            if (capitalPartner && !projectId.HasValue)
            {
                deltas.AddRange(await _context.CapitalTransactions.AsNoTracking()
                    .Where(t => t.Date < finalEnd && t.CapitalPartner.FinanceAccountId != null
                        && (onlyId == null || t.CapitalPartner.FinanceAccountId == onlyId))
                    .GroupBy(t => new
                    {
                        AccountId = t.CapitalPartner.FinanceAccountId!.Value,
                        t.Date,
                        t.Type
                    }).Select(g => new TrialAccountDelta
                    {
                        AccountId = g.Key.AccountId,
                        Date = g.Key.Date,
                        Amount = g.Sum(t => t.Amount)
                            * (g.Key.Type == CapitalTransactionType.Withdrawal
                                || g.Key.Type == CapitalTransactionType.LossShare ? -1m : 1m),
                        Kind = TrialAccountDeltaKind.CapitalPartner
                    }).ToListAsync(cancellationToken));
            }
            if (!normal) return deltas;

            deltas.AddRange(await PaymentsQuery(projectId, null, finalEnd, onlyId)
                .Where(p => p.FinanceAccountId != null)
                .GroupBy(p => new { AccountId = p.FinanceAccountId!.Value, p.PaidAt })
                .Select(g => new TrialAccountDelta
                {
                    AccountId = g.Key.AccountId,
                    Date = g.Key.PaidAt,
                    Amount = g.Sum(p => p.Amount),
                    Kind = TrialAccountDeltaKind.Normal
                }).ToListAsync(cancellationToken));

            deltas.AddRange(await ManualQuery(projectId, null, finalEnd, onlyId)
                .Where(r => r.FinanceAccountId != null)
                .GroupBy(r => new { AccountId = r.FinanceAccountId!.Value, r.Date })
                .Select(g => new TrialAccountDelta
                {
                    AccountId = g.Key.AccountId,
                    Date = g.Key.Date,
                    Amount = g.Sum(r => r.Amount),
                    Kind = TrialAccountDeltaKind.Normal
                }).ToListAsync(cancellationToken));

            var expenses = await ExpenseQuery(projectId, null, finalEnd, expenseId)
                .GroupBy(e => new { e.FinanceAccountId, e.Date })
                .Select(g => new TrialSourceDelta
                {
                    AccountId = g.Key.FinanceAccountId,
                    Date = g.Key.Date,
                    Amount = g.Sum(e => e.Amount - e.WhtAmount),
                    SecondaryAmount = g.Sum(e => e.WhtAmount)
                }).ToListAsync(cancellationToken);
            foreach (var row in expenses)
            {
                if (row.AccountId.HasValue)
                    AddTrialDelta(deltas, row.AccountId.Value, row.Date, -row.Amount);
                foreach (var id in taxAccountIds)
                    AddTrialDelta(deltas, id, row.Date, row.SecondaryAmount);
            }

            var assetsPaid = await AssetPurchaseQuery(projectId, null, finalEnd, null, null, false)
                .Where(p => expenseId == null || p.FinanceAccountId == expenseId)
                .GroupBy(p => new { p.FinanceAccountId, p.Date })
                .Select(g => new TrialSourceDelta
                {
                    AccountId = g.Key.FinanceAccountId,
                    Date = g.Key.Date,
                    Amount = g.Sum(p => p.Amount - p.WhtAmount),
                    SecondaryAmount = g.Sum(p => p.WhtAmount)
                }).ToListAsync(cancellationToken);
            foreach (var row in assetsPaid)
            {
                AddTrialDelta(deltas, row.AccountId!.Value, row.Date, -row.Amount);
                foreach (var id in taxAccountIds)
                    AddTrialDelta(deltas, id, row.Date, row.SecondaryAmount);
            }
            deltas.AddRange(await AssetPurchaseQuery(projectId, null, finalEnd, null, null, false)
                .Where(p => onlyId == null || p.AssetAccountId == onlyId)
                .GroupBy(p => new { p.AssetAccountId, p.Date })
                .Select(g => new TrialAccountDelta
                {
                    AccountId = g.Key.AssetAccountId,
                    Date = g.Key.Date,
                    Amount = g.Sum(p => p.Amount),
                    Kind = TrialAccountDeltaKind.Normal
                }).ToListAsync(cancellationToken));

            // A payout moves two accounts: the bank falls and Commission Payable falls with it. The
            // payable side is what makes the commission double-sided now that the expense is raised
            // by the accrual instead of by the payment.
            var commissionPayouts = await CommissionPayoutQuery(projectId, null, finalEnd, null, false)
                .Where(p => commissionId == null || p.FinanceAccountId == commissionId)
                .GroupBy(p => new { p.FinanceAccountId, p.PaymentDate })
                .Select(g => new TrialSourceDelta
                {
                    AccountId = g.Key.FinanceAccountId,
                    Date = g.Key.PaymentDate,
                    Amount = g.Sum(p => p.Amount)
                }).ToListAsync(cancellationToken);
            foreach (var row in commissionPayouts)
            {
                AddTrialDelta(deltas, row.AccountId!.Value, row.Date, -row.Amount);
                foreach (var id in commissionPayableIds)
                    AddTrialDelta(deltas, id, row.Date, -row.Amount);
            }
            var commissionReversals = await CommissionReversalQuery(projectId, null, finalEnd, null, false)
                .Where(r => commissionId == null || r.Payout.FinanceAccountId == commissionId)
                .GroupBy(r => new { r.Payout.FinanceAccountId, r.ReversedAt })
                .Select(g => new TrialSourceDelta
                {
                    AccountId = g.Key.FinanceAccountId,
                    Date = g.Key.ReversedAt,
                    Amount = g.Sum(r => r.Amount)
                }).ToListAsync(cancellationToken);
            foreach (var row in commissionReversals)
            {
                AddTrialDelta(deltas, row.AccountId!.Value, row.Date, row.Amount);
                foreach (var id in commissionPayableIds)
                    AddTrialDelta(deltas, id, row.Date, row.Amount);
            }
            // The accrual side is NOT read again here. It is already loaded as a dated P&L delta, and
            // AddCommissionPayableTrialDeltas folds those same rows onto this account — the way the
            // receivable is folded out of the unit-sales deltas. A second read would be a second SQL
            // command per report for a figure already in hand.

            deltas.AddRange(await CashRebateQuery(projectId, null, finalEnd, null, false)
                .Where(d => d.FinanceAccountId != null && (onlyId == null || d.FinanceAccountId == onlyId))
                .GroupBy(d => new { AccountId = d.FinanceAccountId!.Value, d.AppliedAt })
                .Select(g => new TrialAccountDelta
                {
                    AccountId = g.Key.AccountId,
                    Date = g.Key.AppliedAt,
                    Amount = -g.Sum(d => d.Amount),
                    Kind = TrialAccountDeltaKind.Normal
                }).ToListAsync(cancellationToken));
            deltas.AddRange(await CashRebateReversalQuery(projectId, null, finalEnd, null, false)
                .Where(r => r.Disbursement.FinanceAccountId != null
                    && (onlyId == null || r.Disbursement.FinanceAccountId == onlyId))
                .GroupBy(r => new { AccountId = r.Disbursement.FinanceAccountId!.Value, r.ReversedAt })
                .Select(g => new TrialAccountDelta
                {
                    AccountId = g.Key.AccountId,
                    Date = g.Key.ReversedAt,
                    Amount = g.Sum(r => r.Amount),
                    Kind = TrialAccountDeltaKind.Normal
                }).ToListAsync(cancellationToken));

            if (!projectId.HasValue)
            {
                var deposits = await _context.WhtDeposits.AsNoTracking()
                    .Where(d => d.DepositDate < finalEnd
                        && (expenseId == null || d.FinanceAccountId == expenseId))
                    .GroupBy(d => new { d.FinanceAccountId, d.DepositDate })
                    .Select(g => new TrialSourceDelta
                    {
                        AccountId = g.Key.FinanceAccountId,
                        Date = g.Key.DepositDate,
                        Amount = g.Sum(d => d.Amount)
                    }).ToListAsync(cancellationToken);
                foreach (var row in deposits)
                {
                    AddTrialDelta(deltas, row.AccountId!.Value, row.Date, -row.Amount);
                    foreach (var id in taxAccountIds)
                        AddTrialDelta(deltas, id, row.Date, -row.Amount);
                }

                deltas.AddRange(await _context.CapitalTransactions.AsNoTracking()
                    .Where(t => t.Date < finalEnd && t.FinanceAccountId != null
                        && (onlyId == null || t.FinanceAccountId == onlyId)
                        && (t.Type == CapitalTransactionType.Contribution
                            || t.Type == CapitalTransactionType.Withdrawal))
                    .GroupBy(t => new { AccountId = t.FinanceAccountId!.Value, t.Date, t.Type })
                    .Select(g => new TrialAccountDelta
                    {
                        AccountId = g.Key.AccountId,
                        Date = g.Key.Date,
                        Amount = g.Sum(t => t.Amount)
                            * (g.Key.Type == CapitalTransactionType.Contribution ? 1m : -1m),
                        Kind = TrialAccountDeltaKind.Normal
                    }).ToListAsync(cancellationToken));
                deltas.AddRange(await _context.LoanTransactions.AsNoTracking()
                    .Where(t => t.Date < finalEnd && (onlyId == null || t.FinanceAccountId == onlyId))
                    .GroupBy(t => new { t.FinanceAccountId, t.Date, t.Type })
                    .Select(g => new TrialAccountDelta
                    {
                        AccountId = g.Key.FinanceAccountId,
                        Date = g.Key.Date,
                        Amount = g.Sum(t => t.Type == LoanTransactionType.Drawdown
                            ? t.PrincipalAmount : -(t.PrincipalAmount + t.InterestAmount)),
                        Kind = TrialAccountDeltaKind.Normal
                    }).ToListAsync(cancellationToken));
                deltas.AddRange(await _context.LoanTransactions.AsNoTracking()
                    .Where(t => t.Date < finalEnd && (onlyId == null || t.Loan.FinanceAccountId == onlyId))
                    .GroupBy(t => new { AccountId = t.Loan.FinanceAccountId, t.Date, t.Type })
                    .Select(g => new TrialAccountDelta
                    {
                        AccountId = g.Key.AccountId,
                        Date = g.Key.Date,
                        Amount = g.Sum(t => t.Type == LoanTransactionType.Drawdown
                            ? t.PrincipalAmount : -t.PrincipalAmount),
                        Kind = TrialAccountDeltaKind.Normal
                    }).ToListAsync(cancellationToken));

                var staff = await _context.StaffCashTransfers.AsNoTracking()
                    .Where(t => t.Date < finalEnd
                        && (onlyId == null || t.StaffFinanceAccountId == onlyId
                            || t.CounterpartyFinanceAccountId == onlyId))
                    .GroupBy(t => new
                    {
                        t.StaffFinanceAccountId,
                        t.CounterpartyFinanceAccountId,
                        t.Date,
                        t.Type
                    }).Select(g => new TrialStaffDelta
                    {
                        StaffAccountId = g.Key.StaffFinanceAccountId,
                        CounterpartyAccountId = g.Key.CounterpartyFinanceAccountId,
                        Date = g.Key.Date,
                        Type = g.Key.Type,
                        Amount = g.Sum(t => t.Amount)
                    }).ToListAsync(cancellationToken);
                foreach (var row in staff)
                {
                    AddTrialDelta(deltas, row.StaffAccountId, row.Date,
                        row.Type == StaffCashMovementType.FundsGiven ? row.Amount : -row.Amount);
                    AddTrialDelta(deltas, row.CounterpartyAccountId, row.Date,
                        row.Type == StaffCashMovementType.FundsReturned ? row.Amount : -row.Amount);
                }
            }

            var refunds = await _context.BookingCancellationRefunds.AsNoTracking()
                .Where(r => r.PaidAt < finalEnd
                    && (refundId == null || r.FinanceAccountId == refundId)
                    && (!projectId.HasValue || r.Settlement.Booking.Unit.ProjectId == projectId.Value))
                .GroupBy(r => new { r.FinanceAccountId, r.PaidAt })
                .Select(g => new TrialSourceDelta
                {
                    AccountId = g.Key.FinanceAccountId,
                    Date = g.Key.PaidAt,
                    Amount = g.Sum(r => r.Amount)
                }).ToListAsync(cancellationToken);
            foreach (var row in refunds)
            {
                AddTrialDelta(deltas, row.AccountId!.Value, row.Date, -row.Amount);
                foreach (var id in refundAccountIds)
                    AddTrialDelta(deltas, id, row.Date, -row.Amount);
            }
            if (refundAccountIds.Length > 0)
            {
                var refundLiabilities = await CancellationSettlementQuery(projectId, null, finalEnd)
                    .GroupBy(s => s.CancellationDate)
                    .Select(g => new TrialSourceDelta
                    {
                        Date = g.Key,
                        Amount = g.Sum(s => s.RefundAmount)
                    }).ToListAsync(cancellationToken);
                foreach (var row in refundLiabilities)
                    foreach (var id in refundAccountIds)
                        AddTrialDelta(deltas, id, row.Date, row.Amount);
            }

            return deltas;
        }

        /// <summary>
        /// Every P&amp;L source, in one place. The Trial Balance reads all of them; a Details request
        /// for one virtual row reads only the source that can produce that row's key, and — where the
        /// key names a real category column rather than a derived bucket — narrows that source to the
        /// line in SQL. The grouping expressions, and therefore the keys, are unchanged either way.
        /// </summary>
        private async Task<List<TrialPnlDelta>> LoadTrialPnlDeltasAsync(
            int? projectId,
            DateTime finalEnd,
            TrialSourceScope scope,
            CancellationToken cancellationToken)
        {
            var rows = new List<TrialPnlDelta>();
            if (scope.Wants(TrialPnlSource.ManualRevenue))
            {
            var manualSource = ManualQuery(projectId, SqlStart, finalEnd);
            // Only when the key named a managed category. The "U" bucket is whatever the grouping
            // expression decides is unclassified — no category, or a legacy one — and restating that
            // rule here is exactly the drift this narrowing must not introduce, so it is left alone.
            if (scope.PnlLine is { CategoryId: int managedCategory } manualLine)
                manualSource = manualSource.Where(r => r.RevenueCategoryId == managedCategory
                    && r.RevenueTypeName == manualLine.Name);
            var manualRows = await manualSource
                .GroupBy(r => new
                {
                    r.Date,
                    CategoryId = r.RevenueCategoryId == null || r.RevenueCategory!.Code.StartsWith("legacy_")
                        ? null : r.RevenueCategoryId,
                    Name = r.RevenueCategoryId == null || r.RevenueCategory!.Code.StartsWith("legacy_")
                        ? "Unclassified" : r.RevenueTypeName,
                    Order = r.RevenueCategory == null || r.RevenueCategory.Code.StartsWith("legacy_")
                        ? int.MaxValue : r.RevenueCategory.DisplayOrder
                }).Select(g => new TrialPnlDelta
                {
                    Date = g.Key.Date,
                    IsIncome = true,
                    CategoryId = g.Key.CategoryId,
                    Name = g.Key.Name,
                    Order = g.Key.Order,
                    Amount = g.Sum(r => r.Amount),
                    Count = g.Count()
                }).ToListAsync(cancellationToken);
            foreach (var row in manualRows)
                row.Key = (row.CategoryId == null ? "U" : row.CategoryId.ToString()) + ":" + row.Name;
            rows.AddRange(manualRows);
            }

            // These recognition/credit rows also feed the physical receivable below. Its legacy
            // snapshot is all-time, even while the virtual P&L is clipped to SqlStart/baseline, so
            // load the full dated source and let BuildTrialPnlPeriod apply the P&L start.
            if (scope.Wants(TrialPnlSource.UnitSales))
            rows.AddRange(await SaleRecognitionQuery(projectId, null, finalEnd)
                .GroupBy(r => r.RecognitionDate)
                .Select(g => new TrialPnlDelta
                {
                    Date = g.Key,
                    IsIncome = true,
                    Key = "unit-sales",
                    Name = "Unit Sales",
                    Order = -1,
                    Amount = g.Sum(r => r.NetSaleValue),
                    Count = g.Count()
                }).ToListAsync(cancellationToken));
            if (scope.Wants(TrialPnlSource.CancellationRetained))
            rows.AddRange(await RetainedCancellationQuery(projectId, SqlStart, finalEnd)
                .GroupBy(s => s.CancellationDate)
                .Select(g => new TrialPnlDelta
                {
                    Date = g.Key,
                    IsIncome = true,
                    Key = "cancellation-retained",
                    Name = "Cancellation Income (Retained)",
                    Order = 0,
                    Amount = g.Sum(s => s.RetainedAmount),
                    Count = g.Count()
                }).ToListAsync(cancellationToken));

            if (scope.Wants(TrialPnlSource.Expenses))
            {
            var expenseSource = ExpenseQuery(projectId, SqlStart, finalEnd);
            // Both halves of an expense key are plain columns of the row — the grouping applies no
            // bucketing rule to them — so the line narrows exactly, unclassified heads included.
            if (scope.PnlLine is { } expenseLine)
                expenseSource = expenseSource.Where(e => e.CategoryId == expenseLine.CategoryId
                    && e.Category == expenseLine.Name);
            var expenses = await expenseSource
                .GroupBy(e => new
                {
                    e.Date,
                    e.CategoryId,
                    e.Category,
                    Order = e.ExpenseCategory == null ? int.MaxValue : e.ExpenseCategory.DisplayOrder
                }).Select(g => new TrialPnlDelta
                {
                    Date = g.Key.Date,
                    CategoryId = g.Key.CategoryId,
                    Name = g.Key.Category,
                    Order = g.Key.Order,
                    Amount = g.Sum(e => e.Amount),
                    Count = g.Count()
                }).ToListAsync(cancellationToken);
            foreach (var row in expenses)
                row.Key = (row.CategoryId == null ? "U" : row.CategoryId.ToString()) + ":" + row.Name;
            rows.AddRange(expenses);
            }

            // The obligation, dated when it arose — the payout is a payable settlement, not a cost.
            if (scope.Wants(TrialPnlSource.CommissionAccrual))
            rows.AddRange(await CommissionAccrualQuery(projectId, SqlStart, finalEnd, null, false)
                .GroupBy(a => a.AccruedOn)
                .Select(g => new TrialPnlDelta
                {
                    Date = g.Key,
                    Key = "commission-expense",
                    Name = "Partner Commissions",
                    Order = int.MaxValue - 1,
                    Amount = g.Sum(a => a.Amount),
                    Count = g.Count()
                }).ToListAsync(cancellationToken));
            if (scope.Wants(TrialPnlSource.CashRebates))
            rows.AddRange(await CashRebateQuery(projectId, SqlStart, finalEnd, null, false)
                .Select(d => new { Date = d.AppliedAt, Amount = d.Amount, Count = 1 })
                .Concat(CashRebateReversalQuery(projectId, SqlStart, finalEnd, null, false)
                    .Select(r => new { Date = r.ReversedAt, Amount = -r.Amount, Count = 1 }))
                .GroupBy(x => x.Date)
                .Select(g => new TrialPnlDelta
                {
                    Date = g.Key,
                    Key = "cash-rebates",
                    Name = "Cash Rebates",
                    Order = int.MaxValue,
                    Amount = g.Sum(x => x.Amount),
                    Count = g.Sum(x => x.Count)
                }).ToListAsync(cancellationToken));

            if (scope.Wants(TrialPnlSource.NonCashCredits))
            rows.AddRange(await NonCashCreditQuery(projectId, null, finalEnd)
                .Select(d => new
                {
                    Date = d.AppliedAt < d.Rebate.Booking.SaleRecognition!.RecognitionDate.AddDays(1)
                        ? d.Rebate.Booking.SaleRecognition!.RecognitionDate : d.AppliedAt,
                    Amount = d.Amount,
                    Count = 1
                }).Concat(NonCashCreditReversalQuery(projectId, null, finalEnd)
                    .Select(r => new
                    {
                        Date = r.ReversedAt < r.Disbursement.Rebate.Booking.SaleRecognition!.RecognitionDate.AddDays(1)
                            ? r.Disbursement.Rebate.Booking.SaleRecognition!.RecognitionDate : r.ReversedAt,
                        Amount = -r.Amount,
                        Count = 1
                    })).GroupBy(x => x.Date)
                .Select(g => new TrialPnlDelta
                {
                    Date = g.Key,
                    Key = "non-cash-credits",
                    Name = "Customer Credits (non-cash)",
                    Order = int.MaxValue,
                    Amount = g.Sum(x => x.Amount),
                    Count = g.Sum(x => x.Count)
                }).ToListAsync(cancellationToken));
            if (scope.Wants(TrialPnlSource.LoanInterest))
            rows.AddRange(await LoanInterestQuery(projectId, SqlStart, finalEnd, null, false)
                .GroupBy(t => t.Date)
                .Select(g => new TrialPnlDelta
                {
                    Date = g.Key,
                    Key = "loan-interest",
                    Name = "Loan Interest",
                    Order = int.MaxValue - 2,
                    Amount = g.Sum(t => t.InterestAmount),
                    Count = g.Count()
                }).ToListAsync(cancellationToken));

            return rows;
        }

        /// <summary>
    /// Puts the commission obligation on Commission Payable, from the P&amp;L deltas already loaded.
    /// <para>
    /// Every accrual raises the expense AND the payable by the same amount on the same day, so the
    /// dated rows behind the expense line are exactly the rows this account needs. Reading them a
    /// second time would cost a SQL command per report to re-derive a figure already in memory —
    /// the same reason the customer receivable is folded out of the unit-sales deltas rather than
    /// re-queried.
    /// </para>
    /// </summary>
    private static void AddCommissionPayableTrialDeltas(
        IReadOnlyList<int> accountIds,
        IReadOnlyList<TrialPnlDelta> pnlDeltas,
        List<TrialAccountDelta> accountDeltas)
    {
        if (accountIds.Count == 0) return;
        foreach (var row in pnlDeltas.Where(r => r.Key == "commission-expense"))
            foreach (var id in accountIds)
                AddTrialDelta(accountDeltas, id, row.Date, row.Amount);
    }

    private async Task AddCustomerTrialDeltasAsync(
            int? projectId,
            DateTime finalEnd,
            IReadOnlyList<AccountSnapshot> accounts,
            IReadOnlyList<TrialPnlDelta> pnlDeltas,
            List<TrialAccountDelta> accountDeltas,
            CancellationToken cancellationToken)
        {
            var depositAccountIds = accounts.Where(a => a.SystemRole == FinanceSystemAccountRole.CustomerDeposits)
                .Select(a => a.Id).ToArray();
            var receivableAccountIds = accounts.Where(a => a.SystemRole == FinanceSystemAccountRole.CustomerReceivables)
                .Select(a => a.Id).ToArray();
            if (depositAccountIds.Length == 0 && receivableAccountIds.Length == 0) return;

            if (depositAccountIds.Length > 0)
            {
                var received = await PaymentsQuery(projectId, null, finalEnd)
                    .Where(p => p.Booking.SaleRecognition == null
                        || p.PaidAt < p.Booking.SaleRecognition.RecognitionDate
                        || (p.PaidAt < p.Booking.SaleRecognition.RecognitionDate.AddDays(1)
                            && p.CreatedAt <= p.Booking.SaleRecognition.RecognizedAt))
                    .GroupBy(p => p.PaidAt)
                    .Select(g => new TrialSourceDelta { Date = g.Key, Amount = g.Sum(p => p.Amount) })
                    .ToListAsync(cancellationToken);
                foreach (var row in received)
                    foreach (var id in depositAccountIds)
                        AddTrialDelta(accountDeltas, id, row.Date, row.Amount);

                var clearedByRecognition = await PaymentsQuery(projectId, null, finalEnd)
                    .Where(p => p.Booking.SaleRecognition != null
                        && (p.PaidAt < p.Booking.SaleRecognition.RecognitionDate
                            || (p.PaidAt < p.Booking.SaleRecognition.RecognitionDate.AddDays(1)
                                && p.CreatedAt <= p.Booking.SaleRecognition.RecognizedAt)))
                    .Select(p => new
                    {
                        Date = p.PaidAt >= p.Booking.SaleRecognition!.RecognitionDate
                            ? p.PaidAt : p.Booking.SaleRecognition.RecognitionDate,
                        p.Amount
                    }).Where(p => p.Date < finalEnd)
                    .GroupBy(p => p.Date)
                    .Select(g => new TrialSourceDelta { Date = g.Key, Amount = g.Sum(p => p.Amount) })
                    .ToListAsync(cancellationToken);
                foreach (var row in clearedByRecognition)
                    foreach (var id in depositAccountIds)
                        AddTrialDelta(accountDeltas, id, row.Date, -row.Amount);

                var clearedByCancellation = await PaymentsQuery(projectId, null, finalEnd)
                    .Where(p => p.Booking.CancellationSettlement != null)
                    .Select(p => new
                    {
                        Date = p.PaidAt >= p.Booking.CancellationSettlement!.CancellationDate
                            ? p.PaidAt : p.Booking.CancellationSettlement.CancellationDate,
                        p.Amount
                    }).Where(p => p.Date < finalEnd)
                    .GroupBy(p => p.Date)
                    .Select(g => new TrialSourceDelta { Date = g.Key, Amount = g.Sum(p => p.Amount) })
                    .ToListAsync(cancellationToken);
                foreach (var row in clearedByCancellation)
                    foreach (var id in depositAccountIds)
                        AddTrialDelta(accountDeltas, id, row.Date, -row.Amount);
            }

            if (receivableAccountIds.Length > 0)
            {
                foreach (var row in pnlDeltas.Where(r => r.Key == "unit-sales"))
                    foreach (var id in receivableAccountIds)
                        AddTrialDelta(accountDeltas, id, row.Date, row.Amount);
                foreach (var row in pnlDeltas.Where(r => r.Key == "non-cash-credits"))
                    foreach (var id in receivableAccountIds)
                        AddTrialDelta(accountDeltas, id, row.Date, -row.Amount);

                var collected = await PaymentsQuery(projectId, null, finalEnd)
                    .Where(p => p.Booking.SaleRecognition != null)
                    .Select(p => new
                    {
                        Date = p.PaidAt >= p.Booking.SaleRecognition!.RecognitionDate
                            ? p.PaidAt : p.Booking.SaleRecognition.RecognitionDate,
                        p.Amount
                    }).Where(p => p.Date < finalEnd)
                    .GroupBy(p => p.Date)
                    .Select(g => new TrialSourceDelta { Date = g.Key, Amount = g.Sum(p => p.Amount) })
                    .ToListAsync(cancellationToken);
                foreach (var row in collected)
                    foreach (var id in receivableAccountIds)
                        AddTrialDelta(accountDeltas, id, row.Date, -row.Amount);
            }
        }

        private static void AddTrialPnlDelta(
            IDictionary<TrialPnlKey, ReportLine> balances,
            TrialPnlDelta delta)
        {
            var key = new TrialPnlKey(
                delta.IsIncome, delta.Key, delta.CategoryId, delta.Name, delta.Order);
            if (!balances.TryGetValue(key, out var line))
            {
                line = new ReportLine
                {
                    Key = delta.Key,
                    CategoryId = delta.CategoryId,
                    Name = delta.Name,
                    Order = delta.Order
                };
                balances[key] = line;
            }
            line.Amount += delta.Amount;
            line.Count += delta.Count;
        }

        private static PnlPeriod BuildTrialPnlPeriod(
            IReadOnlyDictionary<TrialPnlKey, ReportLine> balances)
        {
            var lines = balances.Where(entry => Money(entry.Value.Amount) != 0m)
                .Select(entry => new
                {
                    entry.Key.IsIncome,
                    Line = new ReportLine
                    {
                        Key = entry.Value.Key,
                        CategoryId = entry.Value.CategoryId,
                        Name = entry.Value.Name,
                        Order = entry.Value.Order,
                        Amount = entry.Value.Amount,
                        Count = entry.Value.Count
                    }
                }).ToList();

            return new PnlPeriod(
                lines.Where(row => row.IsIncome).Select(row => row.Line)
                    .OrderBy(line => line.Order).ThenBy(line => line.Name).ToList(),
                lines.Where(row => !row.IsIncome).Select(row => row.Line)
                    .OrderBy(line => line.Order).ThenBy(line => line.Name).ToList());
        }

        private static void AddTrialDelta(
            ICollection<TrialAccountDelta> deltas,
            int accountId,
            DateTime date,
            decimal amount,
            TrialAccountDeltaKind kind = TrialAccountDeltaKind.Normal)
        {
            if (amount == 0m) return;
            deltas.Add(new TrialAccountDelta
            {
                AccountId = accountId,
                Date = date,
                Amount = amount,
                Kind = kind
            });
        }

        private sealed class TrialBalanceColumnData
        {
            public List<AccountSnapshot> Accounts { get; init; } = [];
            public PnlPeriod Pnl { get; init; } = new([], []);
            public decimal NetAllocation { get; init; }
            public bool HasAllocation { get; init; }
        }

        private enum TrialAccountDeltaKind
        {
            Normal,
            CapitalPartner
        }

        private sealed class TrialAccountDelta
        {
            public int AccountId { get; set; }
            public DateTime Date { get; set; }
            public decimal Amount { get; set; }
            public TrialAccountDeltaKind Kind { get; set; }
        }

        private sealed class TrialSourceDelta
        {
            public int? AccountId { get; set; }
            public DateTime Date { get; set; }
            public decimal Amount { get; set; }
            public decimal SecondaryAmount { get; set; }
        }

        private sealed class TrialStaffDelta
        {
            public int StaffAccountId { get; set; }
            public int CounterpartyAccountId { get; set; }
            public DateTime Date { get; set; }
            public StaffCashMovementType Type { get; set; }
            public decimal Amount { get; set; }
        }

        private sealed class TrialPnlDelta
        {
            public DateTime Date { get; set; }
            public bool IsIncome { get; set; }
            public string Key { get; set; } = string.Empty;
            public int? CategoryId { get; set; }
            public string Name { get; set; } = string.Empty;
            public int Order { get; set; }
            public decimal Amount { get; set; }
            public int Count { get; set; }
        }

        private readonly record struct TrialPnlKey(
            bool IsIncome, string Key, int? CategoryId, string Name, int Order);

        private sealed class TrialAllocationDelta
        {
            public DateTime Date { get; set; }
            public CapitalTransactionType Type { get; set; }
            public decimal Amount { get; set; }
        }
    }
}
