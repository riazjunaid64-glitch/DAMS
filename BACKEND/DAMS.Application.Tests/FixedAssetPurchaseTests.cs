using System.IO.Compression;
using DAMS.Application.Common;
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
/// The books hold one entry per purchase: Dr Fixed Asset, Cr the account that paid. The asset stays on
/// the Balance Sheet at cost and is never written down.
/// </para>
/// <para>
/// The client also wants the purchase to reduce Net Profit at once. That rule is applied on the
/// DASHBOARD, where a figure is presented and no journal entry is implied, and it is deliberately NOT
/// applied to the formal P&amp;L, Trial Balance or Balance Sheet — deducting a cost there needs a
/// matching credit, and no account has been approved to carry it. So the statements stay balanced on
/// real double entry alone and DISCLOSE the amount as an outstanding deduction.
/// </para>
/// <para>
/// Two failure modes are pinned here on purpose. A future change that posts the deduction into the
/// formal statements without an approved credit will fail (it would be a half-entry). So will one that
/// invents a capital reserve, contra-asset or depreciation account to absorb it. Both are the
/// accountant's decision, not this code's.
/// </para>
/// </summary>
public sealed class FixedAssetPurchaseTests
{
    [Fact]
    public async Task BuyingFurniture_MovesCashIntoTheAssetAccount_AndReducesNetProfitOnEveryScreen()
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

        // The statement moves, because there is only one Net Profit rule and this is spending.
        var profitAfter = await service.GetProfitAndLossAsync(null, Year.Start, Year.End);
        Assert.Equal(profitBefore.TotalExpenses + 200_000m, profitAfter.TotalExpenses);
        Assert.Equal(profitBefore.NetProfit - 200_000m, profitAfter.NetProfit);
        Assert.Equal(200_000m, Assert.Single(profitAfter.ExpenseLines, l => l.Name == "Fixed Asset Purchases").Amount);

        // Assets are only rearranged — cash out, desks in — so the sheet still balances on the real
        // double entry, with the desks carried at cost and equity untouched. Nothing invented, and
        // the one figure the P&L took off that the ledger did not is named rather than left to be
        // discovered by subtracting one statement from the other.
        var sheetAfter = await service.GetBalanceSheetAsync(null, AsAt);
        Assert.True(sheetBefore.IsBalanced);
        Assert.True(sheetAfter.IsBalanced);
        Assert.Equal(sheetBefore.TotalAssets, sheetAfter.TotalAssets);
        Assert.Equal(200_000m, Group(sheetAfter, "Fixed Assets").Total);
        Assert.Equal(sheetBefore.TotalCapital, sheetAfter.TotalCapital);
        Assert.Equal(200_000m, sheetAfter.UnpostedFixedAssetCharge);
        Assert.DoesNotContain(sheetAfter.CapitalLines, l => l.AccountId < 0);

        // And the dashboard says exactly the same thing as the statement.
        var summary = await service.GetSummaryAsync(null, null, null);
        Assert.Equal(200_000m, summary.TotalAssetPurchases);
        Assert.Equal(200_000m, summary.TotalExpenses);
        Assert.Equal(-200_000m, summary.NetProfit);
        Assert.Equal(summary.NetProfit, profitAfter.NetProfit);
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

        // The charge to profit is the GROSS price, not the net that left the bank: the withheld tax is
        // owed to FBR, not saved, so netting it off would understate the cost by exactly the tax.
        var pnl = await service.GetProfitAndLossAsync(null, Year.Start, Year.End);
        Assert.Equal(100_000m, Assert.Single(pnl.ExpenseLines, l => l.Name == "Fixed Asset Purchases").Amount);
        Assert.Equal(100_000m, pnl.TotalExpenses);
        Assert.Equal(-100_000m, pnl.NetProfit);
        // And the withholding is fully double-sided on its own, so the sheet still balances.
        Assert.True((await service.GetBalanceSheetAsync(null, AsAt)).IsBalanced);

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
            Date = new DateTime(2026, 8, 5)
        }, adminUserId: 1);
        Assert.True(purchase.WhtApplied);
        Assert.Equal(6_000m, purchase.WhtAmount);

        // It works in the other direction too: the expense form now sees the purchase. Dated inside
        // the same financial year and in the past — a money row cannot carry a future date.
        var later = await service.CreateExpenseAsync(new CreateExpenseDto
        {
            FinanceAccountId = world.Hbl.Id, Amount = 10_000m,
            CategoryId = world.GoodsHead.Id, VendorId = world.Supplier.Id,
            Date = new DateTime(2026, 8, 10)
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
        var updated = await service.UpdateAssetPurchaseAsync(created.Id, new UpdateAssetPurchaseDto
        {
            AssetAccountId = world.Equipment.Id, FinanceAccountId = world.Cash.Id,
            Amount = 150_000m, ItemName = "3 office desks",
            CategoryId = world.NoTaxHead.Id, Date = new DateTime(2026, 8, 10),
            ConcurrencyToken = created.ConcurrencyToken
        });

        Assert.Equal(1_000_000m, (await accounts.GetByIdAsync(world.Hbl.Id)).CurrentBalance);
        Assert.Equal(0m, (await accounts.GetByIdAsync(world.Furniture.Id)).CurrentBalance);
        Assert.Equal(-150_000m, (await accounts.GetByIdAsync(world.Cash.Id)).CurrentBalance);
        Assert.Equal(150_000m, (await accounts.GetByIdAsync(world.Equipment.Id)).CurrentBalance);
        // Still balanced, and the charge to profit follows the correction down to what was actually
        // spent — it is derived from the surviving row, not accumulated as the purchase is edited.
        Assert.True((await service.GetBalanceSheetAsync(null, AsAt)).IsBalanced);
        Assert.Equal(-150_000m, (await service.GetProfitAndLossAsync(null, Year.Start, Year.End)).NetProfit);

        await service.DeleteAssetPurchaseAsync(created.Id, updated.ConcurrencyToken);

        Assert.Equal(0m, (await accounts.GetByIdAsync(world.Equipment.Id)).CurrentBalance);
        Assert.Equal(0m, (await accounts.GetByIdAsync(world.Cash.Id)).CurrentBalance);
        Assert.Equal(1_000_000m, (await accounts.GetByIdAsync(world.Hbl.Id)).CurrentBalance);
        // …and the charge is gone entirely once the purchase is: nothing lingers.
        Assert.True((await service.GetBalanceSheetAsync(null, AsAt)).IsBalanced);
        var afterDelete = await service.GetProfitAndLossAsync(null, Year.Start, Year.End);
        Assert.Equal(0m, afterDelete.NetProfit);
        Assert.DoesNotContain(afterDelete.ExpenseLines, l => l.Name == "Fixed Asset Purchases");
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

    /// <summary>
    /// Construction spend. The client confirmed this is a COST on the day it is paid, not an asset
    /// under construction — so the capitalisation route is closed and the expense route is the only
    /// one. If this test ever passes a purchase into a work-in-progress account again, profit has
    /// silently stopped falling when the company buys cement.
    /// </summary>
    [Fact]
    public async Task NewConstructionSpend_IsRefusedAsACapitalisation_AndReducesProfitAsAnExpense()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var wip = Wip(context, "Floria Building — Work in Progress", "26");
        await context.SaveChangesAsync();
        var service = Finance(context);
        var accounts = new FinanceAccountService(context);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAssetPurchaseAsync(
            new CreateAssetPurchaseDto
            {
                AssetAccountId = wip.Id, FinanceAccountId = world.Hbl.Id, Amount = 300_000m,
                ItemName = "Steel & cement — 3rd floor slab", CategoryId = world.NoTaxHead.Id,
                Date = new DateTime(2026, 8, 12)
            }, adminUserId: 1));
        // The message has to name the route that IS correct, or the operator's only option is to
        // pick a different asset account and mis-file it.
        Assert.Contains("recorded as an expense", refused.Message);
        Assert.Contains("Expenses", refused.Message);

        // Recorded the way the rule now requires: an ordinary expense under its construction head.
        await service.CreateExpenseAsync(new CreateExpenseDto
        {
            FinanceAccountId = world.Hbl.Id, Amount = 300_000m, CategoryId = world.NoTaxHead.Id,
            Description = "Steel & cement — 3rd floor slab", Date = new DateTime(2026, 8, 12)
        }, adminUserId: 1);

        // Profit falls immediately, by the full amount, on the day it was paid.
        var pnl = await service.GetProfitAndLossAsync(null, Year.Start, Year.End);
        Assert.Equal(300_000m, pnl.TotalExpenses);
        Assert.Equal(-300_000m, pnl.NetProfit);
        // Recorded under its own head, not as a fixed-asset purchase — there is no asset to carry.
        Assert.DoesNotContain(pnl.ExpenseLines, l => l.Name == "Fixed Asset Purchases");

        // Cash left the bank, and no accounting WIP asset was created for it.
        Assert.Equal(700_000m, (await accounts.GetByIdAsync(world.Hbl.Id)).CurrentBalance);
        Assert.Equal(0m, (await accounts.GetByIdAsync(wip.Id)).CurrentBalance);

        var sheet = await service.GetBalanceSheetAsync(null, AsAt);
        Assert.True(sheet.IsBalanced);
        // The WIP account is still on the chart (its ERP opening balance is a real asset) but this
        // spend did not accumulate into it — the whole 300,000 went to profit instead.
        Assert.Equal(0m, Group(sheet, "Work in Progress").Total);
    }

    /// <summary>
    /// Withholding on construction spend still works — it just works on the expense flow now. The
    /// gross is the cost, the net leaves the bank, the difference is owed to FBR.
    /// </summary>
    [Fact]
    public async Task ConstructionSpendWithWithholding_ExpensesGross_PaysNet_AndOwesFbrTheRest()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var service = Finance(context);
        var accounts = new FinanceAccountService(context);

        var expense = await service.CreateExpenseAsync(new CreateExpenseDto
        {
            FinanceAccountId = world.Hbl.Id, Amount = 100_000m, CategoryId = world.GoodsHead.Id,
            VendorId = world.Supplier.Id, Description = "Site office structure",
            Date = new DateTime(2026, 8, 12)
        }, adminUserId: 1);

        Assert.Equal(10_000m, expense.WhtAmount);
        Assert.Equal(90_000m, expense.NetPaid);

        // Gross is the cost…
        Assert.Equal(100_000m, (await service.GetProfitAndLossAsync(null, Year.Start, Year.End)).TotalExpenses);
        // …net is what left the bank…
        Assert.Equal(910_000m, (await accounts.GetByIdAsync(world.Hbl.Id)).CurrentBalance);
        // …and the difference is a debt to FBR, not a discount on the cement.
        Assert.Equal(10_000m, (await accounts.GetByIdAsync(world.TaxPayable.Id)).CurrentBalance);
        Assert.True((await service.GetBalanceSheetAsync(null, AsAt)).IsBalanced);
    }

    /// <summary>
    /// A purchase recorded against work in progress BEFORE the rule changed. Correcting its amount
    /// must still work: refusing to save it would leave a wrong figure permanently on the books, and
    /// forcing it onto a fixed-asset account would rewrite what actually happened.
    /// </summary>
    [Fact]
    public async Task AnExistingWorkInProgressPurchase_StaysEditableInPlace_ButCannotBeJoinedByANewOne()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var wip = Wip(context, "Work in Progress — Site Office", "29");
        await context.SaveChangesAsync();

        // Written directly, the way a row saved under the old rule exists today.
        var legacy = new AssetPurchase
        {
            AssetAccountId = wip.Id, FinanceAccountId = world.Hbl.Id, Amount = 300_000m,
            ItemName = "Site office structure", Category = world.NoTaxHead.Name,
            CategoryId = world.NoTaxHead.Id, Date = new DateTime(2026, 8, 12)
        };
        context.AssetPurchases.Add(legacy);
        await context.SaveChangesAsync();

        var service = Finance(context);
        var accounts = new FinanceAccountService(context);
        var token = Convert.ToBase64String(legacy.RowVersion);

        // Typed 300,000, actually 250,000. Same destination — allowed.
        await service.UpdateAssetPurchaseAsync(legacy.Id, new UpdateAssetPurchaseDto
        {
            AssetAccountId = wip.Id, FinanceAccountId = world.Hbl.Id, Amount = 250_000m,
            ItemName = "Site office structure", CategoryId = world.NoTaxHead.Id,
            Date = new DateTime(2026, 8, 12), ConcurrencyToken = token
        });
        Assert.Equal(250_000m, (await accounts.GetByIdAsync(wip.Id)).CurrentBalance);
        Assert.True((await service.GetBalanceSheetAsync(null, AsAt)).IsBalanced);

        // The historical balance is still an asset on the sheet — the rule changed for new spending,
        // it did not reclassify what the previous ERP left behind.
        var sheet = await service.GetBalanceSheetAsync(null, AsAt);
        Assert.Equal(250_000m, sheet.AssetGroups.Single(g => g.Name == "Work in Progress").Total);

        // But a NEW purchase still cannot be pointed at it.
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAssetPurchaseAsync(
            new CreateAssetPurchaseDto
            {
                AssetAccountId = wip.Id, FinanceAccountId = world.Hbl.Id, Amount = 10_000m,
                ItemName = "More cement", CategoryId = world.NoTaxHead.Id
            }, adminUserId: 1));

        // …and an existing fixed-asset purchase cannot be MOVED onto it either.
        var fixedAsset = await service.CreateAssetPurchaseAsync(new CreateAssetPurchaseDto
        {
            AssetAccountId = world.Furniture.Id, FinanceAccountId = world.Hbl.Id, Amount = 5_000m,
            ItemName = "Chair", CategoryId = world.NoTaxHead.Id, Date = new DateTime(2026, 8, 12)
        }, adminUserId: 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAssetPurchaseAsync(
            fixedAsset.Id, new UpdateAssetPurchaseDto
            {
                AssetAccountId = wip.Id, FinanceAccountId = world.Hbl.Id, Amount = 5_000m,
                ItemName = "Chair", CategoryId = world.NoTaxHead.Id,
                Date = new DateTime(2026, 8, 12), ConcurrencyToken = fixedAsset.ConcurrencyToken
            }));
    }

    // ── The client's fixed-asset profit rule, and the question it leaves open ────────
    //
    // The client has told us two things: the asset stays on the sheet at its full cost, and the same
    // amount reduces Net Profit at once. They have NOT told us which account carries the balancing
    // credit for that reduction. So the reduction is applied where presenting it posts nothing (the
    // dashboard) and disclosed where posting it would be a half-entry (the formal statements). The
    // line these tests hold: the statements balance on real entries only, and none is invented.

    /// <summary>
    /// The client's acceptance test for the single Net Profit rule, to the figures they gave:
    /// revenue 10m, ordinary expenses 2m, fixed assets 1m, and 7m of Net Profit wherever the words
    /// "Net Profit" appear — dashboard, statement, export and drill-down. There is no second profit
    /// figure and no pending-deduction workaround anywhere in that list.
    /// </summary>
    [Fact]
    public async Task OneNetProfit_OnTheDashboard_TheStatement_TheExport_AndTheDrillDown()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var service = Finance(context);

        await service.CreateManualRevenueAsync(new CreateManualRevenueDto
        {
            FinanceAccountId = world.Hbl.Id, Amount = 10_000_000m, RevenueType = "Other income",
            Date = new DateTime(2026, 8, 1)
        }, adminUserId: 1);
        await service.CreateExpenseAsync(new CreateExpenseDto
        {
            FinanceAccountId = world.Hbl.Id, Amount = 2_000_000m, CategoryId = world.NoTaxHead.Id,
            Date = new DateTime(2026, 8, 5)
        }, adminUserId: 1);
        await service.CreateAssetPurchaseAsync(new CreateAssetPurchaseDto
        {
            AssetAccountId = world.Equipment.Id, FinanceAccountId = world.Hbl.Id, Amount = 1_000_000m,
            ItemName = "Generator", CategoryId = world.NoTaxHead.Id, Date = new DateTime(2026, 8, 10)
        }, adminUserId: 1);

        var summary = await service.GetSummaryAsync(null, Year.Start, Year.End);
        var pnl = await service.GetProfitAndLossAsync(null, Year.Start, Year.End);
        var drillDown = await service.GetNetProfitPageAsync(null, Year.Start, Year.End, 0, 200);
        var export = SheetXml(await service.ExportProfitAndLossAsync(null, Year.Start, Year.End));

        Assert.Equal(7_000_000m, summary.NetProfit);
        Assert.Equal(7_000_000m, pnl.NetProfit);
        Assert.Equal(7_000_000m, drillDown.Items.Sum(i => i.Amount));
        Assert.Contains("7000000", export);

        // The same on the way there: one expense total, with the purchase inside it as a named line.
        Assert.Equal(3_000_000m, summary.TotalExpenses);
        Assert.Equal(3_000_000m, pnl.TotalExpenses);
        Assert.Equal(1_000_000m, Assert.Single(pnl.ExpenseLines, l => l.Name == "Fixed Asset Purchases").Amount);
        Assert.Equal(1_000_000m, summary.TotalAssetPurchases);
        // Nothing is held back for the reader to add on afterwards.
        Assert.DoesNotContain("NOT APPLIED", export, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("awaiting accounting decision", export, StringComparison.OrdinalIgnoreCase);

        // The asset is on the sheet at cost and the sheet balances on real double entry — no reserve,
        // no contra-asset, no depreciation line. Its retained profit is therefore HIGHER than Net
        // Profit by the purchase, and it names that difference instead of leaving it to be found.
        var sheet = await service.GetBalanceSheetAsync(null, AsAt);
        Assert.Equal(1_000_000m, Group(sheet, "Fixed Assets").Total);
        Assert.Equal(8_000_000m, sheet.RetainedProfit);
        Assert.Equal(1_000_000m, sheet.UnpostedFixedAssetCharge);
        Assert.Equal(pnl.NetProfit, sheet.RetainedProfit - sheet.UnpostedFixedAssetCharge);
        Assert.True(sheet.IsBalanced);
        Assert.DoesNotContain(sheet.CapitalLines, l => l.AccountId < 0);

        // Same on the Trial Balance: every debit has its credit, because every row is a real entry.
        // No orphan "Fixed Asset Purchases" debit, and no invented equity row to offset one.
        var trial = await service.GetTrialBalanceAsync(null, AsAt, 0);
        Assert.True(Assert.Single(trial.ColumnBalanced));
        Assert.Equal(Assert.Single(trial.ColumnDebitTotals), Assert.Single(trial.ColumnCreditTotals));
        Assert.DoesNotContain(trial.Rows, r => r.AccountName == "Fixed Asset Purchases");
        Assert.DoesNotContain(trial.Rows, r => r.Type == FinanceAccountType.Capital && r.AccountId < 0);
        // The purchase still shows where it genuinely happened: the asset account itself.
        Assert.Equal(1_000_000m, trial.Rows.Single(r => r.AccountName == "Office Equipment").DebitBalances[0]);
    }

    /// <summary>
    /// The drill-down behind the Net Profit card. Adding up the rows in front of the operator has to
    /// land on the figure they clicked — nothing to add back, nothing to exclude.
    /// </summary>
    [Fact]
    public async Task TheNetProfitDrillDown_ReconcilesToTheNetProfitCard()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var service = Finance(context);

        await service.CreateManualRevenueAsync(new CreateManualRevenueDto
        {
            FinanceAccountId = world.Hbl.Id, Amount = 500_000m, RevenueType = "Other income",
            Date = new DateTime(2026, 8, 1)
        }, adminUserId: 1);
        await service.CreateExpenseAsync(new CreateExpenseDto
        {
            FinanceAccountId = world.Hbl.Id, Amount = 120_000m, CategoryId = world.NoTaxHead.Id,
            Date = new DateTime(2026, 8, 5)
        }, adminUserId: 1);
        await service.CreateAssetPurchaseAsync(new CreateAssetPurchaseDto
        {
            AssetAccountId = world.Furniture.Id, FinanceAccountId = world.Hbl.Id, Amount = 200_000m,
            ItemName = "Boardroom table", CategoryId = world.NoTaxHead.Id, Date = new DateTime(2026, 8, 10)
        }, adminUserId: 1);

        var page = await service.GetNetProfitPageAsync(null, Year.Start, Year.End, 0, 100);
        var summary = await service.GetSummaryAsync(null, Year.Start, Year.End);

        // Every row, added up, is the card.
        Assert.Equal(summary.NetProfit, page.Items.Sum(i => i.Amount));
        Assert.Equal(180_000m, summary.NetProfit);
        // Two kinds only. A third would be a row the reader has to know to treat differently.
        Assert.All(page.Items, i => Assert.True(i.Kind is "revenue" or "expense", "unexpected kind: " + i.Kind));

        // The purchase is an ordinary cost row, signed as a deduction and named for what it is.
        var purchase = Assert.Single(page.Items, i => i.Label.Contains("Boardroom table"));
        Assert.Equal("expense", purchase.Kind);
        Assert.Equal(-200_000m, purchase.Amount);
        Assert.Equal(new DateTime(2026, 8, 10), purchase.Date);
    }

    /// <summary>
    /// The one difference between the two statements travels with the exported workbook, not just the
    /// screen.
    /// <para>
    /// The spreadsheet is what gets emailed to the accountant who has to decide where the balancing
    /// entry belongs. The P&amp;L deducts the purchase; this sheet's retained profit cannot, because
    /// the asset above it is still carried at cost. If that gap is explained only in the browser, the
    /// file that reaches the person answering the question is the one copy that hides it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheExportedBalanceSheet_NamesTheChargeItCouldNotCarry()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var service = Finance(context);

        Assert.DoesNotContain("not to this sheet", SheetXml(await service.ExportBalanceSheetAsync(null, AsAt)));

        await service.CreateAssetPurchaseAsync(new CreateAssetPurchaseDto
        {
            AssetAccountId = world.Furniture.Id, FinanceAccountId = world.Hbl.Id, Amount = 250_000m,
            ItemName = "Filing cabinets", CategoryId = world.NoTaxHead.Id, Date = new DateTime(2026, 8, 10)
        }, adminUserId: 1);

        var sheet = SheetXml(await service.ExportBalanceSheetAsync(null, AsAt));
        Assert.Contains("not to this sheet", sheet);
        Assert.Contains("250000", sheet);

        // And the P&L workbook simply deducts it, with nothing held back below the total.
        var pnl = SheetXml(await service.ExportProfitAndLossAsync(null, Year.Start, Year.End));
        Assert.Contains("Fixed Asset Purchases", pnl);
        Assert.DoesNotContain("NOT APPLIED", pnl, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Historical integrity for the charge. Buying an asset today must not reach into a period that
    /// closed before it happened, on either side.
    /// </summary>
    [Fact]
    public async Task TheProfitCharge_LandsOnlyInThePeriodThePurchaseHappened()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var service = Finance(context);

        await service.CreateAssetPurchaseAsync(new CreateAssetPurchaseDto
        {
            AssetAccountId = world.Equipment.Id, FinanceAccountId = world.Hbl.Id, Amount = 400_000m,
            ItemName = "Server rack", CategoryId = world.NoTaxHead.Id, Date = new DateTime(2026, 8, 10)
        }, adminUserId: 1);

        // July closed before the purchase, so it carries nothing.
        var july = await service.GetProfitAndLossAsync(null, new DateTime(2026, 7, 1), new DateTime(2026, 7, 31));
        Assert.DoesNotContain(july.ExpenseLines, l => l.Name == "Fixed Asset Purchases");
        Assert.Equal(0m, july.NetProfit);

        // August bears it.
        var august = await service.GetProfitAndLossAsync(null, new DateTime(2026, 8, 1), new DateTime(2026, 8, 31));
        Assert.Equal(400_000m, Assert.Single(august.ExpenseLines, l => l.Name == "Fixed Asset Purchases").Amount);
        Assert.Equal(-400_000m, august.NetProfit);

        // The dashboard's deduction is period-correct too, and identical: August only.
        Assert.Equal(0m, (await service.GetSummaryAsync(null, new DateTime(2026, 7, 1), new DateTime(2026, 7, 31))).NetProfit);
        Assert.Equal(-400_000m, (await service.GetSummaryAsync(null, new DateTime(2026, 8, 1), new DateTime(2026, 8, 31))).NetProfit);

        // Balance sheets either side of the purchase both balance, and neither carries the charge in
        // its retained profit — only the one taken after it names the amount it could not carry.
        foreach (var asAt in new[] { new DateTime(2026, 8, 9), new DateTime(2026, 8, 10) })
        {
            var sheet = await service.GetBalanceSheetAsync(null, asAt);
            Assert.Equal(0m, sheet.RetainedProfit);
            Assert.True(sheet.IsBalanced);
        }
        Assert.Equal(0m, (await service.GetBalanceSheetAsync(null, new DateTime(2026, 8, 9))).UnpostedFixedAssetCharge);
        Assert.Equal(400_000m, (await service.GetBalanceSheetAsync(null, new DateTime(2026, 8, 10))).UnpostedFixedAssetCharge);
    }

    [Fact]
    public async Task TheDestinationMustBeCapitalisable_AndTheSourceMustBeCash()
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
    public async Task AHeadUsedOnlyByAnAssetPurchase_CountsAsUsed_AndIsRetiredRatherThanDeleted()
    {
        await using var context = Context();
        var world = await SeedAsync(context);

        await Finance(context).CreateAssetPurchaseAsync(new CreateAssetPurchaseDto
        {
            AssetAccountId = world.Furniture.Id, FinanceAccountId = world.Hbl.Id,
            Amount = 200_000m, ItemName = "3 office desks", CategoryId = world.NoTaxHead.Id
        }, adminUserId: 1);

        var categories = new ExpenseCategoryService(context);

        // The settings screen offers Retire or Delete based on this count. Counting expenses alone
        // would offer Delete on a head the database is bound to refuse to drop.
        var listed = (await categories.GetAllAsync(true)).Single(c => c.Id == world.NoTaxHead.Id);
        Assert.Equal(1, listed.UsageCount);

        var outcome = await categories.DeleteAsync(world.NoTaxHead.Id);

        // Retired, not deleted — the purchase filed under it still needs the name on old reports.
        Assert.NotNull(outcome);
        Assert.False(outcome!.IsActive);
        Assert.True(await context.ExpenseCategories.AnyAsync(c => c.Id == world.NoTaxHead.Id));
    }

    [Fact]
    public async Task ASupplierPaidOnlyForAssets_ShowsRealYearToDateTotals_NotZero()
    {
        await using var context = Context();
        var world = await SeedAsync(context);

        await Finance(context).CreateAssetPurchaseAsync(new CreateAssetPurchaseDto
        {
            AssetAccountId = world.Equipment.Id, FinanceAccountId = world.Hbl.Id,
            Amount = 100_000m, ItemName = "Dell Latitude laptop",
            CategoryId = world.GoodsHead.Id, VendorId = world.Supplier.Id,
            Date = PakistanTime.Today
        }, adminUserId: 1);

        var vendors = new VendorService(context);
        var listed = (await vendors.GetPageAsync(null, false, 0, 50)).Items.Single(v => v.Id == world.Supplier.Id);

        Assert.Equal(100_000m, listed.YearToDateGross);
        Assert.Equal(10_000m, listed.YearToDateWht);
        Assert.Equal(1, listed.PaymentCount);

        // The list and the supplier's own breakdown are two views of the same payments. If they
        // disagree, one of them is telling an admin the threshold has room it does not have.
        var breakdown = await vendors.GetYearToDateAsync(world.Supplier.Id, null);
        Assert.Equal(breakdown.Sum(line => line.GrossPaid), listed.YearToDateGross);
        Assert.Equal(breakdown.Sum(line => line.WhtWithheld), listed.YearToDateWht);
    }

    /// <summary>
    /// The trial balance ties out with a withheld purchase in play.
    /// <para>
    /// Worth pinning on its own, because withholding splits the credit across two places — the paying
    /// account and the tax payable — and a fault in that split would leave the column out by the tax.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheTrialBalanceTiesOut_WithPurchasesAndWithholdingInPlay()
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
        Assert.Equal(10_000m, trial.Rows.Single(r => r.AccountName == "Tax Payable").CreditBalances[0]);
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

    /// <summary>The first worksheet of an exported workbook, as raw XML.</summary>
    private static string SheetXml(FinanceExportDto export)
    {
        using var archive = new ZipArchive(new MemoryStream(export.Content), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        return reader.ReadToEnd();
    }

    /// <summary>
    /// A work-in-progress account, as inherited from the previous ERP. Still a legitimate account
    /// holding a real historical balance — it simply cannot take new purchases any more.
    /// </summary>
    private static FinanceAccount Wip(AppDbContext context, string name, string ledgerCode)
    {
        var account = new FinanceAccount
        {
            Name = name, LedgerCode = ledgerCode, AccountHolderName = "Seven Ventures",
            Type = FinanceAccountType.WorkInProgress, IsActive = true
        };
        context.FinanceAccounts.Add(account);
        return account;
    }

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
