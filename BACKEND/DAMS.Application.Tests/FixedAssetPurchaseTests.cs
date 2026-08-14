using DAMS.Application.DTOs.ExpenseDtos;
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

/// <summary>
/// Recording the purchase of something the company keeps.
/// <para>
/// The invariant every test here defends: buying an asset moves value between two balance-sheet
/// accounts and changes profit by nothing at all. If any of these start asserting a movement in the
/// P&amp;L, the feature has silently become an expense again.
/// </para>
/// </summary>
public sealed class FixedAssetPurchaseTests
{
    [Fact]
    public async Task BuyingFurniture_MovesCashIntoTheAssetAccount_AndLeavesProfitUntouched()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var service = Finance(context);
        var accounts = new FinanceAccountService(context);

        var profitBefore = await service.GetProfitAndLossAsync(null, Year.Start, Year.End);
        var sheetBefore = await service.GetBalanceSheetAsync(null, AsAt);

        await service.CreateAssetPurchaseAsync(new CreateAssetPurchaseDto
        {
            AssetAccountId = world.Furniture.Id,
            FinanceAccountId = world.Hbl.Id,
            Amount = 200_000m,
            ItemName = "3 office desks",
            CategoryId = world.NoTaxHead.Id,
            Date = new DateTime(2026, 8, 10)
        }, adminUserId: 1);

        // The bank falls by exactly what was paid; the asset account rises by exactly the same.
        Assert.Equal(800_000m, (await accounts.GetByIdAsync(world.Hbl.Id)).CurrentBalance);
        Assert.Equal(200_000m, (await accounts.GetByIdAsync(world.Furniture.Id)).CurrentBalance);

        // The whole point: nothing was spent, so nothing about profit moves.
        var profitAfter = await service.GetProfitAndLossAsync(null, Year.Start, Year.End);
        Assert.Equal(profitBefore.TotalExpenses, profitAfter.TotalExpenses);
        Assert.Equal(profitBefore.NetProfit, profitAfter.NetProfit);

        // And the sheet still balances at the same total — rearranged, not larger or smaller.
        var sheetAfter = await service.GetBalanceSheetAsync(null, AsAt);
        Assert.True(sheetAfter.IsBalanced);
        Assert.Equal(sheetBefore.TotalAssets, sheetAfter.TotalAssets);
        Assert.Equal(200_000m, Group(sheetAfter, "Fixed Assets").Total);

        // The dashboard reports it beside the expense total, never inside it.
        var summary = await service.GetSummaryAsync(null, null, null);
        Assert.Equal(200_000m, summary.TotalAssetPurchases);
        Assert.Equal(0m, summary.TotalExpenses);
    }

    [Fact]
    public async Task WithholdingTax_CapitalisesGross_PaysNet_AndOwesTheDifferenceToFbr()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var service = Finance(context);
        var accounts = new FinanceAccountService(context);

        var purchase = await service.CreateAssetPurchaseAsync(new CreateAssetPurchaseDto
        {
            AssetAccountId = world.Equipment.Id,
            FinanceAccountId = world.Hbl.Id,
            Amount = 100_000m,
            ItemName = "Dell Latitude laptop",
            CategoryId = world.GoodsHead.Id,
            VendorId = world.Supplier.Id,
            Date = new DateTime(2026, 8, 10)
        }, adminUserId: 1);

        // 100,000 of goods from a non-filer at 10%, past the 75,000 annual allowance.
        Assert.True(purchase.WhtApplied);
        Assert.Equal(10_000m, purchase.WhtAmount);
        Assert.Equal(90_000m, purchase.NetPaid);
        Assert.Equal("153(1)(a)", purchase.WhtTaxSection);

        // The asset is carried at the full price — the withheld tax is a debt, not a discount.
        Assert.Equal(100_000m, (await accounts.GetByIdAsync(world.Equipment.Id)).CurrentBalance);
        // Only the net actually left the bank.
        Assert.Equal(910_000m, (await accounts.GetByIdAsync(world.Hbl.Id)).CurrentBalance);
        // And the shortfall sits as a liability to FBR.
        Assert.Equal(10_000m, (await accounts.GetByIdAsync(world.TaxPayable.Id)).CurrentBalance);

        var sheet = await service.GetBalanceSheetAsync(null, AsAt);
        Assert.True(sheet.IsBalanced);
        Assert.Equal(0m, (await service.GetProfitAndLossAsync(null, Year.Start, Year.End)).TotalExpenses);

        // The FBR payable report counts tax withheld from capital suppliers too.
        var payable = await Wht(context).GetPayableSummaryAsync(null, null);
        Assert.Equal(10_000m, payable.TotalWithheldAllTime);
        Assert.Equal(10_000m, payable.OutstandingPayable);
        Assert.Equal("153(1)(a)", Assert.Single(payable.BySection).TaxSection);
    }

    [Fact]
    public async Task TheAnnualAllowance_IsSharedWithExpenses_SoASupplierCannotBeSplitAcrossBoth()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var service = Finance(context);

        // 60,000 of cement as an ordinary expense: below the 75,000 allowance, nothing withheld.
        var expense = await service.CreateExpenseAsync(new CreateExpenseDto
        {
            FinanceAccountId = world.Hbl.Id, Amount = 60_000m,
            CategoryId = world.GoodsHead.Id, VendorId = world.Supplier.Id,
            Date = new DateTime(2026, 8, 1)
        }, adminUserId: 1);
        Assert.Equal(0m, expense.WhtAmount);

        // 60,000 of furniture from the SAME supplier under the SAME section. Judged alone it also
        // looks exempt — but the year's total is 120,000, well past the allowance, so tax is due.
        // Without the shared aggregate this supplier could be paid indefinitely in exempt-looking
        // slices simply by alternating which ledger the purchase lands in.
        var purchase = await service.CreateAssetPurchaseAsync(new CreateAssetPurchaseDto
        {
            AssetAccountId = world.Furniture.Id, FinanceAccountId = world.Hbl.Id,
            Amount = 60_000m, ItemName = "Reception counter",
            CategoryId = world.GoodsHead.Id, VendorId = world.Supplier.Id,
            Date = new DateTime(2026, 8, 20)
        }, adminUserId: 1);
        Assert.True(purchase.WhtApplied);
        Assert.Equal(6_000m, purchase.WhtAmount);

        // It works in the other direction too: the expense form now sees the purchase.
        var later = await service.CreateExpenseAsync(new CreateExpenseDto
        {
            FinanceAccountId = world.Hbl.Id, Amount = 10_000m,
            CategoryId = world.GoodsHead.Id, VendorId = world.Supplier.Id,
            Date = new DateTime(2026, 9, 1)
        }, adminUserId: 1);
        Assert.Equal(1_000m, later.WhtAmount);
    }

    [Fact]
    public async Task CorrectingAPurchase_MovesBothBalances_AndDeletingItPutsThemBack()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var service = Finance(context);
        var accounts = new FinanceAccountService(context);

        var created = await service.CreateAssetPurchaseAsync(new CreateAssetPurchaseDto
        {
            AssetAccountId = world.Furniture.Id, FinanceAccountId = world.Hbl.Id,
            Amount = 200_000m, ItemName = "3 office desks",
            CategoryId = world.NoTaxHead.Id, Date = new DateTime(2026, 8, 10)
        }, adminUserId: 1);

        // Typed 200,000, actually 150,000, and it belongs to Office Equipment, paid from cash.
        await service.UpdateAssetPurchaseAsync(created.Id, new UpdateAssetPurchaseDto
        {
            AssetAccountId = world.Equipment.Id, FinanceAccountId = world.Cash.Id,
            Amount = 150_000m, ItemName = "3 office desks",
            CategoryId = world.NoTaxHead.Id, Date = new DateTime(2026, 8, 10)
        });

        Assert.Equal(1_000_000m, (await accounts.GetByIdAsync(world.Hbl.Id)).CurrentBalance);
        Assert.Equal(0m, (await accounts.GetByIdAsync(world.Furniture.Id)).CurrentBalance);
        Assert.Equal(-150_000m, (await accounts.GetByIdAsync(world.Cash.Id)).CurrentBalance);
        Assert.Equal(150_000m, (await accounts.GetByIdAsync(world.Equipment.Id)).CurrentBalance);
        Assert.True((await service.GetBalanceSheetAsync(null, AsAt)).IsBalanced);

        await service.DeleteAssetPurchaseAsync(created.Id);

        Assert.Equal(0m, (await accounts.GetByIdAsync(world.Equipment.Id)).CurrentBalance);
        Assert.Equal(0m, (await accounts.GetByIdAsync(world.Cash.Id)).CurrentBalance);
        Assert.Equal(1_000_000m, (await accounts.GetByIdAsync(world.Hbl.Id)).CurrentBalance);
        Assert.True((await service.GetBalanceSheetAsync(null, AsAt)).IsBalanced);
    }

    [Fact]
    public async Task ThePurchaseAppearsOnBothAccounts_WithOppositeSigns()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var service = Finance(context);
        var accounts = new FinanceAccountService(context);

        await service.CreateAssetPurchaseAsync(new CreateAssetPurchaseDto
        {
            AssetAccountId = world.Equipment.Id, FinanceAccountId = world.Hbl.Id,
            Amount = 100_000m, ItemName = "Dell Latitude laptop",
            CategoryId = world.GoodsHead.Id, VendorId = world.Supplier.Id,
            Date = new DateTime(2026, 8, 10)
        }, adminUserId: 1);

        // The asset ledger reads as a list of what was bought, at cost, with a running total.
        var assetRow = Assert.Single((await accounts.GetTransactionsAsync(world.Equipment.Id, 0, 50)).Items);
        Assert.Equal("Asset acquired", assetRow.Kind);
        Assert.Equal("Dell Latitude laptop", assetRow.Label);
        Assert.Equal(100_000m, assetRow.Amount);

        // The bank statement reads as cash out, at what actually left.
        var bankRow = Assert.Single((await accounts.GetTransactionsAsync(world.Hbl.Id, 0, 50)).Items);
        Assert.Equal("Asset purchase", bankRow.Kind);
        Assert.Equal(-90_000m, bankRow.Amount);
        Assert.Equal(100_000m, bankRow.GrossAmount);
        Assert.Equal(10_000m, bankRow.WhtAmount);

        // And the payable ledger shows where the withheld tax went.
        var payableRow = Assert.Single((await accounts.GetTransactionsAsync(world.TaxPayable.Id, 0, 50)).Items);
        Assert.Equal("WHT withheld", payableRow.Kind);
        Assert.Equal(10_000m, payableRow.Amount);
    }

    [Fact]
    public async Task TheDestinationMustBeAFixedAssetAccount_AndTheSourceMustBeCash()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var service = Finance(context);

        CreateAssetPurchaseDto Purchase(int assetAccountId, int paidFromId) => new()
        {
            AssetAccountId = assetAccountId, FinanceAccountId = paidFromId,
            Amount = 1_000m, ItemName = "Desk", CategoryId = world.NoTaxHead.Id
        };

        // Capitalising into a bank account would count the money twice.
        var wrongDestination = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAssetPurchaseAsync(Purchase(world.Hbl.Id, world.Hbl.Id), 1));
        Assert.Contains("fixed-asset account", wrongDestination.Message);

        // Paying from an asset account would credit something that holds no cash.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAssetPurchaseAsync(Purchase(world.Furniture.Id, world.Equipment.Id), 1));

        // A head with no rate behind it is not a legal classification for a new purchase.
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAssetPurchaseAsync(new CreateAssetPurchaseDto
        {
            AssetAccountId = world.Furniture.Id, FinanceAccountId = world.Hbl.Id,
            Amount = 1_000m, ItemName = "Desk", CategoryId = null
        }, 1));

        // And it has to say what was actually bought.
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAssetPurchaseAsync(new CreateAssetPurchaseDto
        {
            AssetAccountId = world.Furniture.Id, FinanceAccountId = world.Hbl.Id,
            Amount = 1_000m, ItemName = "  ", CategoryId = world.NoTaxHead.Id
        }, 1));
    }

    [Fact]
    public async Task AnAccountHoldingPurchases_CannotBeDeleted_FromEitherSide()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var accounts = new FinanceAccountService(context);

        await Finance(context).CreateAssetPurchaseAsync(new CreateAssetPurchaseDto
        {
            AssetAccountId = world.Furniture.Id, FinanceAccountId = world.Hbl.Id,
            Amount = 200_000m, ItemName = "3 office desks", CategoryId = world.NoTaxHead.Id
        }, adminUserId: 1);

        // Both ends are load-bearing: dropping either would leave a one-sided entry.
        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.DeleteUnusedAsync(world.Furniture.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.DeleteUnusedAsync(world.Hbl.Id));
    }

    [Fact]
    public async Task TheTrialBalanceStillTiesOut_WithPurchasesAndWithholdingInPlay()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var service = Finance(context);

        await service.CreateAssetPurchaseAsync(new CreateAssetPurchaseDto
        {
            AssetAccountId = world.Equipment.Id, FinanceAccountId = world.Hbl.Id,
            Amount = 100_000m, ItemName = "Dell Latitude laptop",
            CategoryId = world.GoodsHead.Id, VendorId = world.Supplier.Id,
            Date = new DateTime(2026, 8, 10)
        }, adminUserId: 1);

        var trial = await service.GetTrialBalanceAsync(null, AsAt, 0);
        Assert.True(Assert.Single(trial.ColumnBalanced));
        Assert.Equal(Assert.Single(trial.ColumnDebitTotals), Assert.Single(trial.ColumnCreditTotals));
    }

    // ── Fixtures ────────────────────────────────────────────────────────────────

    private static readonly DateTime AsAt = new(2026, 9, 30);
    private static readonly (DateTime Start, DateTime End) Year = (new DateTime(2026, 7, 1), new DateTime(2027, 6, 30));

    private sealed record World(
        FinanceAccount Hbl, FinanceAccount Cash, FinanceAccount Furniture, FinanceAccount Equipment,
        FinanceAccount TaxPayable, ExpenseCategory GoodsHead, ExpenseCategory NoTaxHead, Vendor Supplier);

    private static async Task<World> SeedAsync(AppDbContext context)
    {
        var hbl = new FinanceAccount { Name = "HBL Bank", AccountHolderName = "Seven Ventures", Type = FinanceAccountType.Bank, OpeningBalance = 1_000_000m, IsActive = true };
        var cash = new FinanceAccount { Name = "Cash Account", AccountHolderName = "Seven Ventures", Type = FinanceAccountType.Cash, IsActive = true };
        var furniture = new FinanceAccount { Name = "Office Furniture & Fixture", LedgerCode = "23", AccountHolderName = "Seven Ventures", Type = FinanceAccountType.FixedAsset, IsActive = true };
        var equipment = new FinanceAccount { Name = "Office Equipment", LedgerCode = "7", AccountHolderName = "Seven Ventures", Type = FinanceAccountType.FixedAsset, IsActive = true };
        var taxPayable = new FinanceAccount
        {
            Name = "Tax Payable", LedgerCode = "11", AccountHolderName = "Seven Ventures",
            Type = FinanceAccountType.Liability, SystemRole = FinanceSystemAccountRole.TaxPayable, IsActive = true
        };
        // Capital, so the opening bank balance has a credit side and the sheet starts balanced.
        var capital = new FinanceAccount { Name = "Owner Capital", AccountHolderName = "Seven Ventures", Type = FinanceAccountType.Capital, OpeningBalance = 1_000_000m, IsActive = true };

        var goods = new ExpenseCategory
        {
            Name = "Hardware & Tools", Code = "hardware_tools", TaxSection = "153(1)(a)",
            IsWhtApplicable = true, FilerRate = 5m, NonFilerRate = 10m, AnnualThreshold = 75_000m, DisplayOrder = 10
        };
        var noTax = new ExpenseCategory
        {
            Name = "Miscellaneous", Code = "miscellaneous", IsWhtApplicable = false, DisplayOrder = 20
        };
        var supplier = new Vendor { Name = "Al-Karam Traders", FilerStatus = FilerStatus.NonFiler, IsActive = true };

        context.AddRange(hbl, cash, furniture, equipment, taxPayable, capital, goods, noTax, supplier);
        await context.SaveChangesAsync();
        return new World(hbl, cash, furniture, equipment, taxPayable, goods, noTax, supplier);
    }

    private static BsGroupDto Group(BalanceSheetDto sheet, string name) =>
        sheet.AssetGroups.Single(g => g.Name == name);

    private static FinanceService Finance(AppDbContext context)
    {
        var accounts = new FinanceAccountService(context);
        return new FinanceService(context, new NoopStorage(), accounts, new WhtService(context, accounts), NullLogger<FinanceService>.Instance);
    }

    private static WhtService Wht(AppDbContext context) => new(context, new FinanceAccountService(context));

    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class NoopStorage : IFinanceAttachmentStorage
    {
        public Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default) => Task.FromResult("unused");
        public Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(null);
        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
