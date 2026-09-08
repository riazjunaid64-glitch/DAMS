using System.Text.Json;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

public sealed class AccountTransactionHistoryPerformanceTests
{
    [Fact]
    public async Task History_PreservesEveryFieldOrderingAndPaging_WhileReportsRetainBalances()
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var day = new DateTime(2026, 8, 20);
        var account = new FinanceAccount
        {
            Name = "Bank", Type = FinanceAccountType.Bank, AccountHolderName = "DAMS",
            OpeningBalance = 1000m, IsActive = false
        };
        context.FinanceAccounts.Add(account);
        await context.SaveChangesAsync();
        context.ManualRevenues.AddRange(
            new ManualRevenue { FinanceAccountId = account.Id, Date = day.AddDays(-1), Amount = 200m, RevenueType = "Prior" },
            new ManualRevenue { FinanceAccountId = account.Id, Date = day, CreatedAt = day, Amount = 300m, RevenueType = "Fees", Reference = "R-1", Description = "Historic income" },
            new ManualRevenue { FinanceAccountId = account.Id, Date = day, CreatedAt = day.AddHours(1), Amount = 400m, RevenueType = "Other" },
            new ManualRevenue { FinanceAccountId = account.Id, Date = day.AddDays(1), Amount = 999m, RevenueType = "After" });
        context.Expenses.Add(new Expense
        {
            FinanceAccountId = account.Id, Date = day, CreatedAt = day, Amount = 100m,
            WhtAmount = 10m, Category = "Office", Vendor = "Supplier", Description = "Historic expense"
        });
        await context.SaveChangesAsync();
        var service = new FinanceAccountService(context);

        var ledger = await service.GetTransactionLedgerSliceAsync(account.Id, null, day, day, 0, int.MaxValue);
        Assert.Equal(1200m, ledger.OpeningNormalBalance);
        Assert.Equal(610m, ledger.PeriodNormalMovement);
        Assert.Equal(new[] { "Revenue", "Expense", "Revenue" }, ledger.Items.Select(row => row.Kind));
        Assert.Equal(-90m, ledger.Items[1].Amount);
        Assert.Equal(100m, ledger.Items[1].GrossAmount);
        Assert.Equal(10m, ledger.Items[1].WhtAmount);

        var first = await service.GetTransactionsAsync(account.Id, null, day, day, 0, 2);
        var second = await service.GetTransactionsAsync(account.Id, null, day, day, 2, 2);
        Assert.True(first.HasMore);
        Assert.False(second.HasMore);
        Assert.Equal(JsonSerializer.Serialize(ledger.Items.AsEnumerable().Reverse()),
            JsonSerializer.Serialize(first.Items.Concat(second.Items)));

        var reportPage = await service.GetTransactionLedgerSliceAsync(account.Id, null, day, day, 1, 1);
        Assert.Equal(300m, reportPage.NormalMovementBeforePage);
        Assert.Equal(1200m, reportPage.OpeningNormalBalance);
        Assert.Equal(610m, reportPage.PeriodNormalMovement);
        Assert.True(reportPage.HasMore);
        Assert.Equal(JsonSerializer.Serialize(ledger.Items[1]), JsonSerializer.Serialize(Assert.Single(reportPage.Items)));
        Assert.Empty((await service.GetTransactionsAsync(account.Id, null, day, day, 5, 2)).Items);
    }

    [Fact]
    public async Task History_RetainsCutoverAndProjectFilters_WithoutAddingTheReportOpeningEvent()
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var day = new DateTime(2026, 8, 1);
        var account = new FinanceAccount { Name = "Bank", Type = FinanceAccountType.Bank, AccountHolderName = "DAMS", OpeningBalance = 500m };
        var project = new Project { ProjectName = "Project A" };
        context.AddRange(account, project);
        context.OpeningBalanceSets.Add(new OpeningBalanceSet { AsAtDate = day, IsCommitted = true, CommittedAt = day });
        await context.SaveChangesAsync();
        context.ManualRevenues.AddRange(
            new ManualRevenue { FinanceAccountId = account.Id, ProjectId = project.Id, Date = day.AddDays(-1), Amount = 999m, RevenueType = "Legacy before cutover" },
            new ManualRevenue { FinanceAccountId = account.Id, ProjectId = project.Id, Date = day, Amount = 50m, RevenueType = "Project income" },
            new ManualRevenue { FinanceAccountId = account.Id, Date = day, Amount = 25m, RevenueType = "General income" });
        await context.SaveChangesAsync();
        var service = new FinanceAccountService(context);

        var history = await service.GetTransactionsAsync(account.Id, null, day.AddDays(-2), day, 0, 20);
        Assert.Equal(2, history.Items.Count);
        Assert.Equal(75m, history.Items.Sum(row => row.Amount));
        var filtered = await service.GetTransactionsAsync(account.Id, project.Id, day.AddDays(-2), day, 0, 20);
        Assert.Equal("Project A", Assert.Single(filtered.Items).ProjectName);
        Assert.Equal(50m, filtered.Items[0].Amount);
        var ledger = await service.GetTransactionLedgerSliceAsync(account.Id, null, day.AddDays(-2), day, 0, int.MaxValue);
        Assert.Equal(0m, ledger.OpeningNormalBalance);
        Assert.Equal(575m, ledger.PeriodNormalMovement);
        Assert.Equal("Opening balance established", ledger.Items[0].Kind);
        Assert.Equal(3, ledger.Items.Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetTransactionsAsync(account.Id, project.Id + 1, day, day, 0, 2));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetTransactionsAsync(account.Id, null, day.AddDays(1), day, 0, 2));
    }
}
