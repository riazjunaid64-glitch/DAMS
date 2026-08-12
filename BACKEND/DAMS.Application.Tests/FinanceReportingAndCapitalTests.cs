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
    public async Task RevenueCategories_AreAlwaysSoftRetired()
    {
        await using var context = Context();
        var category = new RevenueCategory { Name = "Temporary", Code = "temporary", IsActive = true };
        context.RevenueCategories.Add(category);
        await context.SaveChangesAsync();

        var retired = await new RevenueCategoryService(context).DeleteAsync(category.Id);

        Assert.NotNull(retired);
        Assert.False(retired.IsActive);
        Assert.True(await context.RevenueCategories.AnyAsync(c => c.Id == category.Id));
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
