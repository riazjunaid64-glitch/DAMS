using System.Text.Json;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

public sealed class FinanceAccountsPageTests
{
    [Fact]
    public async Task CombinedPage_MatchesExistingPageAndGlobalOverview_WithMoreThanTwoHundredAccounts()
    {
        await using var context = await SeedAsync();
        var service = new FinanceAccountService(context);
        var expectedPage = await service.GetPageAsync(null, null, null, null, 0, 200);
        var expectedOverview = await service.GetOverviewAsync();

        var result = await service.GetPageWithOverviewAsync(null, null, null, null, 0, 200);

        Assert.Equal(JsonSerializer.Serialize(expectedPage.Items), JsonSerializer.Serialize(result.Items));
        Assert.Equal(expectedPage.HasMore, result.HasMore);
        Assert.Equal(JsonSerializer.Serialize(expectedOverview), JsonSerializer.Serialize(result.Overview));
        Assert.Equal(200, result.Items.Count);
        Assert.True(result.HasMore);
        Assert.Equal(210, result.Overview.ActiveAccounts + result.Overview.InactiveAccounts);
        Assert.Equal(30, result.Overview.InactiveAccounts);
        // All 206 cash-like accounts contribute, including the inactive bank outside this page.
        // Fixed assets, liabilities, capital and staff floats retain their existing exclusion.
        Assert.Equal(Enumerable.Range(1, 205).Sum() + 1000m + 206 * 79m, result.Overview.TotalBalance);
        Assert.Equal(2, result.Overview.HolderBalances.Count);
        Assert.Contains(result.Overview.HolderBalances, h => h.AccountCount == 205);
        Assert.Contains(result.Overview.HolderBalances, h => h.AccountHolderName == "Outside page holder"
            && h.CurrentBalance == 1079m);
        Assert.DoesNotContain(result.Items, a => a.Id == 206);
    }

    [Theory]
    [InlineData("  Account 00  ", null, null, null, 0, 3)]
    [InlineData("Wallet marker", null, null, null, 0, 3)]
    [InlineData("Description marker", null, null, null, 0, 3)]
    [InlineData("Outside page holder", null, null, null, 0, 3)]
    [InlineData(null, FinanceAccountType.Cash, "  Admin  ", true, 2, 5)]
    [InlineData(null, null, null, false, 0, 5)]
    [InlineData(null, FinanceAccountType.FixedAsset, null, null, 0, 5)]
    [InlineData("missing", null, null, null, 0, 5)]
    [InlineData(null, null, null, null, 500, 5)]
    public async Task CombinedPage_PreservesFiltersAndPaging_WithoutFilteringItsOverview(
        string? search, FinanceAccountType? type, string? holder, bool? isActive, int skip, int take)
    {
        await using var context = await SeedAsync();
        var service = new FinanceAccountService(context);
        var expectedPage = await service.GetPageAsync(search, type, holder, isActive, skip, take);
        var expectedOverview = await service.GetOverviewAsync();

        var result = await service.GetPageWithOverviewAsync(search, type, holder, isActive, skip, take);

        Assert.Equal(JsonSerializer.Serialize(expectedPage.Items), JsonSerializer.Serialize(result.Items));
        Assert.Equal(expectedPage.HasMore, result.HasMore);
        Assert.Equal(JsonSerializer.Serialize(expectedOverview), JsonSerializer.Serialize(result.Overview));
    }

    [Fact]
    public async Task CombinedPage_EmptyChart_ReturnsTheExistingEmptyOverview()
    {
        await using var context = Context();
        var service = new FinanceAccountService(context);

        var result = await service.GetPageWithOverviewAsync(null, null, null, null, 0, 200);

        Assert.Empty(result.Items);
        Assert.False(result.HasMore);
        Assert.Equal(JsonSerializer.Serialize(await service.GetOverviewAsync()), JsonSerializer.Serialize(result.Overview));
    }

    private static async Task<AppDbContext> SeedAsync()
    {
        var context = Context();
        for (var id = 1; id <= 205; id++)
            context.FinanceAccounts.Add(new FinanceAccount
            {
                Id = id, Name = $"Account {id:000}", Type = FinanceAccountType.Cash,
                AccountHolderName = id % 2 == 0 ? "Admin" : "ADMIN", OpeningBalance = id,
                IsActive = id % 7 != 0,
                BankOrWalletName = id == 1 ? "Wallet marker" : null,
                Description = id == 2 ? "Description marker" : null
            });
        context.FinanceAccounts.AddRange(
            Account(206, FinanceAccountType.Bank, 1000m, "Outside page holder", false),
            Account(207, FinanceAccountType.FixedAsset, 50000m, "Asset holder"),
            Account(208, FinanceAccountType.Liability, 12000m, "Lender"),
            Account(209, FinanceAccountType.Capital, 25000m, "Partner"),
            Account(210, FinanceAccountType.StaffFloat, 700m, "Staff"));
        for (var id = 1; id <= 206; id++)
        {
            context.ManualRevenues.Add(new ManualRevenue
            {
                FinanceAccountId = id, Amount = 100m + id, RevenueType = "Income"
            });
            context.Expenses.Add(new Expense
            {
                FinanceAccountId = id, Amount = 20m + id, WhtAmount = 2m, Category = "Office"
            });
            context.WhtDeposits.Add(new WhtDeposit { FinanceAccountId = id, Amount = 3m });
        }
        await context.SaveChangesAsync();
        return context;
    }

    private static FinanceAccount Account(int id, FinanceAccountType type, decimal opening, string holder, bool active = true) =>
        new() { Id = id, Name = $"Account {id:000}", Type = type, AccountHolderName = holder, OpeningBalance = opening, IsActive = active };

    private static AppDbContext Context() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
