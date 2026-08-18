using DAMS.Application.Common;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public partial class FinanceService
    {
        private static readonly DateTime SqlStart = new(1753, 1, 1);

        public async Task<ProfitAndLossDto> GetProfitAndLossAsync(
            int? projectId, DateTime? from, DateTime? to, CancellationToken cancellationToken = default)
        {
            await EnsureProjectExistsAsync(projectId, cancellationToken);
            var (start, end, label) = await ResolveReportPeriodAsync(from, to, cancellationToken);
            var priorStart = start.AddYears(-1);
            var priorEnd = end.AddYears(-1);
            var current = await BuildPnlPeriodAsync(projectId, start, end.AddDays(1), cancellationToken);
            var prior = await BuildPnlPeriodAsync(projectId, priorStart, priorEnd.AddDays(1), cancellationToken);
            var projectName = projectId.HasValue
                ? await _context.Projects.AsNoTracking().Where(p => p.Id == projectId).Select(p => p.ProjectName).SingleAsync(cancellationToken)
                : null;
            return new ProfitAndLossDto
            {
                PeriodStart = start, PeriodEnd = end, PeriodLabel = label, ProjectName = projectName,
                IncomeLines = MergeLines(current.Income, prior.Income),
                ExpenseLines = MergeLines(current.Expenses, prior.Expenses),
                TotalIncome = Money(current.Income.Sum(l => l.Amount)),
                TotalExpenses = Money(current.Expenses.Sum(l => l.Amount)),
                NetProfit = Money(current.Income.Sum(l => l.Amount) - current.Expenses.Sum(l => l.Amount)),
                PriorTotalIncome = Money(prior.Income.Sum(l => l.Amount)),
                PriorTotalExpenses = Money(prior.Expenses.Sum(l => l.Amount)),
                PriorNetProfit = Money(prior.Income.Sum(l => l.Amount) - prior.Expenses.Sum(l => l.Amount))
            };
        }

        public async Task<BalanceSheetDto> GetBalanceSheetAsync(
            int? projectId, DateTime asAt, CancellationToken cancellationToken = default)
        {
            await EnsureProjectExistsAsync(projectId, cancellationToken);
            var date = ValidateReportDate(asAt == default ? PakistanTime.Today : asAt.Date, "As-at date");
            var openingDate = await OpeningDateAsync(cancellationToken);
            var pnlStart = openingDate.HasValue && openingDate.Value <= date ? openingDate.Value : SqlStart;
            var snapshots = await AccountSnapshotsAsync(projectId, date, openingDate, cancellationToken);
            var pnl = await BuildPnlPeriodAsync(projectId, pnlStart, date.AddDays(1), cancellationToken);
            var retainedProfit = Money(pnl.Income.Sum(l => l.Amount) - pnl.Expenses.Sum(l => l.Amount));
            if (!projectId.HasValue)
            {
                var allocations = await _context.CapitalTransactions.AsNoTracking()
                    .Where(t => t.Date < date.AddDays(1) && (t.Type == CapitalTransactionType.ProfitShare || t.Type == CapitalTransactionType.LossShare))
                    .GroupBy(t => t.Type).Select(g => new { g.Key, Amount = g.Sum(t => t.Amount) }).ToListAsync(cancellationToken);
                retainedProfit -= allocations.Where(x => x.Key == CapitalTransactionType.ProfitShare).Sum(x => x.Amount);
                retainedProfit += allocations.Where(x => x.Key == CapitalTransactionType.LossShare).Sum(x => x.Amount);
                retainedProfit = Money(retainedProfit);
            }

            var assetGroups = new[]
            {
                Group("Current Assets", snapshots, FinanceAccountType.Cash, FinanceAccountType.Bank, FinanceAccountType.MobileWallet, FinanceAccountType.Other),
                StaffFloatGroup("Cash held by staff", snapshots, positive: true),
                Group("Fixed Assets", snapshots, FinanceAccountType.FixedAsset),
                Group("Work in Progress", snapshots, FinanceAccountType.WorkInProgress),
                Group("Receivables", snapshots, FinanceAccountType.Receivable)
            }.Where(g => g.Lines.Count > 0).ToList();
            var liabilityGroups = new[]
            {
                Group("Liabilities", snapshots, FinanceAccountType.Liability),
                StaffFloatGroup("Due to staff", snapshots, positive: false)
            }.Where(g => g.Lines.Count > 0).ToList();
            var capitalLines = snapshots.Where(s => s.Type == FinanceAccountType.Capital)
                .OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name).Select(ToBsLine).ToList();
            var totalAssets = Money(assetGroups.Sum(g => g.Total));
            var totalLiabilities = Money(liabilityGroups.Sum(g => g.Total));
            var totalCapital = Money(capitalLines.Sum(l => l.Amount) + retainedProfit);
            var rhs = Money(totalLiabilities + totalCapital);
            var imbalance = Money(totalAssets - rhs);
            var result = new BalanceSheetDto
            {
                AsAt = date, AssetGroups = assetGroups, TotalAssets = totalAssets,
                LiabilityGroups = liabilityGroups,
                TotalLiabilities = totalLiabilities, CapitalLines = capitalLines,
                RetainedProfit = retainedProfit, TotalCapital = totalCapital,
                TotalLiabilitiesAndCapital = rhs, Imbalance = imbalance,
                IsBalanced = imbalance == 0m
            };
            if (!result.IsBalanced)
                result.UnbalancedAccounts = await DiagnoseImbalanceAsync(projectId, date, snapshots, cancellationToken);
            return result;
        }

        public async Task<TrialBalanceDto> GetTrialBalanceAsync(
            int? projectId, DateTime asAt, int monthsBack, CancellationToken cancellationToken = default)
        {
            await EnsureProjectExistsAsync(projectId, cancellationToken);
            if (monthsBack is < 0 or > 60) throw new InvalidOperationException("Months back must be between 0 and 60.");
            var date = ValidateReportDate(asAt == default ? PakistanTime.Today : asAt.Date, "As-at date");
            var currentMonthEnd = new DateTime(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));
            var monthEnd = date == currentMonthEnd ? date : new DateTime(date.Year, date.Month, 1).AddDays(-1);
            if (monthEnd.AddMonths(-monthsBack) < SqlStart)
                throw new InvalidOperationException($"The requested trial-balance history cannot start before {SqlStart:dd MMM yyyy}.");
            var dates = Enumerable.Range(0, monthsBack + 1).Select(offset => MonthEnd(monthEnd.AddMonths(offset - monthsBack))).ToList();
            var openingDate = await OpeningDateAsync(cancellationToken);
            var builders = new Dictionary<string, TrialRowBuilder>(StringComparer.Ordinal);

            foreach (var columnDate in dates)
            {
                var values = new Dictionary<string, TrialValue>(StringComparer.Ordinal);
                var snapshots = await AccountSnapshotsAsync(projectId, columnDate, openingDate, cancellationToken);
                foreach (var account in snapshots)
                {
                    var debitNormal = AccountBalanceDirection.IsDebitNormal(account.Type);
                    var debit = account.Balance >= 0m
                        ? (debitNormal ? account.Balance : 0m)
                        : (debitNormal ? 0m : -account.Balance);
                    var credit = account.Balance >= 0m
                        ? (debitNormal ? 0m : account.Balance)
                        : (debitNormal ? -account.Balance : 0m);
                    values[$"A:{account.Id}"] = new TrialValue(account.Id, account.LedgerCode, account.Name, account.Type, debit, credit);
                }

                var start = openingDate.HasValue && openingDate.Value <= columnDate ? openingDate.Value : SqlStart;
                var pnl = await BuildPnlPeriodAsync(projectId, start, columnDate.AddDays(1), cancellationToken);
                // Income is credit-normal and expense is debit-normal, but a contra line (e.g.
                // Customer Refunds) carries a negative amount — that negative amount must flip to
                // the opposite column, not sit as a negative balance in its normal column. A
                // negative Credit is not a valid trial-balance cell even when the totals still
                // happen to net out.
                foreach (var line in pnl.Income)
                    values[$"I:{line.Key}"] = new TrialValue(VirtualId("I:" + line.Key), null, line.Name, FinanceAccountType.Other,
                        line.Amount < 0m ? -line.Amount : 0m, line.Amount < 0m ? 0m : line.Amount);
                foreach (var line in pnl.Expenses)
                    values[$"E:{line.Key}"] = new TrialValue(VirtualId("E:" + line.Key), null, line.Name, FinanceAccountType.Other,
                        line.Amount < 0m ? 0m : line.Amount, line.Amount < 0m ? -line.Amount : 0m);

                if (!projectId.HasValue)
                {
                    var allocations = await _context.CapitalTransactions.AsNoTracking()
                        .Where(t => t.Date < columnDate.AddDays(1) && (t.Type == CapitalTransactionType.ProfitShare || t.Type == CapitalTransactionType.LossShare))
                        .GroupBy(t => t.Type).Select(g => new { g.Key, Amount = g.Sum(t => t.Amount) }).ToListAsync(cancellationToken);
                    var profit = allocations.Where(x => x.Key == CapitalTransactionType.ProfitShare).Sum(x => x.Amount);
                    var loss = allocations.Where(x => x.Key == CapitalTransactionType.LossShare).Sum(x => x.Amount);
                    if (profit != 0m || loss != 0m)
                        values["EQ:allocated"] = new TrialValue(VirtualId("EQ:allocated"), null, "Allocated profit / loss", FinanceAccountType.Capital, profit, loss);
                }

                foreach (var key in builders.Keys.Union(values.Keys).ToList())
                {
                    if (!builders.TryGetValue(key, out var builder))
                    {
                        var value = values[key];
                        builder = new TrialRowBuilder(value.AccountId, value.LedgerCode, value.Name, value.Type);
                        for (var i = 0; i < dates.IndexOf(columnDate); i++) builder.Add(0m, 0m);
                        builders[key] = builder;
                    }
                    if (values.TryGetValue(key, out var current)) builder.Add(Money(current.Debit), Money(current.Credit));
                    else builder.Add(0m, 0m);
                }
            }

            var rows = builders.Values.OrderBy(r => r.AccountId < 0 ? 1 : 0).ThenBy(r => r.Type).ThenBy(r => r.Name)
                .Select(r => r.Build()).ToList();
            var debitTotals = Enumerable.Range(0, dates.Count).Select(i => Money(rows.Sum(r => r.DebitBalances[i]))).ToList();
            var creditTotals = Enumerable.Range(0, dates.Count).Select(i => Money(rows.Sum(r => r.CreditBalances[i]))).ToList();
            return new TrialBalanceDto
            {
                ColumnDates = dates, Rows = rows, ColumnDebitTotals = debitTotals, ColumnCreditTotals = creditTotals,
                ColumnBalanced = debitTotals.Zip(creditTotals, (d, c) => d == c).ToList()
            };
        }

        public async Task<FinanceExportDto> ExportProfitAndLossAsync(int? projectId, DateTime? from, DateTime? to, CancellationToken cancellationToken = default)
        {
            var report = await GetProfitAndLossAsync(projectId, from, to, cancellationToken);
            var rows = new List<IReadOnlyList<object?>>
            {
                new object?[] { "Profit & Loss", report.ProjectName ?? "All projects", report.PeriodLabel },
                new object?[] { "Income", "Current", "Prior" }
            };
            rows.AddRange(report.IncomeLines.Select(l => (IReadOnlyList<object?>)new object?[] { l.Name, l.Amount, l.PriorAmount }));
            rows.Add(new object?[] { "Total Income", report.TotalIncome, report.PriorTotalIncome });
            rows.Add(new object?[] { "Expenses", "Current", "Prior" });
            rows.AddRange(report.ExpenseLines.Select(l => (IReadOnlyList<object?>)new object?[] { l.Name, l.Amount, l.PriorAmount }));
            rows.Add(new object?[] { "Total Expenses", report.TotalExpenses, report.PriorTotalExpenses });
            rows.Add(new object?[] { "Net Profit", report.NetProfit, report.PriorNetProfit });
            return Workbook("profit-and-loss", rows);
        }

        public async Task<FinanceExportDto> ExportTrialBalanceAsync(int? projectId, DateTime asAt, int monthsBack, CancellationToken cancellationToken = default)
        {
            var report = await GetTrialBalanceAsync(projectId, asAt, monthsBack, cancellationToken);
            var header = new List<object?> { "Ledger", "Account" };
            foreach (var date in report.ColumnDates) { header.Add(date.ToString("dd-MMM-yyyy") + " Debit"); header.Add(date.ToString("dd-MMM-yyyy") + " Credit"); }
            var rows = new List<IReadOnlyList<object?>> { header };
            foreach (var line in report.Rows)
            {
                var row = new List<object?> { line.LedgerCode, line.AccountName };
                for (var i = 0; i < report.ColumnDates.Count; i++) { row.Add(line.DebitBalances[i]); row.Add(line.CreditBalances[i]); }
                rows.Add(row);
            }
            var totals = new List<object?> { null, "Totals" };
            for (var i = 0; i < report.ColumnDates.Count; i++) { totals.Add(report.ColumnDebitTotals[i]); totals.Add(report.ColumnCreditTotals[i]); }
            rows.Add(totals);
            return Workbook("trial-balance", rows);
        }

        public async Task<FinanceExportDto> ExportBalanceSheetAsync(int? projectId, DateTime asAt, CancellationToken cancellationToken = default)
        {
            var report = await GetBalanceSheetAsync(projectId, asAt, cancellationToken);
            var rows = new List<IReadOnlyList<object?>> { new object?[] { "Balance Sheet", report.AsAt.ToString("dd-MMM-yyyy") } };
            foreach (var group in report.AssetGroups)
            {
                rows.Add(new object?[] { group.Name, null });
                rows.AddRange(group.Lines.Select(l => (IReadOnlyList<object?>)new object?[] { l.Name, l.Amount }));
                rows.Add(new object?[] { "Total " + group.Name, group.Total });
            }
            rows.Add(new object?[] { "Total Assets", report.TotalAssets });
            foreach (var group in report.LiabilityGroups)
            {
                rows.Add(new object?[] { group.Name, null });
                rows.AddRange(group.Lines.Select(l => (IReadOnlyList<object?>)new object?[] { l.Name, l.Amount }));
            }
            rows.AddRange(report.CapitalLines.Select(l => (IReadOnlyList<object?>)new object?[] { l.Name, l.Amount }));
            rows.Add(new object?[] { "Retained Profit", report.RetainedProfit });
            rows.Add(new object?[] { "Total Liabilities & Capital", report.TotalLiabilitiesAndCapital });
            rows.Add(new object?[] { "Balanced", report.IsBalanced ? "Yes" : "No" });
            return Workbook("balance-sheet", rows);
        }

        private FinanceExportDto Workbook(string prefix, IReadOnlyList<IReadOnlyList<object?>> rows) => new()
        {
            FileName = $"{prefix}-{PakistanTime.Today:yyyy-MM-dd}.xlsx",
            Content = SimpleXlsxWriter.Write("Report", rows)
        };

        private async Task<(DateTime Start, DateTime End, string Label)> ResolveReportPeriodAsync(DateTime? from, DateTime? to, CancellationToken cancellationToken)
        {
            var month = await FinanceSettingsQuery.FinancialYearStartMonthAsync(_context, cancellationToken);
            if (from.HasValue) from = ValidateReportDate(from.Value, "From date");
            if (to.HasValue) to = ValidateReportDate(to.Value, "To date");
            if (!from.HasValue && !to.HasValue)
            {
                var window = FinancialYear.Window(PakistanTime.Today, month);
                return (window.Start, window.EndExclusive.AddDays(-1), FinancialYear.Label(window.Start, month));
            }
            var anchor = from ?? to!.Value;
            var fy = FinancialYear.Window(anchor, month);
            var start = from?.Date ?? fy.Start;
            var end = to?.Date ?? fy.EndExclusive.AddDays(-1);
            if (start > end) throw new InvalidOperationException("From date cannot be after to date.");
            var label = start == fy.Start && end == fy.EndExclusive.AddDays(-1)
                ? FinancialYear.Label(start, month)
                : $"{start:dd MMM yyyy} – {end:dd MMM yyyy}";
            return (start, end, label);
        }

        private async Task<PnlPeriod> BuildPnlPeriodAsync(int? projectId, DateTime from, DateTime toExclusive, CancellationToken cancellationToken)
        {
            var income = await ManualQuery(projectId, from, toExclusive).GroupBy(r => new
                {
                    RevenueCategoryId = r.RevenueCategoryId == null || r.RevenueCategory!.Code.StartsWith("legacy_")
                        ? null : r.RevenueCategoryId,
                    Name = r.RevenueCategoryId == null || r.RevenueCategory!.Code.StartsWith("legacy_")
                        ? "Unclassified" : r.RevenueTypeName,
                    Order = r.RevenueCategory == null || r.RevenueCategory.Code.StartsWith("legacy_")
                        ? int.MaxValue : r.RevenueCategory.DisplayOrder
                }).Select(g => new ReportLine
                {
                    Key = (g.Key.RevenueCategoryId == null ? "U" : g.Key.RevenueCategoryId.ToString()) + ":" + g.Key.Name,
                    CategoryId = g.Key.RevenueCategoryId, Name = g.Key.Name, Order = g.Key.Order,
                    Amount = g.Sum(r => r.Amount), Count = g.Count()
                }).ToListAsync(cancellationToken);
            // Unit sales, recognised at possession — NOT customer receipts. Money taken before
            // possession is a deposit the company owes back, so counting it as income overstated
            // revenue by every unfinished sale and understated the liability by the same amount.
            // The full net sale value lands here once, on its recognition date; what the buyer
            // still owes becomes a receivable rather than future revenue.
            var recognisedSales = await SaleRecognitionQuery(projectId, from, toExclusive).GroupBy(_ => 1)
                .Select(g => new { Amount = g.Sum(r => r.NetSaleValue), Count = g.Count() }).SingleOrDefaultAsync(cancellationToken);
            if (recognisedSales is { Amount: not 0m })
                income.Add(new ReportLine { Key = "unit-sales", Name = "Unit Sales", Order = -1, Amount = recognisedSales.Amount, Count = recognisedSales.Count });
            // What the company keeps when a booking is cancelled. Recognised once, on the
            // cancellation date. The refund itself is NOT contra-revenue: the customer's money was
            // never income, so refunding it cannot reduce income — it converts one liability
            // (deposit) into another (refund payable).
            var retained = await RetainedCancellationQuery(projectId, from, toExclusive).GroupBy(_ => 1)
                .Select(g => new { Amount = g.Sum(s => s.RetainedAmount), Count = g.Count() }).SingleOrDefaultAsync(cancellationToken);
            if (retained is { Amount: not 0m })
                income.Add(new ReportLine { Key = "cancellation-retained", Name = "Cancellation Income (Retained)", Order = 0, Amount = retained.Amount, Count = retained.Count });

            var expenses = await ExpenseQuery(projectId, from, toExclusive).GroupBy(e => new
                {
                    e.CategoryId, e.Category, Order = e.ExpenseCategory == null ? int.MaxValue : e.ExpenseCategory.DisplayOrder
                }).Select(g => new ReportLine
                {
                    Key = (g.Key.CategoryId == null ? "U" : g.Key.CategoryId.ToString()) + ":" + g.Key.Category,
                    CategoryId = g.Key.CategoryId, Name = g.Key.Category, Order = g.Key.Order,
                    Amount = g.Sum(e => e.Amount), Count = g.Count()
                }).ToListAsync(cancellationToken);
            var commission = (await CommissionPayoutQuery(projectId, from, toExclusive, null, false).SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m)
                - (await CommissionReversalQuery(projectId, from, toExclusive, null, false).SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m);
            var commissionCount = await CommissionPayoutQuery(projectId, from, toExclusive, null, false).CountAsync(cancellationToken)
                + await CommissionReversalQuery(projectId, from, toExclusive, null, false).CountAsync(cancellationToken);
            if (commission != 0m) expenses.Add(new ReportLine { Key = "commission-payouts", Name = "Commission Payouts", Order = int.MaxValue - 1, Amount = commission, Count = commissionCount });
            var rebate = (await CashRebateQuery(projectId, from, toExclusive, null, false).SumAsync(d => (decimal?)d.Amount, cancellationToken) ?? 0m)
                - (await CashRebateReversalQuery(projectId, from, toExclusive, null, false).SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m);
            var rebateCount = await CashRebateQuery(projectId, from, toExclusive, null, false).CountAsync(cancellationToken)
                + await CashRebateReversalQuery(projectId, from, toExclusive, null, false).CountAsync(cancellationToken);
            if (rebate != 0m) expenses.Add(new ReportLine { Key = "cash-rebates", Name = "Cash Rebates", Order = int.MaxValue, Amount = rebate, Count = rebateCount });
            // The cost of credits granted against a RECOGNISED sale. Once the full net sale value
            // is income, the slice of it the buyer will never pay has to be a cost — otherwise the
            // receivable would still be claiming money that was written off. Credits on bookings
            // that have not reached possession stay invisible here, exactly as before.
            var nonCashCredit = (await NonCashCreditQuery(projectId, from, toExclusive).SumAsync(d => (decimal?)d.Amount, cancellationToken) ?? 0m)
                - (await NonCashCreditReversalQuery(projectId, from, toExclusive).SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m);
            var nonCashCreditCount = await NonCashCreditQuery(projectId, from, toExclusive).CountAsync(cancellationToken)
                + await NonCashCreditReversalQuery(projectId, from, toExclusive).CountAsync(cancellationToken);
            if (nonCashCredit != 0m)
                expenses.Add(new ReportLine
                {
                    Key = "non-cash-credits", Name = "Customer Credits (non-cash)", Order = int.MaxValue,
                    Amount = nonCashCredit, Count = nonCashCreditCount
                });
            var loanInterest = await LoanInterestQuery(projectId, from, toExclusive, null, false)
                .GroupBy(_ => 1).Select(g => new { Amount = g.Sum(t => t.InterestAmount), Count = g.Count() })
                .SingleOrDefaultAsync(cancellationToken);
            if (loanInterest is { Amount: not 0m })
                expenses.Add(new ReportLine
                {
                    Key = "loan-interest", Name = "Loan Interest", Order = int.MaxValue - 2,
                    Amount = loanInterest.Amount, Count = loanInterest.Count
                });
            return new PnlPeriod(income.Where(l => Money(l.Amount) != 0m).OrderBy(l => l.Order).ThenBy(l => l.Name).ToList(),
                expenses.Where(l => Money(l.Amount) != 0m).OrderBy(l => l.Order).ThenBy(l => l.Name).ToList());
        }

        private async Task<List<AccountSnapshot>> AccountSnapshotsAsync(int? projectId, DateTime asAt, DateTime? openingDate, CancellationToken cancellationToken)
        {
            var end = asAt.Date.AddDays(1);
            var accounts = await _context.FinanceAccounts.AsNoTracking().OrderBy(a => a.DisplayOrder).ThenBy(a => a.Name)
                .Select(a => new AccountSnapshot { Id = a.Id, Name = a.Name, LedgerCode = a.LedgerCode, Type = a.Type,
                    SystemRole = a.SystemRole,
                    DisplayOrder = a.DisplayOrder, Balance = !projectId.HasValue && (!openingDate.HasValue || openingDate <= asAt) ? a.OpeningBalance : 0m })
                .ToListAsync(cancellationToken);
            var payments = await SumByAccount(PaymentsQuery(projectId, null, end).Where(p => p.FinanceAccountId != null)
                .GroupBy(p => p.FinanceAccountId!.Value).Select(g => new AccountAmount(g.Key, g.Sum(p => p.Amount))), cancellationToken);
            var manual = await SumByAccount(ManualQuery(projectId, null, end).Where(r => r.FinanceAccountId != null)
                .GroupBy(r => r.FinanceAccountId!.Value).Select(g => new AccountAmount(g.Key, g.Sum(r => r.Amount))), cancellationToken);
            var expense = await SumByAccount(ExpenseQuery(projectId, null, end).Where(e => e.FinanceAccountId != null)
                .GroupBy(e => e.FinanceAccountId!.Value).Select(g => new AccountAmount(g.Key, g.Sum(e => e.Amount - e.WhtAmount))), cancellationToken);
            // A purchase moves two accounts and touches no income statement line. Cash falls by the
            // net paid; the asset account rises by the gross. The gap between them is the withheld
            // tax, which lands on the payable below — which is exactly why the sheet still balances.
            var assetPaid = await SumByAccount(AssetPurchaseQuery(projectId, null, end, null, null, false)
                .GroupBy(p => p.FinanceAccountId).Select(g => new AccountAmount(g.Key, g.Sum(p => p.Amount - p.WhtAmount))), cancellationToken);
            var assetCapitalised = await SumByAccount(AssetPurchaseQuery(projectId, null, end, null, null, false)
                .GroupBy(p => p.AssetAccountId).Select(g => new AccountAmount(g.Key, g.Sum(p => p.Amount))), cancellationToken);
            var commission = await SumByAccount(CommissionPayoutQuery(projectId, null, end, null, false)
                .GroupBy(p => p.FinanceAccountId).Select(g => new AccountAmount(g.Key, g.Sum(p => p.Amount))), cancellationToken);
            var commissionReversal = await SumByAccount(CommissionReversalQuery(projectId, null, end, null, false)
                .GroupBy(r => r.Payout.FinanceAccountId).Select(g => new AccountAmount(g.Key, g.Sum(r => r.Amount))), cancellationToken);
            var rebate = await SumByAccount(CashRebateQuery(projectId, null, end, null, false).Where(d => d.FinanceAccountId != null)
                .GroupBy(d => d.FinanceAccountId!.Value).Select(g => new AccountAmount(g.Key, g.Sum(d => d.Amount))), cancellationToken);
            var rebateReversal = await SumByAccount(CashRebateReversalQuery(projectId, null, end, null, false).Where(r => r.Disbursement.FinanceAccountId != null)
                .GroupBy(r => r.Disbursement.FinanceAccountId!.Value).Select(g => new AccountAmount(g.Key, g.Sum(r => r.Amount))), cancellationToken);
            var deposits = !projectId.HasValue
                ? await SumByAccount(_context.WhtDeposits.AsNoTracking().Where(d => d.DepositDate < end)
                    .GroupBy(d => d.FinanceAccountId).Select(g => new AccountAmount(g.Key, g.Sum(d => d.Amount))), cancellationToken)
                : [];
            var capitalCash = !projectId.HasValue
                ? await _context.CapitalTransactions.AsNoTracking().Where(t => t.Date < end && t.FinanceAccountId != null
                    && (t.Type == CapitalTransactionType.Contribution || t.Type == CapitalTransactionType.Withdrawal))
                    .GroupBy(t => new { Id = t.FinanceAccountId!.Value, t.Type }).Select(g => new { g.Key.Id, g.Key.Type, Amount = g.Sum(t => t.Amount) }).ToListAsync(cancellationToken)
                : [];
            var capitalPartner = !projectId.HasValue
                ? await _context.CapitalTransactions.AsNoTracking().Where(t => t.Date < end && t.CapitalPartner.FinanceAccountId != null)
                    .GroupBy(t => new { Id = t.CapitalPartner.FinanceAccountId!.Value, t.Type }).Select(g => new { g.Key.Id, g.Key.Type, Amount = g.Sum(t => t.Amount) }).ToListAsync(cancellationToken)
                : [];
            var loanCash = !projectId.HasValue
                ? await _context.LoanTransactions.AsNoTracking().Where(t => t.Date < end)
                    .GroupBy(t => t.FinanceAccountId)
                    .Select(g => new AccountAmount(g.Key, g.Sum(t => t.Type == LoanTransactionType.Drawdown
                        ? t.PrincipalAmount : -(t.PrincipalAmount + t.InterestAmount))))
                    .ToListAsync(cancellationToken)
                : [];
            var loanLiability = !projectId.HasValue
                ? await _context.LoanTransactions.AsNoTracking().Where(t => t.Date < end)
                    .GroupBy(t => t.Loan.FinanceAccountId)
                    .Select(g => new AccountAmount(g.Key, g.Sum(t => t.Type == LoanTransactionType.Drawdown
                        ? t.PrincipalAmount : -t.PrincipalAmount)))
                    .ToListAsync(cancellationToken)
                : [];
            var staffTransfers = !projectId.HasValue
                ? await _context.StaffCashTransfers.AsNoTracking().Where(t => t.Date < end)
                    .Select(t => new
                    {
                        StaffId = t.StaffFinanceAccountId,
                        CounterpartyId = t.CounterpartyFinanceAccountId,
                        t.Type,
                        t.Amount
                    }).ToListAsync(cancellationToken)
                : [];
            var wht = (await ExpenseQuery(projectId, null, end).SumAsync(e => (decimal?)e.WhtAmount, cancellationToken) ?? 0m)
                + (await AssetPurchaseQuery(projectId, null, end, null, null, false)
                    .SumAsync(p => (decimal?)p.WhtAmount, cancellationToken) ?? 0m);
            var deposited = deposits.Sum(d => d.Amount);
            // Customer Refunds Payable: created out of the customer deposit at cancellation,
            // cleared when the cash is actually paid out. Both sides are booking-scoped, so —
            // unlike WHT deposits — they can be filtered by project.
            var cancellationRefundsCash = await SumByAccount(_context.BookingCancellationRefunds.AsNoTracking()
                .Where(r => r.PaidAt < end && (!projectId.HasValue || r.Settlement.Booking.Unit.ProjectId == projectId.Value))
                .GroupBy(r => r.FinanceAccountId).Select(g => new AccountAmount(g.Key, g.Sum(r => r.Amount))), cancellationToken);
            var refundPayableCreated = await CancellationSettlementQuery(projectId, null, end)
                .SumAsync(s => (decimal?)s.RefundAmount, cancellationToken) ?? 0m;
            var refundPayablePaid = cancellationRefundsCash.Sum(x => x.Amount);
            var (customerDeposits, customerReceivables) = await CustomerBalancesAsync(projectId, end, cancellationToken);

            foreach (var account in accounts)
            {
                if (account.Type == FinanceAccountType.Capital)
                {
                    account.Balance += capitalPartner.Where(x => x.Id == account.Id).Sum(x =>
                        x.Type is CapitalTransactionType.Withdrawal or CapitalTransactionType.LossShare ? -x.Amount : x.Amount);
                    continue;
                }
                var debitMovement = Amount(payments, account.Id) + Amount(manual, account.Id) - Amount(expense, account.Id)
                    - Amount(assetPaid, account.Id) + Amount(assetCapitalised, account.Id)
                    - Amount(commission, account.Id) + Amount(commissionReversal, account.Id)
                    - Amount(rebate, account.Id) + Amount(rebateReversal, account.Id) - Amount(deposits, account.Id)
                    - Amount(cancellationRefundsCash, account.Id)
                    + capitalCash.Where(x => x.Id == account.Id).Sum(x => x.Type == CapitalTransactionType.Contribution ? x.Amount : -x.Amount)
                    + Amount(loanCash, account.Id)
                    + staffTransfers.Where(t => t.StaffId == account.Id).Sum(t =>
                        t.Type == StaffCashMovementType.FundsGiven ? t.Amount : -t.Amount)
                    + staffTransfers.Where(t => t.CounterpartyId == account.Id).Sum(t =>
                        t.Type == StaffCashMovementType.FundsReturned ? t.Amount : -t.Amount);
                // These sources are expressed as business increases minus decreases, not raw
                // journal debits. The normal-balance direction is applied later when the trial
                // balance places the positive amount in a Debit or Credit column.
                account.Balance += debitMovement + Amount(loanLiability, account.Id);
                if (account.SystemRole == FinanceSystemAccountRole.TaxPayable)
                    account.Balance += Money(wht - deposited);
                if (account.SystemRole == FinanceSystemAccountRole.CustomerRefundPayable)
                    account.Balance += Money(refundPayableCreated - refundPayablePaid);
                if (account.SystemRole == FinanceSystemAccountRole.CustomerDeposits)
                    account.Balance += customerDeposits;
                if (account.SystemRole == FinanceSystemAccountRole.CustomerReceivables)
                    account.Balance += customerReceivables;
                account.Balance = Money(account.Balance);
            }
            return accounts;
        }

        /// <summary>
        /// The customer deposit liability and the customer receivable asset, both as at
        /// <paramref name="end"/> (exclusive) and both derived entirely from dated domain events —
        /// never from a booking's current status, and never from a stored snapshot that a
        /// back-dated payment could quietly invalidate.
        /// <para>
        /// Deposit: every payment taken while the sale was still unrecognised, less the ones since
        /// cleared by possession (into revenue) or by cancellation (into refund payable + retained
        /// income). Both sides come from the same Payment rows, so they cannot drift apart.
        /// </para>
        /// <para>
        /// Receivable: the recognised net sale value, less every payment on that booking (the
        /// pre-possession ones having already cleared the deposit) and less valid non-cash credits.
        /// A booking that has not reached possession contributes nothing — there is no receivable
        /// until there is a sale.
        /// </para>
        /// </summary>
        private async Task<(decimal Deposits, decimal Receivables)> CustomerBalancesAsync(
            int? projectId, DateTime? end, CancellationToken cancellationToken)
        {
            // "Recognised by the cut-off" and "cancelled by the cut-off". Written as two separate
            // queryables rather than one expression with a sentinel date, because a sentinel would
            // have to be compared against a `date` column and SQL Server's conversion rules at the
            // very end of the calendar are not somewhere financial code should be standing.
            var recognisedByCutOff = SaleRecognitionQuery(projectId, null, end);
            var clearedByRecognition = PaymentsQuery(projectId, null, end)
                .Where(p => p.Booking.SaleRecognition != null
                    && p.PaidAt < p.Booking.SaleRecognition.RecognitionDate.AddDays(1));
            var collectedOnRecognisedSales = PaymentsQuery(projectId, null, end)
                .Where(p => p.Booking.SaleRecognition != null);
            var clearedByCancellation = PaymentsQuery(projectId, null, end)
                .Where(p => p.Booking.CancellationSettlement != null);
            if (end.HasValue)
            {
                clearedByRecognition = clearedByRecognition
                    .Where(p => p.Booking.SaleRecognition!.RecognitionDate < end.Value);
                collectedOnRecognisedSales = collectedOnRecognisedSales
                    .Where(p => p.Booking.SaleRecognition!.RecognitionDate < end.Value);
                clearedByCancellation = clearedByCancellation
                    .Where(p => p.Booking.CancellationSettlement!.CancellationDate < end.Value);
            }

            var depositsReceived = await PaymentsQuery(projectId, null, end)
                .Where(p => p.Booking.SaleRecognition == null
                    || p.PaidAt < p.Booking.SaleRecognition.RecognitionDate.AddDays(1))
                .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
            var depositsCleared = (await clearedByRecognition.SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m)
                + (await clearedByCancellation.SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m);

            var receivablesRaised = await recognisedByCutOff.SumAsync(r => (decimal?)r.NetSaleValue, cancellationToken) ?? 0m;
            var receivablesCollected = await collectedOnRecognisedSales.SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
            var creditsApplied = await NonCashCreditQuery(projectId, null, end)
                .SumAsync(d => (decimal?)d.Amount, cancellationToken) ?? 0m;
            var creditsReversed = await NonCashCreditReversalQuery(projectId, null, end)
                .SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m;

            return (Money(depositsReceived - depositsCleared),
                Money(receivablesRaised - receivablesCollected - creditsApplied + creditsReversed));
        }

        private static async Task<List<AccountAmount>> SumByAccount(IQueryable<AccountAmount> query, CancellationToken cancellationToken) =>
            await query.ToListAsync(cancellationToken);

        private static decimal Amount(IEnumerable<AccountAmount> rows, int id) => rows.FirstOrDefault(x => x.Id == id)?.Amount ?? 0m;

        private async Task<DateTime?> OpeningDateAsync(CancellationToken cancellationToken) =>
            await _context.OpeningBalanceSets.AsNoTracking().Where(s => s.CommittedAt != null)
                .OrderByDescending(s => s.AsAtDate).Select(s => (DateTime?)s.AsAtDate).FirstOrDefaultAsync(cancellationToken);

        private async Task<List<string>> DiagnoseImbalanceAsync(int? projectId, DateTime asAt, List<AccountSnapshot> snapshots, CancellationToken cancellationToken)
        {
            var end = asAt.AddDays(1);
            var issues = new List<string>();
            if (await PaymentsQuery(projectId, null, end, null, true).AnyAsync(cancellationToken)) issues.Add("Unassigned customer payments");
            if (await ManualQuery(projectId, null, end, null, true).AnyAsync(cancellationToken)) issues.Add("Unassigned manual revenue");
            if (await ExpenseQuery(projectId, null, end, null, true).AnyAsync(cancellationToken)) issues.Add("Unassigned expenses");
            // No "unassigned asset purchases" check: both accounts are required on every purchase,
            // so the row that would cause this imbalance cannot be saved in the first place.
            var wht = (await ExpenseQuery(projectId, null, end).SumAsync(e => (decimal?)e.WhtAmount, cancellationToken) ?? 0m)
                + (await AssetPurchaseQuery(projectId, null, end, null, null, false)
                    .SumAsync(p => (decimal?)p.WhtAmount, cancellationToken) ?? 0m);
            var deposited = projectId.HasValue
                ? 0m
                : await _context.WhtDeposits.AsNoTracking().Where(d => d.DepositDate < end)
                    .SumAsync(d => (decimal?)d.Amount, cancellationToken) ?? 0m;
            var undeposited = Money(wht - deposited);
            if (undeposited != 0m && !snapshots.Any(s => s.SystemRole == FinanceSystemAccountRole.TaxPayable))
                issues.Add($"No Tax Payable system account found; withheld tax of {undeposited:N2} has nowhere to sit.");
            var refundObligation = Money((await CancellationSettlementQuery(projectId, null, end).SumAsync(s => (decimal?)s.RefundAmount, cancellationToken) ?? 0m)
                - (await _context.BookingCancellationRefunds.AsNoTracking()
                    .Where(r => r.PaidAt < end && (!projectId.HasValue || r.Settlement.Booking.Unit.ProjectId == projectId.Value))
                    .SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m));
            if (refundObligation != 0m && !snapshots.Any(s => s.SystemRole == FinanceSystemAccountRole.CustomerRefundPayable))
                issues.Add($"No Customer Refunds Payable system account found; outstanding refund obligation of {refundObligation:N2} has nowhere to sit.");
            // Reported, never created: a report must not write to the database as a side effect of
            // being read. Setting the account up is Finance ▸ Accounts ▸ chart setup's job.
            var (deposits, receivables) = await CustomerBalancesAsync(projectId, end, cancellationToken);
            if (deposits != 0m && !snapshots.Any(s => s.SystemRole == FinanceSystemAccountRole.CustomerDeposits))
                issues.Add($"No Customer Deposits system account found; customer money held of {deposits:N2} has nowhere to sit.");
            if (receivables != 0m && !snapshots.Any(s => s.SystemRole == FinanceSystemAccountRole.CustomerReceivables))
                issues.Add($"No Customer Receivables system account found; {receivables:N2} owed on recognised sales has nowhere to sit.");
            if (issues.Count == 0)
            {
                issues.Add("Opening balances or legacy entries are not double-sided");
                issues.AddRange(snapshots.Where(s => s.Balance != 0m).OrderByDescending(s => Math.Abs(s.Balance)).Take(8).Select(s => s.Name));
            }
            return issues;
        }

        private static List<PnlLineDto> MergeLines(List<ReportLine> current, List<ReportLine> prior)
        {
            return current.Select(l => l.Key).Union(prior.Select(l => l.Key)).Select(key =>
            {
                var now = current.SingleOrDefault(l => l.Key == key);
                var before = prior.SingleOrDefault(l => l.Key == key);
                return new { Sort = now?.Order ?? before!.Order, Name = now?.Name ?? before!.Name, Line = new PnlLineDto
                {
                    CategoryId = now?.CategoryId ?? before?.CategoryId, Name = now?.Name ?? before!.Name,
                    Amount = Money(now?.Amount ?? 0m), PriorAmount = Money(before?.Amount ?? 0m),
                    TransactionCount = now?.Count ?? 0
                }};
            }).Where(x => x.Line.Amount != 0m || x.Line.PriorAmount != 0m)
                .OrderBy(x => x.Sort).ThenBy(x => x.Name).Select(x => x.Line).ToList();
        }

        private static BsGroupDto Group(string name, IEnumerable<AccountSnapshot> accounts, params FinanceAccountType[] types)
        {
            var lines = accounts.Where(a => types.Contains(a.Type)).OrderBy(a => a.DisplayOrder).ThenBy(a => a.Name).Select(ToBsLine).ToList();
            return new BsGroupDto { Name = name, Lines = lines, Total = Money(lines.Sum(l => l.Amount)) };
        }

        private static BsGroupDto StaffFloatGroup(
            string name,
            IEnumerable<AccountSnapshot> accounts,
            bool positive)
        {
            var lines = accounts
                .Where(a => a.Type == FinanceAccountType.StaffFloat
                    && (positive ? a.Balance > 0m : a.Balance < 0m))
                .OrderBy(a => a.DisplayOrder).ThenBy(a => a.Name)
                .Select(a => new BsLineDto
                {
                    AccountId = a.Id, LedgerCode = a.LedgerCode, Name = a.Name,
                    Amount = Money(positive ? a.Balance : -a.Balance)
                }).ToList();
            return new BsGroupDto { Name = name, Lines = lines, Total = Money(lines.Sum(l => l.Amount)) };
        }

        private static BsLineDto ToBsLine(AccountSnapshot account) => new() { AccountId = account.Id, LedgerCode = account.LedgerCode, Name = account.Name, Amount = Money(account.Balance) };
        private static DateTime MonthEnd(DateTime date) => new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));
        private static DateTime ValidateReportDate(DateTime date, string field)
        {
            if (date.Date < SqlStart || date.Year > 9998)
                throw new InvalidOperationException($"{field} must be between {SqlStart:dd MMM yyyy} and 31 Dec 9998.");
            return date.Date;
        }
        private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
        private static int VirtualId(string value)
        {
            unchecked
            {
                var hash = 17;
                foreach (var ch in value) hash = hash * 31 + ch;
                return hash == int.MinValue ? -1 : -Math.Abs(hash == 0 ? 1 : hash);
            }
        }

        private sealed record PnlPeriod(List<ReportLine> Income, List<ReportLine> Expenses);
        private sealed class ReportLine
        {
            public string Key { get; set; } = string.Empty;
            public int? CategoryId { get; set; }
            public string Name { get; set; } = string.Empty;
            public int Order { get; set; }
            public decimal Amount { get; set; }
            public int Count { get; set; }
        }
        private sealed class AccountSnapshot
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string? LedgerCode { get; set; }
            public FinanceAccountType Type { get; set; }
            public FinanceSystemAccountRole SystemRole { get; set; }
            public int DisplayOrder { get; set; }
            public decimal Balance { get; set; }
        }
        private sealed record AccountAmount(int Id, decimal Amount);
        private sealed record TrialValue(int AccountId, string? LedgerCode, string Name, FinanceAccountType Type, decimal Debit, decimal Credit);
        private sealed class TrialRowBuilder
        {
            public int AccountId { get; }
            public string? LedgerCode { get; }
            public string Name { get; }
            public FinanceAccountType Type { get; }
            private readonly List<decimal> _debits = [];
            private readonly List<decimal> _credits = [];
            public TrialRowBuilder(int accountId, string? ledgerCode, string name, FinanceAccountType type)
            { AccountId = accountId; LedgerCode = ledgerCode; Name = name; Type = type; }
            public void Add(decimal debit, decimal credit) { _debits.Add(debit); _credits.Add(credit); }
            public TrialBalanceRowDto Build() => new() { AccountId = AccountId, LedgerCode = LedgerCode, AccountName = Name, Type = Type, DebitBalances = _debits, CreditBalances = _credits };
        }
    }
}
