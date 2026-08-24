using DAMS.Application.Common;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DAMS.Application.Services
{
    public partial class FinanceService
    {
        // The same floor the write side enforces, so a report window can never start earlier than
        // the earliest date a row is allowed to carry.
        private static readonly DateTime SqlStart = FinanceDateRules.SqlMin;

        public async Task<ProfitAndLossDto> GetProfitAndLossAsync(
            int? projectId, DateTime? from, DateTime? to, CancellationToken cancellationToken = default)
        {
            string? projectName = null;
            if (projectId.HasValue)
            {
                var project = await _context.Projects.AsNoTracking()
                    .Where(p => p.Id == projectId.Value)
                    .Select(p => new { p.ProjectName })
                    .SingleOrDefaultAsync(cancellationToken)
                    ?? throw new InvalidOperationException("Selected project does not exist.");
                projectName = project.ProjectName;
            }
            var (start, end, label) = await ResolveReportPeriodAsync(from, to, cancellationToken);
            var priorStart = start.AddYears(-1);
            var priorEnd = end.AddYears(-1);
            // WITH the fixed-asset charge, because this statement reports Net Profit and DAMS has
            // exactly one Net Profit rule: buying an asset spends the money, so the period bears it.
            // The Balance Sheet and Trial Balance below ask for the same period without it — see
            // BuildPnlPeriodAsync for why those two cannot carry it and what they disclose instead.
            var current = await BuildPnlPeriodAsync(projectId, start, end.AddDays(1), true, cancellationToken);
            var prior = await BuildPnlPeriodAsync(projectId, priorStart, priorEnd.AddDays(1), true, cancellationToken);
            var netProfit = Money(current.Income.Sum(l => l.Amount) - current.Expenses.Sum(l => l.Amount));
            var priorNetProfit = Money(prior.Income.Sum(l => l.Amount) - prior.Expenses.Sum(l => l.Amount));
            return new ProfitAndLossDto
            {
                PeriodStart = start, PeriodEnd = end, PeriodLabel = label, ProjectName = projectName,
                IncomeLines = MergeLines(current.Income, prior.Income),
                ExpenseLines = MergeLines(current.Expenses, prior.Expenses),
                TotalIncome = Money(current.Income.Sum(l => l.Amount)),
                TotalExpenses = Money(current.Expenses.Sum(l => l.Amount)),
                NetProfit = netProfit,
                PriorTotalIncome = Money(prior.Income.Sum(l => l.Amount)),
                PriorTotalExpenses = Money(prior.Expenses.Sum(l => l.Amount)),
                PriorNetProfit = priorNetProfit
            };
        }

        /// <summary>
        /// Fixed assets bought in the window, at gross cost: spending Net Profit deducts and the
        /// ledger cannot, because the asset stays at full cost and no balancing account has been
        /// approved. Disclosed by the Balance Sheet for its own window, never as a claim that the
        /// two statements reconcile — the ledger treatment is still an open accountant decision.
        /// </summary>
        private async Task<decimal> FixedAssetChargeAsync(
            int? projectId, DateTime from, DateTime toExclusive, CancellationToken cancellationToken) =>
            Money(await FixedAssetChargeQuery(projectId, from, toExclusive, null, false)
                .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m);

        public async Task<BalanceSheetDto> GetBalanceSheetAsync(
            int? projectId, DateTime asAt, CancellationToken cancellationToken = default)
        {
            await EnsureProjectExistsAsync(projectId, cancellationToken);
            var date = ValidateReportDate(asAt == default ? PakistanTime.Today : asAt.Date, "As-at date");
            var openingDate = await OpeningDateAsync(cancellationToken);
            var pnlStart = openingDate.HasValue && openingDate.Value <= date ? openingDate.Value : SqlStart;
            var snapshots = await AccountSnapshotsAsync(projectId, date, openingDate, cancellationToken);
            // WITHOUT the fixed-asset charge: this figure has to reconcile the sheet, and the only
            // entry the books hold for a purchase is Dr Fixed Asset / Cr Bank. Deducting the cost
            // here as well, while the asset is still carried at full cost above, would put the sheet
            // out by exactly that amount. The gap between this and the P&L's Net Profit is reported
            // as UnpostedFixedAssetCharge rather than left for the reader to discover.
            var pnl = await BuildPnlPeriodAsync(projectId, pnlStart, date.AddDays(1), false, cancellationToken);
            var retainedProfit = Money(pnl.Income.Sum(l => l.Amount) - pnl.Expenses.Sum(l => l.Amount));
            var unpostedFixedAssetCharge = await FixedAssetChargeAsync(projectId, pnlStart, date.AddDays(1), cancellationToken);
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
            // Retained profit carries only entries the books actually hold, so this balances on real
            // double entry alone. Fixed-asset purchases are absent by design: their cost has no
            // approved balancing account, so charging it here would put the sheet out by that amount
            // while the asset sits above at full cost. An imbalance below therefore still means what
            // it always meant — a genuine fault.
            var imbalance = Money(totalAssets - rhs);
            var result = new BalanceSheetDto
            {
                AsAt = date, AssetGroups = assetGroups, TotalAssets = totalAssets,
                LiabilityGroups = liabilityGroups,
                TotalLiabilities = totalLiabilities, CapitalLines = capitalLines,
                RetainedProfit = retainedProfit, TotalCapital = totalCapital,
                UnpostedFixedAssetCharge = unpostedFixedAssetCharge,
                RetainedProfitStart = pnlStart == SqlStart ? null : pnlStart,
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
            List<DateTime> dates;
            if (monthsBack == 0)
            {
                // A one-column Trial Balance means the day the user selected, not the previous
                // completed month. Historical multi-column callers retain the completed-month rule.
                dates = [date];
            }
            else
            {
                var currentMonthEnd = new DateTime(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));
                var monthEnd = date == currentMonthEnd ? date : new DateTime(date.Year, date.Month, 1).AddDays(-1);
                if (monthEnd.AddMonths(-monthsBack) < SqlStart)
                    throw new InvalidOperationException($"The requested trial-balance history cannot start before {SqlStart:dd MMM yyyy}.");
                dates = Enumerable.Range(0, monthsBack + 1)
                    .Select(offset => MonthEnd(monthEnd.AddMonths(offset - monthsBack))).ToList();
            }
            var openingDate = await OpeningDateAsync(cancellationToken);
            var columns = await LoadTrialBalanceColumnsAsync(projectId, dates, openingDate, cancellationToken);
            var builders = new Dictionary<string, TrialRowBuilder>(StringComparer.Ordinal);

            foreach (var columnDate in dates)
            {
                var values = new Dictionary<string, TrialValue>(StringComparer.Ordinal);
                var column = columns[columnDate];
                var snapshots = column.Accounts;
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

                // Ledger-only, for the same reason the Balance Sheet is: every line here is asserted
                // to have a debit and a credit, and the fixed-asset charge has only one.
                var pnl = column.Pnl;
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
                    var netAllocation = Money(column.NetAllocation);
                    if (column.HasAllocation)
                    {
                        values["EQ:allocated"] = new TrialValue(
                            VirtualId("EQ:allocated"), null, "Allocated profit / loss",
                            FinanceAccountType.Capital,
                            netAllocation > 0m ? netAllocation : 0m,
                            netAllocation < 0m ? -netAllocation : 0m);
                    }
                }

                foreach (var key in builders.Keys.Union(values.Keys).ToList())
                {
                    if (!builders.TryGetValue(key, out var builder))
                    {
                        var value = values[key];
                        builder = new TrialRowBuilder(key, value.AccountId, value.LedgerCode, value.Name, value.Type);
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
                AsAt = dates[^1],
                ColumnDates = dates, Rows = rows, ColumnDebitTotals = debitTotals, ColumnCreditTotals = creditTotals,
                ColumnBalanced = debitTotals.Zip(creditTotals, (d, c) => d == c).ToList(),
                TotalDebit = debitTotals[^1], TotalCredit = creditTotals[^1],
                IsBalanced = debitTotals[^1] == creditTotals[^1]
            };
        }

        public async Task<TrialBalanceAccountDetailsDto> GetTrialBalanceDetailsAsync(
            string accountKey, int? projectId, DateTime? from, DateTime? to,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(accountKey))
                throw new InvalidOperationException("Account key is required.");
            accountKey = accountKey.Trim();
            await EnsureProjectExistsAsync(projectId, cancellationToken);

            var toDate = ValidateReportDate((to ?? PakistanTime.Today).Date, "To date");
            var fromDate = ValidateReportDate((from ?? toDate).Date, "From date");
            if (fromDate > toDate)
                throw new InvalidOperationException("From date cannot be after to date.");

            var openingDate = await OpeningDateAsync(cancellationToken);

            // Looking the row up first both validates the opaque key and gives the exact identity
            // against which the closing detail must reconcile.
            var summary = await GetTrialBalanceAsync(projectId, toDate, 0, cancellationToken);
            var summaryRow = summary.Rows.SingleOrDefault(row => row.AccountKey == accountKey)
                ?? throw new InvalidOperationException(
                    "The selected Trial Balance account is not available for these filters.");

            TrialBalanceAccountDetailsDto details;
            if (TryPhysicalAccountId(accountKey, out var accountId))
            {
                var slice = await _accountService.GetTransactionLedgerSliceAsync(
                    accountId, projectId, fromDate, toDate, 0, int.MaxValue, cancellationToken);
                details = BuildPhysicalDetails(summaryRow, slice, fromDate, toDate);
            }
            else
            {
                var accumulationStart = accountKey == "EQ:allocated"
                    ? SqlStart
                    : openingDate.HasValue && openingDate.Value.Date <= toDate
                        ? openingDate.Value.Date
                        : SqlStart;
                var movements = await BuildVirtualLedgerAsync(
                    accountKey, projectId, accumulationStart, toDate.AddDays(1), cancellationToken);
                details = BuildVirtualDetails(summaryRow, movements, fromDate, toDate);
            }

            var detailDebit = details.ClosingBalanceType == "Debit" ? details.ClosingBalance : 0m;
            var detailCredit = details.ClosingBalanceType == "Credit" ? details.ClosingBalance : 0m;
            if (Money(detailDebit) != Money(summaryRow.Debit)
                || Money(detailCredit) != Money(summaryRow.Credit))
            {
                _logger.LogError(
                    "Trial Balance detail reconciliation failed for {AccountKey}. Summary Dr {SummaryDebit} Cr {SummaryCredit}; detail Dr {DetailDebit} Cr {DetailCredit}.",
                    accountKey, summaryRow.Debit, summaryRow.Credit, detailDebit, detailCredit);
                throw new InvalidOperationException(
                    "The account details could not be reconciled with the Trial Balance. Refresh and try again.");
            }

            return details;
        }

        private static TrialBalanceAccountDetailsDto BuildPhysicalDetails(
            TrialBalanceRowDto summaryRow, FinanceAccountLedgerSliceDto slice,
            DateTime from, DateTime to)
        {
            var debitNormal = AccountBalanceDirection.IsDebitNormal(slice.AccountType);
            var direction = debitNormal ? 1m : -1m;
            var openingSigned = Money(slice.OpeningNormalBalance * direction);
            var runningSigned = Money(openingSigned + slice.NormalMovementBeforePage * direction);
            var rows = new List<TrialBalanceTransactionDto>(slice.Items.Count);
            foreach (var item in slice.Items)
            {
                var movement = Money(item.Amount * direction);
                runningSigned = Money(runningSigned + movement);
                rows.Add(new TrialBalanceTransactionDto
                {
                    Id = $"{item.SourceOrder}:{item.RecordId}",
                    Date = item.Date.Date,
                    Description = Describe(item),
                    Reference = item.Reference,
                    Debit = movement > 0m ? movement : 0m,
                    Credit = movement < 0m ? -movement : 0m,
                    RunningBalance = Math.Abs(runningSigned),
                    RunningBalanceType = BalanceType(runningSigned)
                });
            }

            var closingSigned = Money(openingSigned + slice.PeriodNormalMovement * direction);
            return Details(summaryRow, from, to, openingSigned, closingSigned, rows);
        }

        private static TrialBalanceAccountDetailsDto BuildVirtualDetails(
            TrialBalanceRowDto summaryRow, List<TrialLedgerMovement> movements,
            DateTime from, DateTime to)
        {
            var openingSigned = Money(movements.Where(row => row.Date < from)
                .Sum(row => row.Debit - row.Credit));
            var period = movements.Where(row => row.Date >= from && row.Date < to.AddDays(1))
                .OrderBy(row => row.Date).ThenBy(row => row.PostedAt)
                .ThenBy(row => row.SourceOrder).ThenBy(row => row.RecordId).ToList();
            var runningSigned = openingSigned;
            var rows = new List<TrialBalanceTransactionDto>(period.Count);
            foreach (var item in period)
            {
                runningSigned = Money(runningSigned + item.Debit - item.Credit);
                rows.Add(new TrialBalanceTransactionDto
                {
                    Id = $"{item.SourceOrder}:{item.RecordId}", Date = item.Date.Date,
                    Description = item.Description, Reference = item.Reference,
                    Debit = Money(item.Debit), Credit = Money(item.Credit),
                    RunningBalance = Math.Abs(runningSigned),
                    RunningBalanceType = BalanceType(runningSigned)
                });
            }
            return Details(summaryRow, from, to, openingSigned, runningSigned, rows);
        }

        private static TrialBalanceAccountDetailsDto Details(
            TrialBalanceRowDto summaryRow, DateTime from, DateTime to,
            decimal openingSigned, decimal closingSigned, List<TrialBalanceTransactionDto> rows) => new()
        {
            AccountName = summaryRow.AccountName,
            LedgerCode = summaryRow.LedgerCode,
            From = from,
            To = to,
            OpeningBalance = Math.Abs(Money(openingSigned)),
            OpeningBalanceType = BalanceType(openingSigned),
            ClosingBalance = Math.Abs(Money(closingSigned)),
            ClosingBalanceType = BalanceType(closingSigned),
            TotalDebit = Money(rows.Sum(row => row.Debit)),
            TotalCredit = Money(rows.Sum(row => row.Credit)),
            Rows = rows
        };

        private static string Describe(FinanceAccountTransactionDto row)
        {
            var detail = string.IsNullOrWhiteSpace(row.Description) ? row.Label : row.Description.Trim();
            return string.IsNullOrWhiteSpace(detail) ? row.Kind : $"{row.Kind} - {detail}";
        }

        private static string BalanceType(decimal signedDebitBalance) =>
            signedDebitBalance < 0m ? "Credit" : "Debit";

        private static bool TryPhysicalAccountId(string accountKey, out int accountId)
        {
            accountId = 0;
            return accountKey.StartsWith("A:", StringComparison.Ordinal)
                && int.TryParse(accountKey.AsSpan(2), out accountId)
                && accountId > 0;
        }

        private async Task<List<TrialLedgerMovement>> BuildVirtualLedgerAsync(
            string accountKey, int? projectId, DateTime accumulationStart, DateTime toExclusive,
            CancellationToken cancellationToken)
        {
            if (accountKey == "I:unit-sales")
            {
                return await SaleRecognitionQuery(projectId, accumulationStart, toExclusive)
                    .Select(row => new TrialLedgerMovement
                    {
                        RecordId = row.Id, Date = row.RecognitionDate, PostedAt = row.RecognizedAt,
                        SourceOrder = 200, Description = "Unit sale - " + row.Booking.Customer.FullName,
                        Reference = row.Booking.BookingReference,
                        Debit = row.NetSaleValue < 0m ? -row.NetSaleValue : 0m,
                        Credit = row.NetSaleValue < 0m ? 0m : row.NetSaleValue
                    }).ToListAsync(cancellationToken);
            }

            if (accountKey == "I:cancellation-retained")
            {
                return await RetainedCancellationQuery(projectId, accumulationStart, toExclusive)
                    .Select(row => new TrialLedgerMovement
                    {
                        RecordId = row.Id, Date = row.CancellationDate, PostedAt = row.CancelledAt,
                        SourceOrder = 201,
                        Description = "Cancellation income - " + row.Booking.Customer.FullName,
                        Reference = row.Booking.BookingReference,
                        Debit = row.RetainedAmount < 0m ? -row.RetainedAmount : 0m,
                        Credit = row.RetainedAmount < 0m ? 0m : row.RetainedAmount
                    }).ToListAsync(cancellationToken);
            }

            if (accountKey.StartsWith("I:", StringComparison.Ordinal))
            {
                var (categoryId, categoryName, unclassified) = ParseVirtualCategoryKey(accountKey, "I:");
                var query = ManualQuery(projectId, accumulationStart, toExclusive);
                query = unclassified
                    ? query.Where(row => row.RevenueCategoryId == null
                        || row.RevenueCategory!.Code.StartsWith("legacy_"))
                    : query.Where(row => row.RevenueCategoryId == categoryId
                        && row.RevenueTypeName == categoryName);
                return await query.Select(row => new TrialLedgerMovement
                {
                    RecordId = row.Id, Date = row.Date, PostedAt = row.CreatedAt,
                    SourceOrder = 202,
                    Description = row.Description ?? row.RevenueTypeName,
                    Reference = row.Reference,
                    Debit = row.Amount < 0m ? -row.Amount : 0m,
                    Credit = row.Amount < 0m ? 0m : row.Amount
                }).ToListAsync(cancellationToken);
            }

            if (accountKey == "E:commission-payouts")
            {
                var rows = await CommissionPayoutQuery(
                        projectId, accumulationStart, toExclusive, null, false)
                    .Select(row => new TrialLedgerMovement
                    {
                        RecordId = row.Id, Date = row.PaymentDate, PostedAt = row.RecordedAt,
                        SourceOrder = 210,
                        Description = "Commission payout - " + row.Commission.Partner.Name,
                        Reference = row.PaymentReference,
                        Debit = row.Amount, Credit = 0m
                    }).ToListAsync(cancellationToken);
                rows.AddRange(await CommissionReversalQuery(
                        projectId, accumulationStart, toExclusive, null, false)
                    .Select(row => new TrialLedgerMovement
                    {
                        RecordId = row.Id, Date = row.ReversedAt, PostedAt = row.ReversedAt,
                        SourceOrder = 211,
                        Description = "Commission reversal - " + row.Payout.Commission.Partner.Name,
                        Reference = row.Reason,
                        Debit = 0m, Credit = row.Amount
                    }).ToListAsync(cancellationToken));
                return rows;
            }

            if (accountKey == "E:cash-rebates")
            {
                var rows = await CashRebateQuery(
                        projectId, accumulationStart, toExclusive, null, false)
                    .Select(row => new TrialLedgerMovement
                    {
                        RecordId = row.Id, Date = row.AppliedAt, PostedAt = row.RecordedAt,
                        SourceOrder = 220,
                        Description = "Customer rebate - " + row.Rebate.Customer.FullName,
                        Reference = row.Reference,
                        Debit = row.Amount, Credit = 0m
                    }).ToListAsync(cancellationToken);
                rows.AddRange(await CashRebateReversalQuery(
                        projectId, accumulationStart, toExclusive, null, false)
                    .Select(row => new TrialLedgerMovement
                    {
                        RecordId = row.Id, Date = row.ReversedAt, PostedAt = row.ReversedAt,
                        SourceOrder = 221,
                        Description = "Rebate reversal - " + row.Disbursement.Rebate.Customer.FullName,
                        Reference = row.Reason,
                        Debit = 0m, Credit = row.Amount
                    }).ToListAsync(cancellationToken));
                return rows;
            }

            if (accountKey == "E:non-cash-credits")
            {
                var rows = await NonCashCreditQuery(projectId, accumulationStart, toExclusive)
                    .Select(row => new TrialLedgerMovement
                    {
                        RecordId = row.Id,
                        Date = row.AppliedAt < row.Rebate.Booking.SaleRecognition!.RecognitionDate.AddDays(1)
                            ? row.Rebate.Booking.SaleRecognition!.RecognitionDate : row.AppliedAt,
                        PostedAt = row.AppliedAt < row.Rebate.Booking.SaleRecognition!.RecognitionDate.AddDays(1)
                            ? row.Rebate.Booking.SaleRecognition!.RecognizedAt : row.RecordedAt,
                        SourceOrder = 230,
                        Description = "Customer credit - " + row.Rebate.Customer.FullName,
                        Reference = row.Reference,
                        Debit = row.Amount, Credit = 0m
                    }).ToListAsync(cancellationToken);
                rows.AddRange(await NonCashCreditReversalQuery(projectId, accumulationStart, toExclusive)
                    .Select(row => new TrialLedgerMovement
                    {
                        RecordId = row.Id,
                        Date = row.ReversedAt < row.Disbursement.Rebate.Booking.SaleRecognition!.RecognitionDate.AddDays(1)
                            ? row.Disbursement.Rebate.Booking.SaleRecognition!.RecognitionDate : row.ReversedAt,
                        PostedAt = row.ReversedAt < row.Disbursement.Rebate.Booking.SaleRecognition!.RecognitionDate.AddDays(1)
                            ? row.Disbursement.Rebate.Booking.SaleRecognition!.RecognizedAt : row.ReversedAt,
                        SourceOrder = 231,
                        Description = "Customer credit reversal - " + row.Disbursement.Rebate.Customer.FullName,
                        Reference = row.Reason,
                        Debit = 0m, Credit = row.Amount
                    }).ToListAsync(cancellationToken));
                return rows;
            }

            if (accountKey == "E:loan-interest")
            {
                return await LoanInterestQuery(projectId, accumulationStart, toExclusive, null, false)
                    .Select(row => new TrialLedgerMovement
                    {
                        RecordId = row.Id, Date = row.Date, PostedAt = row.CreatedAt,
                        SourceOrder = 240,
                        Description = "Loan interest - " + row.Loan.Name,
                        Reference = row.Reference,
                        Debit = row.InterestAmount, Credit = 0m
                    }).ToListAsync(cancellationToken);
            }

            if (accountKey.StartsWith("E:", StringComparison.Ordinal))
            {
                var (categoryId, categoryName, unclassified) = ParseVirtualCategoryKey(accountKey, "E:");
                var query = ExpenseQuery(projectId, accumulationStart, toExclusive);
                query = unclassified
                    ? query.Where(row => row.CategoryId == null && row.Category == categoryName)
                    : query.Where(row => row.CategoryId == categoryId && row.Category == categoryName);
                return await query.Select(row => new TrialLedgerMovement
                {
                    RecordId = row.Id, Date = row.Date, PostedAt = row.CreatedAt,
                    SourceOrder = 250,
                    Description = row.Description ?? row.Category,
                    Reference = row.Vendor,
                    Debit = row.Amount < 0m ? 0m : row.Amount,
                    Credit = row.Amount < 0m ? -row.Amount : 0m
                }).ToListAsync(cancellationToken);
            }

            if (accountKey == "EQ:allocated")
            {
                if (projectId.HasValue) return [];
                return await _context.CapitalTransactions.AsNoTracking()
                    .Where(row => row.Date >= accumulationStart && row.Date < toExclusive
                        && (row.Type == CapitalTransactionType.ProfitShare
                            || row.Type == CapitalTransactionType.LossShare))
                    .Select(row => new TrialLedgerMovement
                    {
                        RecordId = row.Id, Date = row.Date, PostedAt = row.CreatedAt,
                        SourceOrder = 260,
                        Description = row.Type == CapitalTransactionType.ProfitShare
                            ? "Profit allocated - " + row.CapitalPartner.Name
                            : "Loss allocated - " + row.CapitalPartner.Name,
                        Reference = row.Reference,
                        Debit = row.Type == CapitalTransactionType.ProfitShare ? row.Amount : 0m,
                        Credit = row.Type == CapitalTransactionType.LossShare ? row.Amount : 0m
                    }).ToListAsync(cancellationToken);
            }

            throw new InvalidOperationException(
                "Details are not supported for this Trial Balance account key.");
        }

        private static (int? CategoryId, string Name, bool Unclassified) ParseVirtualCategoryKey(
            string accountKey, string prefix)
        {
            var source = accountKey[prefix.Length..];
            var separator = source.IndexOf(':');
            if (separator <= 0 || separator == source.Length - 1)
                throw new InvalidOperationException("The Trial Balance account key is invalid.");
            var idPart = source[..separator];
            var name = source[(separator + 1)..];
            if (idPart == "U") return (null, name, true);
            if (!int.TryParse(idPart, out var id) || id <= 0)
                throw new InvalidOperationException("The Trial Balance account key is invalid.");
            return (id, name, false);
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
            rows.Add(new object?[] { "Retained Profit (per the ledger)", report.RetainedProfit });
            rows.Add(new object?[] { "Total Liabilities & Capital", report.TotalLiabilitiesAndCapital });
            rows.Add(new object?[] { "Balanced", report.IsBalanced ? "Yes" : "No" });
            // This workbook is what reaches the accountant who has to decide where the balancing
            // entry belongs, so it states the one difference between this sheet and the P&L rather
            // than leaving it to be discovered by subtraction.
            if (report.UnpostedFixedAssetCharge != 0m)
            {
                rows.Add(new object?[] { "Fixed asset purchases charged to Net Profit but not to this sheet", report.UnpostedFixedAssetCharge });
                rows.Add(new object?[] { "Bought " + (report.RetainedProfitStart.HasValue
                    ? "between " + report.RetainedProfitStart.Value.ToString("dd MMM yyyy") + " and " + report.AsAt.ToString("dd MMM yyyy")
                    : "up to " + report.AsAt.ToString("dd MMM yyyy")) + ", the window Retained Profit above covers", null });
                rows.Add(new object?[] { "Retained Profit is stated before that charge: the assets are still carried at full cost and the balancing ledger treatment is an open accountant decision", null });
            }
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

        /// <param name="includeFixedAssetCharge">
        /// True for the P&amp;L, false for the Balance Sheet and Trial Balance. See the fixed-asset
        /// block near the end of this method for why the same period is built both ways.
        /// </param>
        private async Task<PnlPeriod> BuildPnlPeriodAsync(int? projectId, DateTime from, DateTime toExclusive,
            bool includeFixedAssetCharge, CancellationToken cancellationToken)
        {
            // Every source has the same flat transport shape, so SQL Server can UNION ALL and
            // aggregate them in one command. The previous implementation waited for nine commands
            // per period; a comparison P&L paid that cost twice.
            IQueryable<PnlAggregateRow> rows = ManualQuery(projectId, from, toExclusive)
                .Select(r => new PnlAggregateRow
                {
                    Source = PnlAggregateSource.ManualRevenue,
                    CategoryId = r.RevenueCategoryId == null || r.RevenueCategory!.Code.StartsWith("legacy_")
                        ? null : r.RevenueCategoryId,
                    Name = r.RevenueCategoryId == null || r.RevenueCategory!.Code.StartsWith("legacy_")
                        ? "Unclassified" : r.RevenueTypeName,
                    Order = r.RevenueCategory == null || r.RevenueCategory.Code.StartsWith("legacy_")
                        ? int.MaxValue : r.RevenueCategory.DisplayOrder,
                    Amount = r.Amount,
                    Count = 1
                })
                .Concat(SaleRecognitionQuery(projectId, from, toExclusive)
                    .Select(r => new PnlAggregateRow
                    {
                        Source = PnlAggregateSource.RecognisedSale,
                        CategoryId = null,
                        Name = "Unit Sales",
                        Order = -1,
                        Amount = r.NetSaleValue,
                        Count = 1
                    }))
                .Concat(RetainedCancellationQuery(projectId, from, toExclusive)
                    .Select(s => new PnlAggregateRow
                    {
                        Source = PnlAggregateSource.RetainedCancellation,
                        CategoryId = null,
                        Name = "Cancellation Income (Retained)",
                        Order = 0,
                        Amount = s.RetainedAmount,
                        Count = 1
                    }))
                .Concat(ExpenseQuery(projectId, from, toExclusive)
                    .Select(e => new PnlAggregateRow
                    {
                        Source = PnlAggregateSource.OrdinaryExpense,
                        CategoryId = e.CategoryId,
                        Name = e.Category,
                        Order = e.ExpenseCategory == null ? int.MaxValue : e.ExpenseCategory.DisplayOrder,
                        Amount = e.Amount,
                        Count = 1
                    }))
                .Concat(CommissionPayoutQuery(projectId, from, toExclusive, null, false)
                    .Select(p => new PnlAggregateRow
                    {
                        Source = PnlAggregateSource.Commission,
                        CategoryId = null,
                        Name = "Commission Payouts",
                        Order = int.MaxValue - 1,
                        Amount = p.Amount,
                        Count = 1
                    }))
                .Concat(CommissionReversalQuery(projectId, from, toExclusive, null, false)
                    .Select(r => new PnlAggregateRow
                    {
                        Source = PnlAggregateSource.Commission,
                        CategoryId = null,
                        Name = "Commission Payouts",
                        Order = int.MaxValue - 1,
                        Amount = -r.Amount,
                        Count = 1
                    }))
                .Concat(CashRebateQuery(projectId, from, toExclusive, null, false)
                    .Select(d => new PnlAggregateRow
                    {
                        Source = PnlAggregateSource.CashRebate,
                        CategoryId = null,
                        Name = "Cash Rebates",
                        Order = int.MaxValue,
                        Amount = d.Amount,
                        Count = 1
                    }))
                .Concat(CashRebateReversalQuery(projectId, from, toExclusive, null, false)
                    .Select(r => new PnlAggregateRow
                    {
                        Source = PnlAggregateSource.CashRebate,
                        CategoryId = null,
                        Name = "Cash Rebates",
                        Order = int.MaxValue,
                        Amount = -r.Amount,
                        Count = 1
                    }))
                .Concat(NonCashCreditQuery(projectId, from, toExclusive)
                    .Select(d => new PnlAggregateRow
                    {
                        Source = PnlAggregateSource.NonCashCredit,
                        CategoryId = null,
                        Name = "Customer Credits (non-cash)",
                        Order = int.MaxValue,
                        Amount = d.Amount,
                        Count = 1
                    }))
                .Concat(NonCashCreditReversalQuery(projectId, from, toExclusive)
                    .Select(r => new PnlAggregateRow
                    {
                        Source = PnlAggregateSource.NonCashCredit,
                        CategoryId = null,
                        Name = "Customer Credits (non-cash)",
                        Order = int.MaxValue,
                        Amount = -r.Amount,
                        Count = 1
                    }))
                .Concat(LoanInterestQuery(projectId, from, toExclusive, null, false)
                    .Select(t => new PnlAggregateRow
                    {
                        Source = PnlAggregateSource.LoanInterest,
                        CategoryId = null,
                        Name = "Loan Interest",
                        Order = int.MaxValue - 2,
                        Amount = t.InterestAmount,
                        Count = 1
                    }));

            // Fixed-asset spending is part of the P&L's Net Profit, but not ledger-only retained
            // profit on the Balance Sheet. A conditional branch keeps that rule in the same query.
            if (includeFixedAssetCharge)
            {
                rows = rows.Concat(FixedAssetChargeQuery(projectId, from, toExclusive, null, false)
                    .Select(p => new PnlAggregateRow
                    {
                        Source = PnlAggregateSource.FixedAssetPurchase,
                        CategoryId = null,
                        Name = "Fixed Asset Purchases",
                        Order = int.MaxValue - 3,
                        Amount = p.Amount,
                        Count = 1
                    }));
            }

            var totals = await rows.GroupBy(row => new
                {
                    row.Source,
                    row.CategoryId,
                    row.Name,
                    row.Order
                })
                .Select(group => new PnlAggregateRow
                {
                    Source = group.Key.Source,
                    CategoryId = group.Key.CategoryId,
                    Name = group.Key.Name,
                    Order = group.Key.Order,
                    Amount = group.Sum(row => row.Amount),
                    Count = group.Sum(row => row.Count)
                })
                .ToListAsync(cancellationToken);

            var income = new List<ReportLine>();
            var expenses = new List<ReportLine>();
            foreach (var total in totals)
            {
                if (Money(total.Amount) == 0m) continue;
                var line = new ReportLine
                {
                    Key = PnlKey(total),
                    CategoryId = total.CategoryId,
                    Name = total.Name,
                    Order = total.Order,
                    Amount = total.Amount,
                    Count = total.Count
                };
                if (total.Source is PnlAggregateSource.ManualRevenue
                    or PnlAggregateSource.RecognisedSale
                    or PnlAggregateSource.RetainedCancellation)
                    income.Add(line);
                else
                    expenses.Add(line);
            }

            return new PnlPeriod(
                income.OrderBy(line => line.Order).ThenBy(line => line.Name).ToList(),
                expenses.OrderBy(line => line.Order).ThenBy(line => line.Name).ToList());
        }

        private static string PnlKey(PnlAggregateRow row) => row.Source switch
        {
            PnlAggregateSource.RecognisedSale => "unit-sales",
            PnlAggregateSource.RetainedCancellation => "cancellation-retained",
            PnlAggregateSource.Commission => "commission-payouts",
            PnlAggregateSource.CashRebate => "cash-rebates",
            PnlAggregateSource.NonCashCredit => "non-cash-credits",
            PnlAggregateSource.FixedAssetPurchase => "fixed-asset-purchases",
            PnlAggregateSource.LoanInterest => "loan-interest",
            _ => (row.CategoryId.HasValue ? row.CategoryId.Value.ToString() : "U") + ":" + row.Name
        };

        private async Task<List<AccountSnapshot>> AccountSnapshotsAsync(int? projectId, DateTime asAt, DateTime? openingDate, CancellationToken cancellationToken)
        {
            var end = asAt.Date.AddDays(1);
            var accounts = await _context.FinanceAccounts.AsNoTracking().OrderBy(a => a.DisplayOrder).ThenBy(a => a.Name)
                .Select(a => new AccountSnapshot { Id = a.Id, Name = a.Name, LedgerCode = a.LedgerCode, Type = a.Type,
                    SystemRole = a.SystemRole,
                    DisplayOrder = a.DisplayOrder, Balance = !projectId.HasValue && (!openingDate.HasValue || openingDate <= asAt) ? a.OpeningBalance : 0m })
                .ToListAsync(cancellationToken);
            // Every ordinary account movement is emitted with its final sign, then SQL Server reads
            // and groups the sources in one UNION ALL command. The previous implementation issued a
            // separate command for each table even though the application only needed one number per
            // account from all of them.
            var movementRows = PaymentsQuery(projectId, null, end).Where(p => p.FinanceAccountId != null)
                .Select(p => new AccountMovementRow { Id = p.FinanceAccountId!.Value, Amount = p.Amount })
                .Concat(ManualQuery(projectId, null, end).Where(r => r.FinanceAccountId != null)
                    .Select(r => new AccountMovementRow { Id = r.FinanceAccountId!.Value, Amount = r.Amount }))
                .Concat(ExpenseQuery(projectId, null, end).Where(e => e.FinanceAccountId != null)
                    .Select(e => new AccountMovementRow { Id = e.FinanceAccountId!.Value, Amount = -(e.Amount - e.WhtAmount) }));
            // A purchase moves two accounts: cash falls by the net paid, the asset account rises by
            // the gross, and the gap between them is the withheld tax, which lands on the payable
            // below. That is what balances the CASH side. The charge the same purchase makes against
            // profit has no counter-entry anywhere — the asset account itself is never written down.
            movementRows = movementRows
                .Concat(AssetPurchaseQuery(projectId, null, end, null, null, false)
                    .Select(p => new AccountMovementRow { Id = p.FinanceAccountId, Amount = -(p.Amount - p.WhtAmount) }))
                .Concat(AssetPurchaseQuery(projectId, null, end, null, null, false)
                    .Select(p => new AccountMovementRow { Id = p.AssetAccountId, Amount = p.Amount }))
                .Concat(CommissionPayoutQuery(projectId, null, end, null, false)
                    .Select(p => new AccountMovementRow { Id = p.FinanceAccountId, Amount = -p.Amount }))
                .Concat(CommissionReversalQuery(projectId, null, end, null, false)
                    .Select(r => new AccountMovementRow { Id = r.Payout.FinanceAccountId, Amount = r.Amount }))
                .Concat(CashRebateQuery(projectId, null, end, null, false).Where(d => d.FinanceAccountId != null)
                    .Select(d => new AccountMovementRow { Id = d.FinanceAccountId!.Value, Amount = -d.Amount }))
                .Concat(CashRebateReversalQuery(projectId, null, end, null, false).Where(r => r.Disbursement.FinanceAccountId != null)
                    .Select(r => new AccountMovementRow { Id = r.Disbursement.FinanceAccountId!.Value, Amount = r.Amount }))
                .Concat(_context.BookingCancellationRefunds.AsNoTracking()
                    .Where(r => r.PaidAt < end && (!projectId.HasValue || r.Settlement.Booking.Unit.ProjectId == projectId.Value))
                    .Select(r => new AccountMovementRow { Id = r.FinanceAccountId, Amount = -r.Amount }));

            if (!projectId.HasValue)
            {
                movementRows = movementRows
                    .Concat(_context.WhtDeposits.AsNoTracking().Where(d => d.DepositDate < end)
                        .Select(d => new AccountMovementRow { Id = d.FinanceAccountId, Amount = -d.Amount }))
                    .Concat(_context.CapitalTransactions.AsNoTracking().Where(t => t.Date < end && t.FinanceAccountId != null
                        && (t.Type == CapitalTransactionType.Contribution || t.Type == CapitalTransactionType.Withdrawal))
                        .Select(t => new AccountMovementRow
                        {
                            Id = t.FinanceAccountId!.Value,
                            Amount = t.Type == CapitalTransactionType.Contribution ? t.Amount : -t.Amount
                        }))
                    .Concat(_context.LoanTransactions.AsNoTracking().Where(t => t.Date < end)
                        .Select(t => new AccountMovementRow
                        {
                            Id = t.FinanceAccountId,
                            Amount = t.Type == LoanTransactionType.Drawdown
                                ? t.PrincipalAmount : -(t.PrincipalAmount + t.InterestAmount)
                        }))
                    .Concat(_context.LoanTransactions.AsNoTracking().Where(t => t.Date < end)
                        .Select(t => new AccountMovementRow
                        {
                            Id = t.Loan.FinanceAccountId,
                            Amount = t.Type == LoanTransactionType.Drawdown ? t.PrincipalAmount : -t.PrincipalAmount
                        }))
                    .Concat(_context.StaffCashTransfers.AsNoTracking().Where(t => t.Date < end)
                        .Select(t => new AccountMovementRow
                        {
                            Id = t.StaffFinanceAccountId,
                            Amount = t.Type == StaffCashMovementType.FundsGiven ? t.Amount : -t.Amount
                        }))
                    .Concat(_context.StaffCashTransfers.AsNoTracking().Where(t => t.Date < end)
                        .Select(t => new AccountMovementRow
                        {
                            Id = t.CounterpartyFinanceAccountId,
                            Amount = t.Type == StaffCashMovementType.FundsReturned ? t.Amount : -t.Amount
                        }));
            }

            var movements = await SumByAccount(movementRows.GroupBy(row => row.Id)
                .Select(group => new AccountAmount(group.Key, group.Sum(row => row.Amount))), cancellationToken);
            var capitalPartner = !projectId.HasValue
                ? await _context.CapitalTransactions.AsNoTracking().Where(t => t.Date < end && t.CapitalPartner.FinanceAccountId != null)
                    .GroupBy(t => new { Id = t.CapitalPartner.FinanceAccountId!.Value, t.Type }).Select(g => new { g.Key.Id, g.Key.Type, Amount = g.Sum(t => t.Amount) }).ToListAsync(cancellationToken)
                : [];
            var whtRows = ExpenseQuery(projectId, null, end).Select(e => e.WhtAmount)
                .Concat(AssetPurchaseQuery(projectId, null, end, null, null, false).Select(p => p.WhtAmount));
            if (!projectId.HasValue)
            {
                whtRows = whtRows.Concat(_context.WhtDeposits.AsNoTracking().Where(d => d.DepositDate < end)
                    .Select(d => -d.Amount));
            }
            var whtPayable = Money(await whtRows.SumAsync(amount => (decimal?)amount, cancellationToken) ?? 0m);
            // Customer Refunds Payable: created out of the customer deposit at cancellation,
            // cleared when the cash is actually paid out. Both sides are booking-scoped, so —
            // unlike WHT deposits — they can be filtered by project.
            var refundPayableRows = CancellationSettlementQuery(projectId, null, end)
                .Select(s => s.RefundAmount)
                .Concat(_context.BookingCancellationRefunds.AsNoTracking()
                    .Where(r => r.PaidAt < end && (!projectId.HasValue || r.Settlement.Booking.Unit.ProjectId == projectId.Value))
                    .Select(r => -r.Amount));
            var refundPayable = Money(await refundPayableRows.SumAsync(amount => (decimal?)amount, cancellationToken) ?? 0m);
            var (customerDeposits, customerReceivables) = await CustomerBalancesAsync(projectId, end, cancellationToken);

            foreach (var account in accounts)
            {
                if (account.Type == FinanceAccountType.Capital)
                {
                    account.Balance += capitalPartner.Where(x => x.Id == account.Id).Sum(x =>
                        x.Type is CapitalTransactionType.Withdrawal or CapitalTransactionType.LossShare ? -x.Amount : x.Amount);
                    continue;
                }
                var debitMovement = Amount(movements, account.Id);
                // These sources are expressed as business increases minus decreases, not raw
                // journal debits. The normal-balance direction is applied later when the trial
                // balance places the positive amount in a Debit or Credit column.
                account.Balance += debitMovement;
                if (account.SystemRole == FinanceSystemAccountRole.TaxPayable)
                    account.Balance += whtPayable;
                if (account.SystemRole == FinanceSystemAccountRole.CustomerRefundPayable)
                    account.Balance += refundPayable;
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
        /// Deposit: every payment taken while the sale was still unrecognised — an earlier business
        /// date, or the possession date itself but entered before possession was recorded (the
        /// same-day ordering comes from the UTC audit instants, CreatedAt vs RecognizedAt) — less the ones since
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
            var collectedOnRecognisedSales = PaymentsQuery(projectId, null, end)
                .Where(p => p.Booking.SaleRecognition != null);
            if (end.HasValue)
            {
                collectedOnRecognisedSales = collectedOnRecognisedSales
                    .Where(p => p.Booking.SaleRecognition!.RecognitionDate < end.Value);
            }

            var deposits = await CustomerDepositBalanceAsync(projectId, end, cancellationToken);

            // Keep every source as a signed row until SQL Server has combined and summed it. This
            // preserves the accounting equation while replacing four independent round trips with
            // one UNION ALL aggregate.
            var receivables = recognisedByCutOff.Select(r => r.NetSaleValue)
                .Concat(collectedOnRecognisedSales.Select(p => -p.Amount))
                .Concat(NonCashCreditQuery(projectId, null, end).Select(d => -d.Amount))
                .Concat(NonCashCreditReversalQuery(projectId, null, end).Select(r => r.Amount));

            return (deposits,
                Money(await receivables.SumAsync(amount => (decimal?)amount, cancellationToken) ?? 0m));
        }

        /// <summary>
        /// The deposit half of <see cref="CustomerBalancesAsync"/> on its own: customer money held
        /// but not yet earned, as at <paramref name="end"/> (exclusive).
        /// <para>
        /// Split out for the dashboard card, which wants only this. All three accounting sources are
        /// emitted as signed rows and summed by SQL Server in one query, so the card and the Balance
        /// Sheet cannot report different deposits.
        /// </para>
        /// </summary>
        private async Task<decimal> CustomerDepositBalanceAsync(
            int? projectId, DateTime? end, CancellationToken cancellationToken)
        {
            var clearedByRecognition = PaymentsQuery(projectId, null, end)
                .Where(p => p.Booking.SaleRecognition != null
                    && (p.PaidAt < p.Booking.SaleRecognition.RecognitionDate || (p.PaidAt < p.Booking.SaleRecognition.RecognitionDate.AddDays(1) && p.CreatedAt <= p.Booking.SaleRecognition.RecognizedAt)));
            var clearedByCancellation = PaymentsQuery(projectId, null, end)
                .Where(p => p.Booking.CancellationSettlement != null);
            if (end.HasValue)
            {
                clearedByRecognition = clearedByRecognition
                    .Where(p => p.Booking.SaleRecognition!.RecognitionDate < end.Value);
                clearedByCancellation = clearedByCancellation
                    .Where(p => p.Booking.CancellationSettlement!.CancellationDate < end.Value);
            }

            var depositsReceived = PaymentsQuery(projectId, null, end)
                .Where(p => p.Booking.SaleRecognition == null
                    || (p.PaidAt < p.Booking.SaleRecognition.RecognitionDate || (p.PaidAt < p.Booking.SaleRecognition.RecognitionDate.AddDays(1) && p.CreatedAt <= p.Booking.SaleRecognition.RecognizedAt)))
                .Select(p => p.Amount);
            var balance = depositsReceived
                .Concat(clearedByRecognition.Select(p => -p.Amount))
                .Concat(clearedByCancellation.Select(p => -p.Amount));

            return Money(await balance.SumAsync(amount => (decimal?)amount, cancellationToken) ?? 0m);
        }

        private static async Task<List<AccountAmount>> SumByAccount(IQueryable<AccountAmount> query, CancellationToken cancellationToken) =>
            await query.ToListAsync(cancellationToken);

        private static decimal Amount(IEnumerable<AccountAmount> rows, int id) => rows.FirstOrDefault(x => x.Id == id)?.Amount ?? 0m;

        private Task<DateTime?> OpeningDateAsync(CancellationToken cancellationToken) =>
            FinanceDateRules.BaselineAsync(_context, cancellationToken);

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

        private enum PnlAggregateSource
        {
            ManualRevenue,
            RecognisedSale,
            RetainedCancellation,
            OrdinaryExpense,
            Commission,
            CashRebate,
            NonCashCredit,
            FixedAssetPurchase,
            LoanInterest
        }

        private sealed class PnlAggregateRow
        {
            public PnlAggregateSource Source { get; set; }
            public int? CategoryId { get; set; }
            public string Name { get; set; } = string.Empty;
            public int Order { get; set; }
            public decimal Amount { get; set; }
            public int Count { get; set; }
        }

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
        private sealed class AccountMovementRow
        {
            public int Id { get; set; }
            public decimal Amount { get; set; }
        }
        private sealed record AccountAmount(int Id, decimal Amount);
        private sealed record TrialValue(int AccountId, string? LedgerCode, string Name, FinanceAccountType Type, decimal Debit, decimal Credit);
        private sealed class TrialLedgerMovement
        {
            public int RecordId { get; set; }
            public DateTime Date { get; set; }
            public DateTime PostedAt { get; set; }
            public int SourceOrder { get; set; }
            public string Description { get; set; } = string.Empty;
            public string? Reference { get; set; }
            public decimal Debit { get; set; }
            public decimal Credit { get; set; }
        }
        private sealed class TrialRowBuilder
        {
            public string AccountKey { get; }
            public int AccountId { get; }
            public string? LedgerCode { get; }
            public string Name { get; }
            public FinanceAccountType Type { get; }
            private readonly List<decimal> _debits = [];
            private readonly List<decimal> _credits = [];
            public TrialRowBuilder(string accountKey, int accountId, string? ledgerCode, string name, FinanceAccountType type)
            { AccountKey = accountKey; AccountId = accountId; LedgerCode = ledgerCode; Name = name; Type = type; }
            public void Add(decimal debit, decimal credit) { _debits.Add(debit); _credits.Add(credit); }
            public TrialBalanceRowDto Build() => new()
            {
                AccountKey = AccountKey, AccountId = AccountId, LedgerCode = LedgerCode,
                AccountName = Name, Type = Type, DebitBalances = _debits, CreditBalances = _credits,
                Debit = _debits.Count == 0 ? 0m : _debits[^1],
                Credit = _credits.Count == 0 ? 0m : _credits[^1]
            };
        }
    }
}
