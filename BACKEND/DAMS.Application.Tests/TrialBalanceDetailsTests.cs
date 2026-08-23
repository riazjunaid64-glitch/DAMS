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

public sealed class TrialBalanceDetailsTests
{
    [Fact]
    public async Task ExactDay_ProjectFilter_AndSameDayOrder_ReconcilePhysicalAndVirtualRows()
    {
        await using var context = Context();
        var day = new DateTime(2026, 8, 23);
        var projectA = new Project { ProjectName = "Alpha", Location = "Islamabad", CreatedById = 1 };
        var projectB = new Project { ProjectName = "Beta", Location = "Lahore", CreatedById = 1 };
        var bank = new FinanceAccount
        {
            Name = "HBL Bank", LedgerCode = "BANK-1", AccountHolderName = "DAMS",
            Type = FinanceAccountType.Bank, IsActive = true
        };
        var income = new RevenueCategory { Name = "Consulting", Code = "consulting", DisplayOrder = 1 };
        var cost = new ExpenseCategory { Name = "Office Cost", Code = "office-cost", DisplayOrder = 1 };
        context.AddRange(projectA, projectB, bank, income, cost);
        await context.SaveChangesAsync();

        context.ManualRevenues.AddRange(
            new ManualRevenue
            {
                ProjectId = projectA.Id, FinanceAccountId = bank.Id, RevenueCategoryId = income.Id,
                RevenueType = income.Name, RevenueTypeName = income.Name, Amount = 500m,
                Date = day, CreatedAt = day.AddHours(8), Reference = "A-1", Description = "First receipt"
            },
            new ManualRevenue
            {
                ProjectId = projectB.Id, FinanceAccountId = bank.Id, RevenueCategoryId = income.Id,
                RevenueType = income.Name, RevenueTypeName = income.Name, Amount = 900m,
                Date = day, CreatedAt = day.AddHours(8).AddMinutes(30), Reference = "B-1"
            },
            new ManualRevenue
            {
                ProjectId = projectA.Id, FinanceAccountId = bank.Id, RevenueCategoryId = income.Id,
                RevenueType = income.Name, RevenueTypeName = income.Name, Amount = 200m,
                Date = day, CreatedAt = day.AddHours(10), Reference = "A-2", Description = "Second receipt"
            });
        context.Expenses.Add(new Expense
        {
            ProjectId = projectA.Id, FinanceAccountId = bank.Id, CategoryId = cost.Id,
            Category = cost.Name, Amount = 100m, Date = day, CreatedAt = day.AddHours(9),
            Vendor = "Vendor-A", Description = "Same-day expense"
        });
        await context.SaveChangesAsync();

        var finance = Finance(context);
        var trial = await finance.GetTrialBalanceAsync(projectA.Id, day, 0);

        Assert.Equal(day, trial.AsAt);
        Assert.Single(trial.ColumnDates);
        Assert.Equal(day, trial.ColumnDates[0]);
        Assert.True(trial.IsBalanced);
        Assert.Equal(700m, trial.TotalDebit);
        Assert.Equal(700m, trial.TotalCredit);

        var bankRow = trial.Rows.Single(row => row.AccountKey == $"A:{bank.Id}");
        Assert.Equal(600m, bankRow.Debit);
        Assert.Equal(0m, bankRow.Credit);
        var bankDetails = await finance.GetTrialBalanceDetailsAsync(
            bankRow.AccountKey, projectA.Id, day, day);
        Assert.Equal(0m, bankDetails.OpeningBalance);
        Assert.Equal(600m, bankDetails.ClosingBalance);
        Assert.Equal("Debit", bankDetails.ClosingBalanceType);
        Assert.Equal(new[] { "A-1", "Vendor-A", "A-2" },
            bankDetails.Rows.Select(row => row.Reference).ToArray());
        Assert.Equal(new[] { 500m, 400m, 600m },
            bankDetails.Rows.Select(row => row.RunningBalance).ToArray());
        Assert.DoesNotContain(bankDetails.Rows, row => row.Reference == "B-1");

        var incomeRow = trial.Rows.Single(row => row.AccountName == income.Name);
        Assert.StartsWith("I:", incomeRow.AccountKey);
        var incomeDetails = await finance.GetTrialBalanceDetailsAsync(
            incomeRow.AccountKey, projectA.Id, day, day);
        Assert.Equal(2, incomeDetails.Rows.Count);
        Assert.Equal(700m, incomeDetails.TotalCredit);
        Assert.Equal(700m, incomeDetails.ClosingBalance);
        Assert.Equal("Credit", incomeDetails.ClosingBalanceType);

        var expenseRow = trial.Rows.Single(row => row.AccountName == cost.Name);
        Assert.StartsWith("E:", expenseRow.AccountKey);
        var expenseDetails = await finance.GetTrialBalanceDetailsAsync(
            expenseRow.AccountKey, projectA.Id, day, day);
        Assert.Equal(100m, Assert.Single(expenseDetails.Rows).Debit);
        Assert.Equal(100m, expenseDetails.ClosingBalance);
        Assert.Equal("Debit", expenseDetails.ClosingBalanceType);
    }

    [Fact]
    public async Task MidMonthCutover_IsOpeningAtBaseline_AndSyntheticMovementWhenRangeStartsEarlier()
    {
        await using var context = Context();
        var baseline = new DateTime(2026, 8, 15);
        var bank = new FinanceAccount
        {
            Name = "Opening Bank", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank,
            OpeningBalance = 1_000m, IsActive = true
        };
        var capital = new FinanceAccount
        {
            Name = "Opening Capital", AccountHolderName = "DAMS", Type = FinanceAccountType.Capital,
            OpeningBalance = 1_000m, IsActive = true
        };
        var project = new Project { ProjectName = "Cutover Project", Location = "Karachi", CreatedById = 1 };
        var revenueCategory = new RevenueCategory
        {
            Name = "Cutover Income", Code = "cutover-income", DisplayOrder = 1
        };
        context.AddRange(bank, capital, project, revenueCategory, new OpeningBalanceSet
        {
            AsAtDate = baseline, IsCommitted = true, CommittedAt = baseline.ToUniversalTime()
        });
        await context.SaveChangesAsync();
        context.ManualRevenues.Add(new ManualRevenue
        {
            ProjectId = project.Id,
            FinanceAccountId = bank.Id,
            RevenueCategoryId = revenueCategory.Id,
            RevenueType = revenueCategory.Name,
            RevenueTypeName = revenueCategory.Name,
            Amount = 100m,
            Date = baseline,
            // An early UTC audit instant must not place this posting before the business-day
            // opening boundary in a chronological ledger.
            CreatedAt = baseline.AddHours(-4),
            Reference = "CUT-1",
            Description = "Baseline-day receipt"
        });
        await context.SaveChangesAsync();

        var finance = Finance(context);
        var accountKey = $"A:{bank.Id}";

        var spanning = await finance.GetTrialBalanceDetailsAsync(
            accountKey, null, new DateTime(2026, 8, 1), new DateTime(2026, 8, 20));
        Assert.Equal(new DateTime(2026, 8, 1), spanning.From);
        Assert.Equal(0m, spanning.OpeningBalance);
        Assert.Equal(2, spanning.Rows.Count);
        var openingRow = spanning.Rows[0];
        Assert.Equal(baseline, openingRow.Date);
        Assert.Contains("Opening balance", openingRow.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1_000m, openingRow.Debit);
        Assert.Equal(1_000m, openingRow.RunningBalance);
        Assert.Equal("CUT-1", spanning.Rows[1].Reference);
        Assert.Equal(1_100m, spanning.Rows[1].RunningBalance);
        Assert.Equal(1_100m, spanning.ClosingBalance);
        Assert.Equal("Debit", spanning.ClosingBalanceType);

        var onBaseline = await finance.GetTrialBalanceDetailsAsync(
            accountKey, null, baseline, new DateTime(2026, 8, 20));
        Assert.Equal(1_000m, onBaseline.OpeningBalance);
        Assert.Equal("Debit", onBaseline.OpeningBalanceType);
        Assert.Equal("CUT-1", Assert.Single(onBaseline.Rows).Reference);
        Assert.Equal(1_100m, onBaseline.ClosingBalance);

        var projectOnly = await finance.GetTrialBalanceDetailsAsync(
            accountKey, project.Id, new DateTime(2026, 8, 1), new DateTime(2026, 8, 20));
        Assert.Equal(0m, projectOnly.OpeningBalance);
        Assert.Equal(100m, projectOnly.ClosingBalance);
        Assert.Equal("CUT-1", Assert.Single(projectOnly.Rows).Reference);
    }

    [Fact]
    public async Task MixedProfitAndLossAllocations_NetOnSummary_ButRemainSeparateOrderedDetailRows()
    {
        await using var context = Context();
        var day = new DateTime(2026, 8, 23);
        var capitalAccount = new FinanceAccount
        {
            Name = "Partner Capital", AccountHolderName = "Partner", Type = FinanceAccountType.Capital,
            IsActive = true
        };
        var partner = new CapitalPartner
        {
            Name = "Partner", ProfitSharePercent = 100m, IsActive = true, FinanceAccount = capitalAccount
        };
        context.Add(partner);
        await context.SaveChangesAsync();
        context.CapitalTransactions.AddRange(
            new CapitalTransaction
            {
                CapitalPartnerId = partner.Id, Type = CapitalTransactionType.ProfitShare,
                Amount = 100m, Date = day, CreatedAt = day.AddHours(8), Reference = "PS-1"
            },
            new CapitalTransaction
            {
                CapitalPartnerId = partner.Id, Type = CapitalTransactionType.LossShare,
                Amount = 40m, Date = day, CreatedAt = day.AddHours(9), Reference = "LS-1"
            });
        await context.SaveChangesAsync();

        var finance = Finance(context);
        var trial = await finance.GetTrialBalanceAsync(null, day, 0);
        var allocation = trial.Rows.Single(row => row.AccountKey == "EQ:allocated");
        Assert.Equal(60m, allocation.Debit);
        Assert.Equal(0m, allocation.Credit);

        var details = await finance.GetTrialBalanceDetailsAsync(
            allocation.AccountKey, null, day, day);
        Assert.Equal(2, details.Rows.Count);
        Assert.Equal(new[] { "PS-1", "LS-1" }, details.Rows.Select(row => row.Reference).ToArray());
        Assert.Equal(100m, details.TotalDebit);
        Assert.Equal(40m, details.TotalCredit);
        Assert.Equal(60m, details.ClosingBalance);
        Assert.Equal("Debit", details.ClosingBalanceType);
    }

    private static FinanceService Finance(AppDbContext context)
    {
        var accounts = new FinanceAccountService(context);
        return new FinanceService(context, new NoopStorage(), accounts, new WhtService(context, accounts),
            NullLogger<FinanceService>.Instance);
    }

    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class NoopStorage : IFinanceAttachmentStorage
    {
        public Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default) =>
            Task.FromResult("unused");
        public Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(null);
        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
