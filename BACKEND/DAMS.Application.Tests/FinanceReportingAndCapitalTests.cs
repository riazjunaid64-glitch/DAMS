using DAMS.Application.DTOs.BookingDtos;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DAMS.Application.Tests;

public sealed class FinanceReportingAndCapitalTests
{
    [Fact]
    public async Task ProfitAndLoss_GroupsSnapshotsAtGross_AndBalancesWithTheTrialBalance()
    {
        await using var context = Context();
        var bank = new FinanceAccount { Name = "Bank", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank, IsActive = true };
        var taxPayable = new FinanceAccount
        {
            Name = "Tax Payable", LedgerCode = "11", AccountHolderName = "DAMS",
            Type = FinanceAccountType.Liability, SystemRole = FinanceSystemAccountRole.TaxPayable, IsActive = true
        };
        var revenueCategory = new RevenueCategory { Name = "Other Income", Code = "other", DisplayOrder = 10 };
        var expenseCategory = new ExpenseCategory { Name = "Office Rent", Code = "rent", DisplayOrder = 10 };
        context.AddRange(bank, taxPayable, revenueCategory, expenseCategory);
        await context.SaveChangesAsync();
        context.ManualRevenues.Add(new ManualRevenue
        {
            FinanceAccountId = bank.Id, RevenueCategoryId = revenueCategory.Id,
            RevenueType = "Other Income", RevenueTypeName = "Other Income", Amount = 1_000m,
            Date = new DateTime(2026, 8, 1)
        });
        context.Expenses.Add(new Expense
        {
            FinanceAccountId = bank.Id, CategoryId = expenseCategory.Id, Category = "Office Rent",
            Amount = 200m, WhtAmount = 20m, Date = new DateTime(2026, 8, 2)
        });
        await context.SaveChangesAsync();
        var service = Finance(context);

        var pnl = await service.GetProfitAndLossAsync(null, new DateTime(2026, 7, 1), new DateTime(2027, 6, 30));

        Assert.Equal(1_000m, pnl.TotalIncome);
        Assert.Equal(200m, pnl.TotalExpenses); // gross, not the 180 cash paid
        Assert.Equal(800m, pnl.NetProfit);
        Assert.Equal("Other Income", Assert.Single(pnl.IncomeLines).Name);
        Assert.Equal("Office Rent", Assert.Single(pnl.ExpenseLines).Name);

        var trial = await service.GetTrialBalanceAsync(null, new DateTime(2026, 8, 31), 0);
        Assert.True(Assert.Single(trial.ColumnBalanced));
        Assert.Equal(1_020m, Assert.Single(trial.ColumnDebitTotals));
        Assert.Equal(1_020m, Assert.Single(trial.ColumnCreditTotals));

        var sheet = await service.GetBalanceSheetAsync(null, new DateTime(2026, 8, 31));
        Assert.True(sheet.IsBalanced);
        Assert.Equal(820m, sheet.TotalAssets);
        Assert.Equal(pnl.NetProfit, sheet.RetainedProfit);
        Assert.Equal(20m, (await new FinanceAccountService(context).GetByIdAsync(taxPayable.Id)).CurrentBalance);
    }

    [Fact]
    public async Task TaxPayableSystemRole_BalancesWht_AndProtectsIdentity()
    {
        await using var context = Context();
        var bank = new FinanceAccount { Name = "Bank", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank, IsActive = true };
        var taxPayable = new FinanceAccount
        {
            Name = "Withholding payable", LedgerCode = "WHT-LIAB", AccountHolderName = "DAMS",
            Type = FinanceAccountType.Liability, SystemRole = FinanceSystemAccountRole.TaxPayable, IsActive = true
        };
        context.AddRange(bank, taxPayable);
        await context.SaveChangesAsync();
        context.Expenses.Add(new Expense
        {
            FinanceAccountId = bank.Id, Category = "Supplier invoice",
            Amount = 1_000m, WhtAmount = 100m, Date = new DateTime(2026, 8, 2)
        });
        await context.SaveChangesAsync();
        var accounts = new FinanceAccountService(context);

        var sheet = await Finance(context).GetBalanceSheetAsync(null, new DateTime(2026, 8, 31));

        Assert.True(sheet.IsBalanced);
        Assert.Equal(100m, (await accounts.GetByIdAsync(taxPayable.Id)).CurrentBalance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.UpdateAsync(taxPayable.Id, new UpdateFinanceAccountDto
        {
            Name = "Renamed", Type = FinanceAccountType.Liability, AccountHolderName = "DAMS",
            OpeningBalance = 0m, LedgerCode = "WHT-LIAB", ConcurrencyToken = ""
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.UpdateAsync(taxPayable.Id, new UpdateFinanceAccountDto
        {
            Name = "Withholding payable", Type = FinanceAccountType.Bank, AccountHolderName = "DAMS",
            OpeningBalance = 0m, LedgerCode = "WHT-LIAB", ConcurrencyToken = ""
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.UpdateAsync(taxPayable.Id, new UpdateFinanceAccountDto
        {
            Name = "Withholding payable", Type = FinanceAccountType.Liability, AccountHolderName = "DAMS",
            OpeningBalance = 0m, LedgerCode = "11", ConcurrencyToken = ""
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.SetActiveAsync(taxPayable.Id, false, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.DeleteUnusedAsync(taxPayable.Id));
    }

    [Fact]
    public async Task BalanceSheet_DiagnosesMissingTaxPayableSystemAccount()
    {
        await using var context = Context();
        var bank = new FinanceAccount { Name = "Bank", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank, IsActive = true };
        context.FinanceAccounts.Add(bank);
        await context.SaveChangesAsync();
        context.Expenses.Add(new Expense
        {
            FinanceAccountId = bank.Id, Category = "Supplier invoice",
            Amount = 1_000m, WhtAmount = 100m, Date = new DateTime(2026, 8, 2)
        });
        await context.SaveChangesAsync();

        var sheet = await Finance(context).GetBalanceSheetAsync(null, new DateTime(2026, 8, 31));

        Assert.False(sheet.IsBalanced);
        Assert.Contains(sheet.UnbalancedAccounts, issue => issue.Contains("No Tax Payable system account found"));
    }

    [Fact]
    public async Task ProfitAndLoss_DefaultPeriodHonoursConfiguredYear_AndLegacyRowsAreUnclassified()
    {
        await using var context = Context();
        context.FinanceSettings.Add(new FinanceSetting { Id = 1, FinancialYearStartMonth = 7 });
        context.ManualRevenues.AddRange(
            new ManualRevenue { RevenueType = "Legacy spelling", RevenueTypeName = "Legacy spelling", Amount = 5m, Date = new DateTime(2026, 7, 1) },
            new ManualRevenue { RevenueType = "Outside", RevenueTypeName = "Outside", Amount = 9m, Date = new DateTime(2026, 6, 30) });
        await context.SaveChangesAsync();

        var pnl = await Finance(context).GetProfitAndLossAsync(null, null, null);

        Assert.Equal(new DateTime(2026, 7, 1), pnl.PeriodStart);
        Assert.Equal(new DateTime(2027, 6, 30), pnl.PeriodEnd);
        var line = Assert.Single(pnl.IncomeLines);
        Assert.Equal("Unclassified", line.Name);
        Assert.Equal(5m, line.Amount);
    }

    [Fact]
    public async Task AccountPickerAndOverview_ExcludeNonCashAccounts_AndDirectionIsApplied()
    {
        await using var context = Context();
        context.FinanceAccounts.AddRange(
            new FinanceAccount { Id = 1, Name = "Bank", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank, OpeningBalance = 100m, IsActive = true },
            new FinanceAccount { Id = 2, Name = "Loan", AccountHolderName = "DAMS", Type = FinanceAccountType.Liability, OpeningBalance = 500m, IsActive = true });
        context.ManualRevenues.Add(new ManualRevenue { FinanceAccountId = 2, RevenueType = "Legacy", RevenueTypeName = "Legacy", Amount = 50m });
        await context.SaveChangesAsync();
        var service = new FinanceAccountService(context);

        Assert.Single(await service.GetOptionsAsync(false));
        Assert.Equal(100m, (await service.GetOverviewAsync()).TotalBalance);
        var liability = await service.GetByIdAsync(2);
        Assert.Equal(50m, liability.NetMovement);
        Assert.Equal(550m, liability.CurrentBalance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureSelectableAsync(2));
    }

    [Fact]
    public async Task OpeningBalanceCommit_RequiresEquality_WritesNormalSigns_AndReopenIsAudited()
    {
        await using var context = Context();
        context.FinanceAccounts.AddRange(
            new FinanceAccount { Id = 1, Name = "Bank", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank, IsActive = true },
            new FinanceAccount { Id = 2, Name = "Capital", AccountHolderName = "Partner", Type = FinanceAccountType.Capital, IsActive = true });
        await context.SaveChangesAsync();
        var service = new OpeningBalanceService(context);
        var set = await service.CreateAsync(new DateTime(2026, 7, 1), 1);
        set = await service.SaveAsync(set.Id, new SaveOpeningBalanceSetDto
        {
            ConcurrencyToken = set.ConcurrencyToken,
            Entries =
            [
                new() { FinanceAccountId = 1, DebitAmount = 1_000m },
                new() { FinanceAccountId = 2, CreditAmount = 900m }
            ]
        }, 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CommitAsync(set.Id, set.ConcurrencyToken, 1));
        set = await service.SaveAsync(set.Id, new SaveOpeningBalanceSetDto
        {
            ConcurrencyToken = set.ConcurrencyToken,
            Entries =
            [
                new() { FinanceAccountId = 1, DebitAmount = 1_000m },
                new() { FinanceAccountId = 2, CreditAmount = 1_000m }
            ]
        }, 1);
        set = await service.CommitAsync(set.Id, set.ConcurrencyToken, 1);
        Assert.True(set.IsCommitted);
        Assert.All(await context.FinanceAccounts.ToListAsync(), account => Assert.Equal(1_000m, account.OpeningBalance));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(set.Id, new SaveOpeningBalanceSetDto(), 1));
        set = await service.ReopenAsync(set.Id, new ReopenOpeningBalanceSetDto
        {
            WarningAccepted = true, Note = "Accountant correction", ConcurrencyToken = set.ConcurrencyToken
        }, 7);
        Assert.False(set.IsCommitted);
        Assert.Contains(set.AuditEntries, entry => entry.Action == "Reopened" && entry.UserId == 7);
    }

    [Fact]
    public async Task OpeningBalanceDraft_IncludesAccountsAddedLater_AndControlsTheirOpeningAmount()
    {
        await using var context = Context();
        context.FinanceAccounts.Add(new FinanceAccount
        {
            Id = 1, Name = "Bank", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank, IsActive = true
        });
        await context.SaveChangesAsync();
        var service = new OpeningBalanceService(context);
        var set = await service.CreateAsync(new DateTime(2026, 7, 1), 1);
        context.FinanceAccounts.Add(new FinanceAccount
        {
            Id = 2, Name = "Receivable", AccountHolderName = "DAMS", Type = FinanceAccountType.Receivable,
            OpeningBalance = 500m, IsActive = true
        });
        await context.SaveChangesAsync();

        set = Assert.IsType<OpeningBalanceSetDto>(await service.GetCurrentAsync());
        Assert.Equal(2, set.Entries.Count);
        set = await service.SaveAsync(set.Id, new SaveOpeningBalanceSetDto
        {
            ConcurrencyToken = set.ConcurrencyToken,
            Entries = set.Entries.Select(e => new SaveOpeningBalanceEntryDto { FinanceAccountId = e.FinanceAccountId }).ToList()
        }, 1);
        await service.CommitAsync(set.Id, set.ConcurrencyToken, 1);

        Assert.All(await context.FinanceAccounts.ToListAsync(), account => Assert.Equal(0m, account.OpeningBalance));
    }

    [Fact]
    public async Task AUsedRevenueCategory_IsRetiredRatherThanDeleted_SoHistoricIncomeKeepsItsLabel()
    {
        await using var context = Context();
        var bank = new FinanceAccount { Name = "Bank", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank, IsActive = true };
        var category = new RevenueCategory { Name = "Consultancy", Code = "consultancy", IsActive = true };
        context.AddRange(bank, category);
        await context.SaveChangesAsync();
        context.ManualRevenues.Add(new ManualRevenue
        {
            FinanceAccountId = bank.Id, RevenueCategoryId = category.Id,
            RevenueType = "Consultancy", RevenueTypeName = "Consultancy", Amount = 5_000m,
            Date = new DateTime(2026, 8, 1)
        });
        await context.SaveChangesAsync();
        var service = new RevenueCategoryService(context);

        var retired = await service.DeleteAsync(category.Id);

        Assert.NotNull(retired);
        Assert.False(retired.IsActive);
        Assert.True(await context.RevenueCategories.AnyAsync(c => c.Id == category.Id));
        // The revenue keeps the name it was filed under, which is the point of retiring.
        Assert.Equal("Consultancy", await context.ManualRevenues.Select(r => r.RevenueTypeName).SingleAsync());

        // Retiring an already retired category is a no-op, not an error.
        Assert.False((await service.DeleteAsync(category.Id))!.IsActive);
    }

    [Fact]
    public async Task ANeverUsedRevenueCategory_IsDeletedOutright_LikeAnExpenseCategory()
    {
        await using var context = Context();
        var category = new RevenueCategory { Name = "Mistyped", Code = "mistyped", IsActive = true };
        context.RevenueCategories.Add(category);
        await context.SaveChangesAsync();

        Assert.Null(await new RevenueCategoryService(context).DeleteAsync(category.Id));
        Assert.False(await context.RevenueCategories.AnyAsync(c => c.Id == category.Id));
    }

    [Fact]
    public async Task RenamingARevenueCategory_LeavesTheNameAlreadyRecordedOnRevenue()
    {
        await using var context = Context();
        var bank = new FinanceAccount { Name = "Bank", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank, IsActive = true };
        var category = new RevenueCategory { Name = "Rentl Incom", Code = "rental", DisplayOrder = 10, IsActive = true };
        context.AddRange(bank, category);
        await context.SaveChangesAsync();
        context.ManualRevenues.Add(new ManualRevenue
        {
            FinanceAccountId = bank.Id, RevenueCategoryId = category.Id,
            RevenueType = "Rentl Incom", RevenueTypeName = "Rentl Incom", Amount = 900m,
            Date = new DateTime(2026, 8, 1)
        });
        await context.SaveChangesAsync();

        var renamed = await new RevenueCategoryService(context).UpdateAsync(category.Id, new SaveRevenueCategoryDto
        {
            Name = "Rental Income", Code = category.Code, DisplayOrder = 20, IsActive = true,
            ConcurrencyToken = Convert.ToBase64String(category.RowVersion)
        });

        Assert.Equal("Rental Income", renamed.Name);
        Assert.Equal(20, renamed.DisplayOrder);
        // Correcting the spelling must not restate a filed income statement.
        Assert.Equal("Rentl Incom", await context.ManualRevenues.Select(r => r.RevenueTypeName).SingleAsync());
    }

    [Fact]
    public async Task ARetiredRevenueCategory_IsOfferedToNobodyNew_ButStaysOnTheRowThatUsesIt()
    {
        await using var context = Context();
        context.RevenueCategories.AddRange(
            new RevenueCategory { Name = "Live", Code = "live", DisplayOrder = 10, IsActive = true },
            new RevenueCategory { Name = "Retired", Code = "retired", DisplayOrder = 20, IsActive = false });
        await context.SaveChangesAsync();
        var service = new RevenueCategoryService(context);

        Assert.Equal(["Live"], (await service.GetAllAsync(false)).Select(c => c.Name));
        Assert.Equal(["Live", "Retired"], (await service.GetAllAsync(true)).Select(c => c.Name));
    }

    [Fact]
    public async Task InactivePartner_CanBeStagedBeforeAtomicShareActivation()
    {
        await using var context = Context();
        var service = new CapitalPartnerService(context, new FinanceAccountService(context));

        var partner = await service.CreateAsync(new SaveCapitalPartnerDto
        {
            Name = "Future Partner", ProfitSharePercent = 25m, IsActive = false
        });

        Assert.False(partner.IsActive);
        Assert.Equal(25m, partner.ProfitSharePercent);
    }

    [Fact]
    public async Task TrialBalance_UsesRealCompletedMonthEnds()
    {
        await using var context = Context();

        var trial = await Finance(context).GetTrialBalanceAsync(null, new DateTime(2026, 8, 15), 12);

        Assert.Equal(13, trial.ColumnDates.Count);
        Assert.Equal(new DateTime(2026, 7, 31), trial.ColumnDates[^1]);
        Assert.All(trial.ColumnDates, date => Assert.Equal(DateTime.DaysInMonth(date.Year, date.Month), date.Day));
        Assert.All(trial.ColumnBalanced, Assert.True);
    }

    [Fact]
    public async Task CapitalContributionMovesBothAccounts_AndProfitSnapshotNeverChanges()
    {
        await using var context = Context();
        var bank = new FinanceAccount { Name = "Bank", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank, IsActive = true };
        var capital1 = new FinanceAccount { Name = "A Capital", AccountHolderName = "A", Type = FinanceAccountType.Capital, IsActive = true };
        var capital2 = new FinanceAccount { Name = "B Capital", AccountHolderName = "B", Type = FinanceAccountType.Capital, IsActive = true };
        var a = new CapitalPartner { Name = "A", ProfitSharePercent = 50m, FinanceAccount = capital1 };
        var b = new CapitalPartner { Name = "B", ProfitSharePercent = 50m, FinanceAccount = capital2 };
        context.AddRange(bank, a, b);
        await context.SaveChangesAsync();
        var accounts = new FinanceAccountService(context);
        var partners = new CapitalPartnerService(context, accounts);

        await partners.RecordTransactionAsync(a.Id, new SaveCapitalTransactionDto
        {
            Type = CapitalTransactionType.Contribution, Amount = 1_000m, Date = new DateTime(2026, 7, 1), FinanceAccountId = bank.Id
        }, 1);
        var profit = await partners.RecordTransactionAsync(a.Id, new SaveCapitalTransactionDto
        {
            Type = CapitalTransactionType.ProfitShare, Amount = 100m, Date = new DateTime(2026, 7, 2)
        }, 1);
        Assert.Equal(50m, profit.ProfitSharePercentSnapshot);
        Assert.Equal(1_000m, (await accounts.GetByIdAsync(bank.Id)).CurrentBalance);
        Assert.Equal(1_100m, (await accounts.GetByIdAsync(capital1.Id)).CurrentBalance);

        var current = await partners.GetAllAsync(true);
        await partners.UpdateSharesAsync(new SaveCapitalPartnerSharesDto
        {
            Partners = current.Select(row => new CapitalPartnerShareDto
            {
                Id = row.Id, ProfitSharePercent = row.Name == "A" ? 60m : 40m,
                IsActive = row.IsActive, ConcurrencyToken = row.ConcurrencyToken
            }).ToList()
        });
        Assert.Equal(50m, (await context.CapitalTransactions.SingleAsync(t => t.Id == profit.Id)).ProfitSharePercentSnapshot);
    }

    /// <summary>
    /// A cancellation earns the RETAINED amount and nothing else. The customer's payments were a
    /// deposit, never income, so the refund cannot be negative income either — it converts one
    /// liability into another.
    /// </summary>
    [Fact]
    public async Task CancellationSettlement_PayNow_EarnsOnlyTheRetainedAmount_AndBalances()
    {
        await using var context = Context();
        var (bookingId, bank) = await SeedCancellableBooking(context, paid: 500_000m);
        var booking = new BookingService(context, new CustomerService(context), new FinanceAccountService(context));
        await booking.CancelBookingAsync(bookingId, new CancelBookingDto
        {
            Reason = "Customer requested cancellation", ExpectedCustomerCashReceived = 500_000m,
            RefundAmount = 450_000m, RefundDecision = CancellationRefundDecision.PayNow, IdempotencyKey = "pn-1",
            RefundFinanceAccountId = bank.Id, RefundPaymentMethod = PaymentMethod.Cash,
            RefundPaidAt = DAMS.Application.Common.PakistanTime.Today
        }, new DAMS.Application.Common.FinancialWorkflowActor(1, "Admin"));

        // Pakistan time, not raw UTC: the cancellation itself is dated by PakistanTime.Today (see
        // BookingService.Cancellation.cs), and UTC lags PKT by up to ~5 hours — using UtcNow.Date
        // here would flake for report queries run between 00:00 and 04:59 PKT.
        var today = DAMS.Application.Common.PakistanTime.Today;
        var pnl = await Finance(context).GetProfitAndLossAsync(null, today.AddDays(-1), today.AddDays(1));
        Assert.Equal(50_000m, Assert.Single(pnl.IncomeLines, l => l.Name == "Cancellation Income (Retained)").Amount);
        Assert.DoesNotContain(pnl.IncomeLines, l => l.Name == "Customer Receipts");
        Assert.DoesNotContain(pnl.IncomeLines, l => l.Name == "Customer Refunds");
        Assert.Equal(50_000m, pnl.TotalIncome);
        Assert.Equal(0m, pnl.TotalExpenses); // never folded into expenses — it is income, once
        Assert.Equal(50_000m, pnl.NetProfit);

        var accounts = new FinanceAccountService(context);
        Assert.Equal(50_000m, (await accounts.GetByIdAsync(bank.Id)).CurrentBalance);
        var payable = await context.FinanceAccounts.SingleAsync(a => a.SystemRole == FinanceSystemAccountRole.CustomerRefundPayable);
        Assert.Equal(0m, (await accounts.GetByIdAsync(payable.Id)).CurrentBalance);

        var sheet = await Finance(context).GetBalanceSheetAsync(null, today);
        Assert.True(sheet.IsBalanced);
        Assert.Equal(50_000m, sheet.TotalAssets);
        Assert.Equal(0m, sheet.TotalLiabilities);
        Assert.Equal(50_000m, sheet.RetainedProfit);

        // GetTrialBalanceAsync snaps a mid-month "as at" date back to the END OF THE PREVIOUS
        // month, so the current month-end (not "today") must be passed to actually include today's
        // cancellation in the column.
        var monthEnd = new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        var trial = await Finance(context).GetTrialBalanceAsync(null, monthEnd, 0);
        var retainedRow = Assert.Single(trial.Rows, r => r.AccountName == "Cancellation Income (Retained)");
        Assert.Equal(0m, Assert.Single(retainedRow.DebitBalances));
        Assert.Equal(50_000m, Assert.Single(retainedRow.CreditBalances));
        Assert.DoesNotContain(trial.Rows, r => r.AccountName == "Customer Receipts");
        Assert.Equal(50_000m, Assert.Single(trial.ColumnDebitTotals));
        Assert.Equal(50_000m, Assert.Single(trial.ColumnCreditTotals));
    }

    [Fact]
    public async Task CancellationSettlement_PayLater_RecognisesLiabilityImmediately_ThenClearsOnPayment_WithNoSecondPnLEffect()
    {
        await using var context = Context();
        var (bookingId, bank) = await SeedCancellableBooking(context, paid: 500_000m);
        var booking = new BookingService(context, new CustomerService(context), new FinanceAccountService(context));
        await booking.CancelBookingAsync(bookingId, new CancelBookingDto
        {
            Reason = "Customer requested cancellation", ExpectedCustomerCashReceived = 500_000m,
            RefundAmount = 450_000m, RefundDecision = CancellationRefundDecision.PayLater, IdempotencyKey = "pl-1"
        }, new DAMS.Application.Common.FinancialWorkflowActor(1, "Admin"));

        // Pakistan time, not raw UTC: the cancellation itself is dated by PakistanTime.Today (see
        // BookingService.Cancellation.cs), and UTC lags PKT by up to ~5 hours — using UtcNow.Date
        // here would flake for report queries run between 00:00 and 04:59 PKT.
        var today = DAMS.Application.Common.PakistanTime.Today;
        var accounts = new FinanceAccountService(context);
        var payable = await context.FinanceAccounts.SingleAsync(a => a.SystemRole == FinanceSystemAccountRole.CustomerRefundPayable);

        // Before payment: cash untouched, liability recognised, P&L already shows the net figure.
        Assert.Equal(500_000m, (await accounts.GetByIdAsync(bank.Id)).CurrentBalance);
        Assert.Equal(450_000m, (await accounts.GetByIdAsync(payable.Id)).CurrentBalance);
        var pnlBefore = await Finance(context).GetProfitAndLossAsync(null, today.AddDays(-1), today.AddDays(1));
        Assert.Equal(50_000m, pnlBefore.NetProfit);
        var sheetBefore = await Finance(context).GetBalanceSheetAsync(null, today);
        Assert.True(sheetBefore.IsBalanced);
        Assert.Equal(500_000m, sheetBefore.TotalAssets);
        Assert.Equal(450_000m, sheetBefore.TotalLiabilities);
        Assert.Equal(50_000m, sheetBefore.RetainedProfit);

        await booking.PayCancellationRefundAsync(bookingId, new PayCancellationRefundDto
        {
            FinanceAccountId = bank.Id, PaymentMethod = PaymentMethod.Cash,
            PaidAt = DAMS.Application.Common.PakistanTime.Today, IdempotencyKey = "pay-later-1"
        }, new DAMS.Application.Common.FinancialWorkflowActor(1, "Admin"));

        // After payment: cash moved, liability cleared, P&L unchanged (no second recognition).
        Assert.Equal(50_000m, (await accounts.GetByIdAsync(bank.Id)).CurrentBalance);
        Assert.Equal(0m, (await accounts.GetByIdAsync(payable.Id)).CurrentBalance);
        var pnlAfter = await Finance(context).GetProfitAndLossAsync(null, today.AddDays(-1), today.AddDays(1));
        Assert.Equal(50_000m, pnlAfter.NetProfit);
        var sheetAfter = await Finance(context).GetBalanceSheetAsync(null, today);
        Assert.True(sheetAfter.IsBalanced);
        Assert.Equal(50_000m, sheetAfter.TotalAssets);
        Assert.Equal(0m, sheetAfter.TotalLiabilities);
        Assert.Equal(50_000m, sheetAfter.RetainedProfit);
    }

    [Fact]
    public async Task CancellationSettlement_NoRefund_TurnsTheWholeDepositIntoIncome()
    {
        await using var context = Context();
        var (bookingId, _) = await SeedCancellableBooking(context, paid: 500_000m);
        var booking = new BookingService(context, new CustomerService(context), new FinanceAccountService(context));
        await booking.CancelBookingAsync(bookingId, new CancelBookingDto
        {
            Reason = "Full forfeiture", ExpectedCustomerCashReceived = 500_000m,
            RefundAmount = 0m, RefundDecision = CancellationRefundDecision.None, IdempotencyKey = "nf-1"
        }, new DAMS.Application.Common.FinancialWorkflowActor(1, "Admin"));

        // Pakistan time, not raw UTC: the cancellation itself is dated by PakistanTime.Today (see
        // BookingService.Cancellation.cs), and UTC lags PKT by up to ~5 hours — using UtcNow.Date
        // here would flake for report queries run between 00:00 and 04:59 PKT.
        var today = DAMS.Application.Common.PakistanTime.Today;
        var pnl = await Finance(context).GetProfitAndLossAsync(null, today.AddDays(-1), today.AddDays(1));
        Assert.DoesNotContain(pnl.IncomeLines, l => l.Name == "Customer Refunds");
        // The whole 500,000 is retained, so the whole 500,000 is earned — once, at cancellation.
        Assert.Equal(500_000m, Assert.Single(pnl.IncomeLines, l => l.Name == "Cancellation Income (Retained)").Amount);
        Assert.Equal(500_000m, pnl.TotalIncome);
        Assert.Equal(500_000m, pnl.NetProfit);
    }

    /// <summary>
    /// DAMS' business day is Pakistan time (UTC+5). A cancellation made at 00:30 PKT on 1 September
    /// is still 19:30 UTC on 31 August. If reports dated the refund by the raw UTC instant
    /// (CancelledAt) instead of the Pakistan business date (CancellationDate), it would land in
    /// August's financial period instead of September's — moving income/liability across a period
    /// boundary, which is much more serious at a month/year end than on an ordinary day.
    /// </summary>
    [Fact]
    public async Task CancellationSettlement_UsesPakistanBusinessDate_NotRawUtcInstant_ForFinancialPeriod()
    {
        await using var context = Context();
        var bank = new FinanceAccount { Name = "Bank", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank, IsActive = true };
        var payable = new FinanceAccount
        {
            Name = "Customer Refunds Payable", AccountHolderName = "DAMS", Type = FinanceAccountType.Liability,
            IsActive = true, SystemRole = FinanceSystemAccountRole.CustomerRefundPayable, DisplayOrder = 515
        };
        var (bookingId, _) = await SeedCancellableBooking(context, paid: 500_000m);
        context.AddRange(bank, payable);
        await context.SaveChangesAsync();

        // 1 September 00:30 Pakistan time == 31 August 19:30 UTC.
        var cancelledAtUtc = new DateTime(2026, 8, 31, 19, 30, 0, DateTimeKind.Utc);
        var cancellationDatePkt = new DateTime(2026, 9, 1);
        context.BookingCancellationSettlements.Add(new BookingCancellationSettlement
        {
            BookingId = bookingId, CustomerCashReceivedSnapshot = 500_000m, RefundAmount = 100_000m,
            RetainedAmount = 400_000m, RefundDecision = CancellationRefundDecision.PayLater,
            RefundPayableAccountId = payable.Id, Reason = "Midnight-boundary regression test",
            IdempotencyKey = "midnight-1", CancelledByUserId = 1, CancelledByName = "Admin",
            CancelledAt = cancelledAtUtc, CancellationDate = cancellationDatePkt
        });
        await context.SaveChangesAsync();

        var finance = Finance(context);
        var august = await finance.GetProfitAndLossAsync(null, new DateTime(2026, 8, 1), new DateTime(2026, 8, 31));
        Assert.Empty(august.IncomeLines);

        var september = await finance.GetProfitAndLossAsync(null, new DateTime(2026, 9, 1), new DateTime(2026, 9, 30));
        var retainedLine = Assert.Single(september.IncomeLines, l => l.Name == "Cancellation Income (Retained)");
        Assert.Equal(400_000m, retainedLine.Amount);
    }

    private static async Task<(int BookingId, FinanceAccount Bank)> SeedCancellableBooking(AppDbContext context, decimal paid)
    {
        var bank = new FinanceAccount { Name = "Bank", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank, IsActive = true };
        var project = new Project { ProjectName = "Cancellation Finance Test", Location = "Karachi", CreatedById = 1 };
        var unit = new Unit { Project = project, UnitNumber = $"U-{Guid.NewGuid():N}"[..8], UnitType = "Apartment", Price = 1_000_000m, Status = UnitStatus.OnPaymentPlan };
        var customer = new Customer { FullName = "Buyer", Phone = "03001112222", Status = CustomerStatus.Active };
        var bookingEntity = new Booking
        {
            BookingReference = $"BK-{Guid.NewGuid():N}", Customer = customer, Unit = unit,
            Status = BookingStatus.PaymentPlanActive, Source = CustomerSource.Referral,
            AgreedSalePrice = 1_000_000m, DiscountAmount = 0m, BookingAmountRequired = 500_000m,
            BookingAmountReceived = paid, BookingDate = DateTime.UtcNow
        };
        bookingEntity.Payments.Add(new Payment
        {
            Booking = bookingEntity, Amount = paid, Type = PaymentType.BookingAmount,
            PaymentMethod = PaymentMethod.Cash, FinanceAccountId = null, PaidAt = DateTime.UtcNow
        });
        context.AddRange(bank, project, unit, customer, bookingEntity);
        await context.SaveChangesAsync();
        // The payment must be attributed to the bank account for its cash balance to move.
        bookingEntity.Payments.Single().FinanceAccountId = bank.Id;
        await context.SaveChangesAsync();
        return (bookingEntity.Id, bank);
    }

    private static FinanceService Finance(AppDbContext context)
    {
        var accounts = new FinanceAccountService(context);
        return new FinanceService(context, new NoopStorage(), accounts, new WhtService(context, accounts), NullLogger<FinanceService>.Instance);
    }

    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class NoopStorage : IFinanceAttachmentStorage
    {
        public Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default) => Task.FromResult("unused");
        public Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(null);
        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
