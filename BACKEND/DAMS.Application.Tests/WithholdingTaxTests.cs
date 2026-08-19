using DAMS.Application.Common;
using DAMS.Application.DTOs.ExpenseDtos;
using DAMS.Application.DTOs.WhtDtos;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// Withholding tax. The invariant under nearly every test here: an expense costs the business its
/// GROSS amount, but costs the bank only its NET, and the difference is a liability owed to FBR.
/// </summary>
public sealed class WithholdingTaxTests
{
    // ── Pure calculation ────────────────────────────────────────────────────────

    [Fact]
    public void FilerAndNonFiler_GetDifferentRates_AndUnknownIsTreatedAsNonFiler()
    {
        var category = new WhtCalculator.CategoryRates(true, FilerRate: 4m, NonFilerRate: 8m, AnnualThreshold: 0m);

        Assert.Equal(4m, WhtCalculator.RateFor(category, FilerStatus.Filer));
        Assert.Equal(8m, WhtCalculator.RateFor(category, FilerStatus.NonFiler));
        // Under-deducting is the penalised direction; over-deducting is refunded on filing.
        Assert.Equal(8m, WhtCalculator.RateFor(category, FilerStatus.Unknown));
    }

    [Fact]
    public void Tax_RoundsHalfUp_NotWithBankersRounding()
    {
        // 50.005 at 5% is exactly 2.50025 -> 2.50; the case that matters is the exact midpoint.
        // 1% of 50 = 0.5 -> the .NET default would round 0.5 to 0 (to even) and under-deduct.
        Assert.Equal(0.5m, WhtCalculator.TaxOn(50m, 1m));
        Assert.Equal(2.51m, WhtCalculator.TaxOn(50.1m, 5.01m));
        Assert.Equal(0.03m, WhtCalculator.TaxOn(1m, 2.5m));
    }

    [Fact]
    public void NetPaid_IsAlwaysGrossMinusTax_SoTheLedgerBalances()
    {
        var category = new WhtCalculator.CategoryRates(true, 7.5m, 15m, 0m);
        var result = WhtCalculator.Compute(category, FilerStatus.Filer, 1_000_000m, 0m);

        Assert.Equal(75_000m, result.WhtAmount);
        Assert.Equal(925_000m, result.NetPaid);
        Assert.Equal(1_000_000m, result.WhtAmount + result.NetPaid);
    }

    [Fact]
    public void NonApplicableCategory_WithholdsNothing()
    {
        var salaries = new WhtCalculator.CategoryRates(IsWhtApplicable: false, 0m, 0m, 0m);
        var result = WhtCalculator.Compute(salaries, FilerStatus.NonFiler, 500_000m, 0m);

        Assert.False(result.IsWhtApplicable);
        Assert.Equal(0m, result.WhtAmount);
        Assert.Equal(500_000m, result.NetPaid);
    }

    [Fact]
    public void Threshold_IsAnAnnualAggregate_NotAPerInvoiceTest()
    {
        var goods = new WhtCalculator.CategoryRates(true, 5m, 10m, AnnualThreshold: 75_000m);

        // First 40,000 of the year: under the threshold on its own and in aggregate.
        var first = WhtCalculator.Compute(goods, FilerStatus.Filer, 40_000m, yearToDateTotal: 0m);
        Assert.True(first.BelowThreshold);
        Assert.Equal(0m, first.WhtAmount);

        // Second 40,000: still small, but 80,000 for the year crosses 75,000, so the whole
        // payment is withheld.
        var second = WhtCalculator.Compute(goods, FilerStatus.Filer, 40_000m, yearToDateTotal: 40_000m);
        Assert.False(second.BelowThreshold);
        Assert.Equal(2_000m, second.WhtAmount);
    }

    [Fact]
    public void Threshold_OfZero_WithholdsFromTheFirstRupee()
    {
        var rent = new WhtCalculator.CategoryRates(true, 5m, 10m, AnnualThreshold: 0m);
        var result = WhtCalculator.Compute(rent, FilerStatus.Filer, 1_000m, 0m);

        Assert.False(result.BelowThreshold);
        Assert.Equal(50m, result.WhtAmount);
    }

    [Fact]
    public void EffectiveRate_IsBackComputedFromAHandEnteredAmount()
    {
        Assert.Equal(4.7619m, WhtCalculator.EffectiveRate(210_000m, 10_000m));
        Assert.Equal(0m, WhtCalculator.EffectiveRate(0m, 0m));
    }

    // ── Financial year ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2026-08-11", "2026-07-01", "2027-07-01", "2026-27")]
    [InlineData("2026-06-30", "2025-07-01", "2026-07-01", "2025-26")]
    [InlineData("2026-07-01", "2026-07-01", "2027-07-01", "2026-27")]
    public void FinancialYear_RunsJulyToJune(string date, string start, string end, string label)
    {
        var (windowStart, windowEnd) = FinancialYear.Window(DateTime.Parse(date), 7);

        Assert.Equal(DateTime.Parse(start), windowStart);
        Assert.Equal(DateTime.Parse(end), windowEnd);
        Assert.Equal(label, FinancialYear.Label(DateTime.Parse(date), 7));
    }

    [Fact]
    public void FinancialYear_StartMonthIsConfigurable_AndACorruptValueFallsBackToJuly()
    {
        var (january, _) = FinancialYear.Window(new DateTime(2026, 8, 11), 1);
        Assert.Equal(new DateTime(2026, 1, 1), january);
        Assert.Equal("2026", FinancialYear.Label(new DateTime(2026, 8, 11), 1));

        // A corrupt setting must never throw inside a tax calculation.
        Assert.Equal(7, FinancialYear.Normalise(0));
        Assert.Equal(7, FinancialYear.Normalise(13));
    }

    // ── End to end through the expense pipeline ─────────────────────────────────

    [Fact]
    public async Task Expense_IsBookedGross_ButOnlyTheNetLeavesTheAccount()
    {
        await using var context = Seeded();
        var finance = Finance(context);

        var expense = await finance.CreateExpenseAsync(new CreateExpenseDto
        {
            FinanceAccountId = 1, VendorId = 1, CategoryId = 1, Amount = 1_000_000m
        }, adminUserId: 1);

        Assert.Equal(1_000_000m, expense.Amount);
        Assert.Equal(1m, expense.WhtRate);          // Cement, filer
        Assert.Equal(10_000m, expense.WhtAmount);
        Assert.Equal(990_000m, expense.NetPaid);
        Assert.True(expense.WhtApplied);
        Assert.Equal("153(1)(a)", expense.WhtTaxSection);

        // The P&L still shows the full cost of the purchase...
        var summary = await finance.GetSummaryAsync(null, null, null);
        Assert.Equal(1_000_000m, summary.TotalExpenses);
        Assert.Equal(10_000m, summary.WhtWithheld);

        // ...while the bank only lost what actually left it.
        var account = await new FinanceAccountService(context).GetByIdAsync(1);
        Assert.Equal(990_000m, account.ExpensesPaid);
        Assert.Equal(10_000m, account.WhtWithheld);
        Assert.Equal(0m, account.WhtDeposited);
        Assert.Equal(-990_000m, account.NetMovement);
        Assert.Equal(-990_000m, account.CurrentBalance);
    }

    [Fact]
    public async Task DepositingWithFbr_ReducesTheAccount_ButIsNotABusinessExpense()
    {
        await using var context = Seeded();
        var finance = Finance(context);
        await finance.CreateExpenseAsync(new CreateExpenseDto
        {
            FinanceAccountId = 1, VendorId = 1, CategoryId = 1, Amount = 1_000_000m
        }, 1);

        var wht = Wht(context);
        var payableBefore = await wht.GetPayableSummaryAsync(null, null);
        Assert.Equal(10_000m, payableBefore.OutstandingPayable);

        await wht.CreateDepositAsync(new SaveWhtDepositDto
        {
            FinanceAccountId = 1, Amount = 10_000m, ChallanNumber = "CPR-001"
        }, 1);

        var payableAfter = await wht.GetPayableSummaryAsync(null, null);
        Assert.Equal(0m, payableAfter.OutstandingPayable);
        Assert.Equal(10_000m, payableAfter.TotalDepositedAllTime);

        // The deposit is cash leaving the account...
        var account = await new FinanceAccountService(context).GetByIdAsync(1);
        Assert.Equal(1_000_000m, account.ExpensesPaid);

        // ...but it is not a cost of doing business, so profit is unchanged.
        var summary = await finance.GetSummaryAsync(null, null, null);
        Assert.Equal(1_000_000m, summary.TotalExpenses);
        Assert.Equal(-1_000_000m, summary.NetProfit);
    }

    [Fact]
    public async Task AccountTransactionList_ShowsTheCashEffect_WithGrossAndTaxBesideIt()
    {
        await using var context = Seeded();
        var finance = Finance(context);
        await finance.CreateExpenseAsync(new CreateExpenseDto
        { FinanceAccountId = 1, VendorId = 1, CategoryId = 1, Amount = 1_000_000m }, 1);
        await Wht(context).CreateDepositAsync(new SaveWhtDepositDto
        { FinanceAccountId = 1, Amount = 10_000m, ChallanNumber = "CPR-9" }, 1);

        var rows = (await new FinanceAccountService(context).GetTransactionsAsync(1, 0, 50)).Items;

        var expense = Assert.Single(rows, r => r.Kind == "Expense");
        // The signed amount is what hit the bank; gross and tax ride alongside so the row can be
        // reconciled against the invoice.
        Assert.Equal(-990_000m, expense.Amount);
        Assert.Equal(1_000_000m, expense.GrossAmount);
        Assert.Equal(10_000m, expense.WhtAmount);

        var deposit = Assert.Single(rows, r => r.Kind == "WHT deposit");
        Assert.Equal(-10_000m, deposit.Amount);
        Assert.Equal("CPR-9", deposit.Reference);

        // Opening 0, less the 990,000 paid out, less the 10,000 handed to FBR.
        Assert.Equal(-1_000_000m, rows.Sum(r => r.Amount));
    }

    [Fact]
    public async Task DeletingAnExpense_ReleasesTheTaxItWasHolding()
    {
        await using var context = Seeded();
        var finance = Finance(context);
        var created = await finance.CreateExpenseAsync(new CreateExpenseDto
        { FinanceAccountId = 1, VendorId = 1, CategoryId = 1, Amount = 1_000_000m }, 1);
        Assert.Equal(10_000m, (await Wht(context).GetPayableSummaryAsync(null, null)).OutstandingPayable);

        await finance.DeleteExpenseAsync(created.Id);

        Assert.Equal(0m, (await Wht(context).GetPayableSummaryAsync(null, null)).OutstandingPayable);
        Assert.Equal(0m, (await new FinanceAccountService(context).GetByIdAsync(1)).CurrentBalance);
    }

    [Fact]
    public async Task LandAndStatutoryHeads_SurviveFromTheOldExpenseList_AndCarryNoTax()
    {
        // These three were heads the expense form offered before categories existed, and none has
        // a close equivalent among the construction categories. Losing them would have taken a
        // property developer's largest cost line with them.
        await using var context = Seeded();
        context.ExpenseCategories.AddRange(
            new ExpenseCategory { Id = 60, Name = "Land Acquisition", Code = "land_acquisition", IsWhtApplicable = false, IsActive = true, RowVersion = [1] },
            new ExpenseCategory { Id = 61, Name = "Permits & Approvals", Code = "permits_approvals", IsWhtApplicable = false, IsActive = true, RowVersion = [1] },
            new ExpenseCategory { Id = 62, Name = "Taxes & Fees", Code = "taxes_fees", IsWhtApplicable = false, IsActive = true, RowVersion = [1] });
        await context.SaveChangesAsync();

        var expense = await Finance(context).CreateExpenseAsync(new CreateExpenseDto
        { FinanceAccountId = 1, VendorId = 1, CategoryId = 60, Amount = 50_000_000m }, 1);

        Assert.Equal("Land Acquisition", expense.Category);
        Assert.Equal(0m, expense.WhtAmount);
        Assert.Equal(50_000_000m, expense.NetPaid);
        Assert.False(expense.WhtApplied);
    }

    [Fact]
    public async Task UnknownFilerStatus_IsWithheldAtTheNonFilerRate()
    {
        await using var context = Seeded();
        context.Vendors.Add(new Vendor { Id = 2, Name = "Unverified Traders", FilerStatus = FilerStatus.Unknown });
        await context.SaveChangesAsync();

        var expense = await Finance(context).CreateExpenseAsync(new CreateExpenseDto
        {
            FinanceAccountId = 1, VendorId = 2, CategoryId = 1, Amount = 100_000m
        }, 1);

        Assert.Equal(2m, expense.WhtRate);
        Assert.Equal(2_000m, expense.WhtAmount);
        Assert.Equal(FilerStatus.Unknown, expense.VendorFilerStatusAtEntry);
    }

    [Fact]
    public async Task NoVendorSelected_StillWithholds_AtTheNonFilerRate()
    {
        await using var context = Seeded();

        var expense = await Finance(context).CreateExpenseAsync(new CreateExpenseDto
        {
            FinanceAccountId = 1, CategoryId = 1, Vendor = "Cash purchase", Amount = 100_000m
        }, 1);

        Assert.Equal(2m, expense.WhtRate);
        Assert.Null(expense.VendorId);
        Assert.Equal("Cash purchase", expense.Vendor);
    }

    [Fact]
    public async Task EditingACategoryRate_NeverRestatesTaxAlreadyWithheld()
    {
        await using var context = Seeded();
        var created = await Finance(context).CreateExpenseAsync(new CreateExpenseDto
        {
            FinanceAccountId = 1, VendorId = 1, CategoryId = 1, Amount = 1_000_000m
        }, 1);
        Assert.Equal(10_000m, created.WhtAmount);

        var categories = new ExpenseCategoryService(context);
        var cement = await categories.GetByIdAsync(1);
        await categories.UpdateAsync(1, new SaveExpenseCategoryDto
        {
            Name = cement.Name, Code = cement.Code, IsWhtApplicable = true,
            FilerRate = 9m, NonFilerRate = 18m, AnnualThreshold = cement.AnnualThreshold,
            TaxSection = cement.TaxSection, DisplayOrder = cement.DisplayOrder, IsActive = true,
            ConcurrencyToken = cement.ConcurrencyToken
        });

        // The filed figure is frozen on the expense; only new entries see the new rate.
        var stored = await context.Expenses.AsNoTracking().SingleAsync(e => e.Id == created.Id);
        Assert.Equal(1m, stored.WhtRate);
        Assert.Equal(10_000m, stored.WhtAmount);
    }

    [Fact]
    public async Task ThresholdIsTrackedPerVendorPerSection_NotPerCategory()
    {
        await using var context = Seeded();
        var finance = Finance(context);

        // Cement and bricks are different heads but the same section, so the 75,000 goods
        // threshold is one allowance across both — not one each.
        var first = await finance.CreateExpenseAsync(new CreateExpenseDto
        { FinanceAccountId = 1, VendorId = 1, CategoryId = 1, Amount = 50_000m }, 1);
        Assert.Equal(0m, first.WhtAmount);

        var second = await finance.CreateExpenseAsync(new CreateExpenseDto
        { FinanceAccountId = 1, VendorId = 1, CategoryId = 2, Amount = 50_000m }, 1);
        Assert.Equal(2_500m, second.WhtAmount);   // bricks, filer 5%
    }

    [Fact]
    public async Task RepeatedFreeTextPayments_CannotHideUnderTheThresholdForever()
    {
        // Without a vendor record there is no identity to aggregate against, so the annual
        // allowance cannot be proven — and assuming it applies would let a supplier be paid in
        // slices that each look exempt while the year's total is far over the limit.
        await using var context = Seeded();
        var finance = Finance(context);

        var first = await finance.CreateExpenseAsync(new CreateExpenseDto
        { FinanceAccountId = 1, CategoryId = 1, Vendor = "ABC Traders", Amount = 40_000m }, 1);
        var second = await finance.CreateExpenseAsync(new CreateExpenseDto
        { FinanceAccountId = 1, CategoryId = 1, Vendor = "ABC Traders", Amount = 40_000m }, 1);

        // Non-filer rate, because an unidentified payee cannot be shown to be on the ATL either.
        Assert.Equal(800m, first.WhtAmount);
        Assert.Equal(800m, second.WhtAmount);
    }

    [Fact]
    public async Task LinkingAVendor_StillGrantsTheThreshold()
    {
        // The exemption is available — it just requires saying who was paid.
        await using var context = Seeded();
        var expense = await Finance(context).CreateExpenseAsync(new CreateExpenseDto
        { FinanceAccountId = 1, VendorId = 1, CategoryId = 1, Amount = 40_000m }, 1);

        Assert.Equal(0m, expense.WhtAmount);
    }

    [Fact]
    public async Task SpendPredatingAVendorRecord_StillCountsTowardsTheirAnnualThreshold()
    {
        // Go-live reality: a supplier was paid as free text for months, then gets a vendor record.
        // Those payments are the same supplier's, so ignoring them would under-withhold for the
        // whole first year.
        await using var context = Seeded();
        var finance = Finance(context);

        await finance.CreateExpenseAsync(new CreateExpenseDto
        { FinanceAccountId = 1, CategoryId = 1, Vendor = "ABC Traders", Amount = 70_000m }, 1);

        var afterLinking = await finance.CreateExpenseAsync(new CreateExpenseDto
        { FinanceAccountId = 1, CategoryId = 1, VendorId = 1, Amount = 20_000m }, 1);

        // 70,000 + 20,000 is over the 75,000 allowance, so the whole payment is withheld at the
        // filer rate — not treated as the first 20,000 of the year.
        Assert.Equal(200m, afterLinking.WhtAmount);
    }

    [Fact]
    public async Task EditingAnExpense_DoesNotCountItsOwnAmountTowardsItsThreshold()
    {
        await using var context = Seeded();
        var finance = Finance(context);
        var created = await finance.CreateExpenseAsync(new CreateExpenseDto
        { FinanceAccountId = 1, VendorId = 1, CategoryId = 1, Amount = 50_000m }, 1);
        Assert.Equal(0m, created.WhtAmount);

        // Re-saving unchanged must not push itself over the threshold by double counting.
        var updated = await finance.UpdateExpenseAsync(created.Id, new UpdateExpenseDto
        { FinanceAccountId = 1, VendorId = 1, CategoryId = 1, Amount = 50_000m });

        Assert.Equal(0m, updated.WhtAmount);
    }

    [Fact]
    public async Task OverridingTheTax_RequiresAReason_AndIsFlagged()
    {
        await using var context = Seeded();
        var finance = Finance(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => finance.CreateExpenseAsync(new CreateExpenseDto
        {
            FinanceAccountId = 1, VendorId = 1, CategoryId = 1, Amount = 1_000_000m, WhtAmount = 15_000m
        }, 1));

        var expense = await finance.CreateExpenseAsync(new CreateExpenseDto
        {
            FinanceAccountId = 1, VendorId = 1, CategoryId = 1, Amount = 1_000_000m,
            WhtAmount = 15_000m, WhtOverrideReason = "Figure taken from the vendor invoice."
        }, 1);

        Assert.True(expense.WhtRateOverridden);
        Assert.Equal(15_000m, expense.WhtAmount);
        Assert.Equal(1.5m, expense.WhtRate);          // back-computed from the amount
        Assert.Equal(985_000m, expense.NetPaid);
    }

    [Fact]
    public async Task TaxCannotExceedTheExpense_NorBeNegative()
    {
        await using var context = Seeded();
        var finance = Finance(context);

        foreach (var amount in new[] { -1m, 100_001m })
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => finance.CreateExpenseAsync(new CreateExpenseDto
            {
                FinanceAccountId = 1, VendorId = 1, CategoryId = 1, Amount = 100_000m,
                WhtAmount = amount, WhtOverrideReason = "Testing the bound."
            }, 1));
        }
    }

    [Fact]
    public async Task TaxOnAHeadThatCarriesNone_IsRefused_NotSilentlyDropped()
    {
        await using var context = Seeded();
        context.ExpenseCategories.Add(new ExpenseCategory
        { Id = 90, Name = "Salaries", Code = "salaries", IsWhtApplicable = false, IsActive = true });
        await context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Finance(context).CreateExpenseAsync(
            new CreateExpenseDto
            {
                FinanceAccountId = 1, CategoryId = 90, Amount = 100_000m,
                WhtAmount = 5_000m, WhtOverrideReason = "Should not be allowed."
            }, 1));

        Assert.Contains("not subject to withholding tax", error.Message);
    }

    [Fact]
    public async Task FreeTextCategoriesAlreadyOnRecord_StillReadBack_AndCarryNoTax()
    {
        // Free text is closed to new writes (see ANewExpense_CannotUseAFreeTextCategory), but the
        // rows that predate the managed list have to keep working everywhere they are read.
        await using var context = Seeded();
        context.Expenses.Add(new Expense
        { Id = 91, FinanceAccountId = 1, Category = "Some one-off head", Amount = 5_000m });
        await context.SaveChangesAsync();

        var page = await Finance(context).GetExpensePageAsync(null, null, null, 0, 20);
        var expense = Assert.Single(page.Items, e => e.Id == 91);

        Assert.Equal("Some one-off head", expense.Category);
        Assert.Null(expense.CategoryId);
        Assert.Equal(0m, expense.WhtAmount);
        Assert.Equal(5_000m, expense.NetPaid);
    }

    [Fact]
    public async Task SelectingACategory_SnapshotsItsNameOntoTheExpense()
    {
        await using var context = Seeded();

        var expense = await Finance(context).CreateExpenseAsync(new CreateExpenseDto
        {
            // A category id wins over whatever text the caller sent.
            FinanceAccountId = 1, CategoryId = 1, Category = "ignored", Amount = 1_000m
        }, 1);

        Assert.Equal("Cement", expense.Category);
        Assert.Equal(1, expense.CategoryId);
    }

    [Fact]
    public async Task CalculatePreview_ExplainsTheThreshold_WithoutSavingAnything()
    {
        await using var context = Seeded();
        var wht = Wht(context);

        var preview = await wht.CalculateAsync(new WhtCalculationRequestDto
        { CategoryId = 1, VendorId = 1, GrossAmount = 50_000m });

        Assert.True(preview.BelowThreshold);
        Assert.Equal(0m, preview.WhtAmount);
        Assert.Equal(75_000m, preview.AnnualThreshold);
        Assert.Equal(FinancialYear.Label(PakistanTime.Today, 7), preview.FinancialYear);
        Assert.Contains("threshold", preview.Notice, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(context.Expenses);
    }

    [Fact]
    public async Task VendorStatement_GroupsByVendorAndSection_AndTiesOutToTheExport()
    {
        await using var context = Seeded();
        var finance = Finance(context);
        await finance.CreateExpenseAsync(new CreateExpenseDto
        { FinanceAccountId = 1, VendorId = 1, CategoryId = 1, Amount = 1_000_000m }, 1);
        await finance.CreateExpenseAsync(new CreateExpenseDto
        { FinanceAccountId = 1, VendorId = 1, CategoryId = 3, Amount = 200_000m }, 1);

        var lines = await Wht(context).GetByVendorAsync(null, null);

        var goods = Assert.Single(lines, l => l.TaxSection == "153(1)(a)");
        Assert.Equal(1_000_000m, goods.GrossAmount);
        Assert.Equal(10_000m, goods.WhtAmount);
        var services = Assert.Single(lines, l => l.TaxSection == "153(1)(b)");
        Assert.Equal(200_000m, services.GrossAmount);
        Assert.Equal(12_000m, services.WhtAmount);  // transport, filer 6%

        var (fileName, content) = await Wht(context).ExportAsync(null, null);
        var csv = System.Text.Encoding.UTF8.GetString(content);
        Assert.EndsWith(".csv", fileName);
        Assert.Contains("ABC Traders", csv);
        Assert.Contains("1200000.00,22000.00", csv);   // totals line
    }

    [Fact]
    public async Task ExportEscapesFormulaCharacters_SoAVendorNameCannotBecomeASpreadsheetCommand()
    {
        await using var context = Seeded();
        context.Vendors.Add(new Vendor { Id = 3, Name = "=cmd|'/c calc'!A1", FilerStatus = FilerStatus.Filer });
        await context.SaveChangesAsync();
        await Finance(context).CreateExpenseAsync(new CreateExpenseDto
        { FinanceAccountId = 1, VendorId = 3, CategoryId = 1, Amount = 1_000_000m }, 1);

        var (_, content) = await Wht(context).ExportAsync(null, null);
        var csv = System.Text.Encoding.UTF8.GetString(content);

        Assert.Contains("'=cmd", csv);
        Assert.DoesNotContain("\n=cmd", csv);
    }

    [Fact]
    public async Task DuplicateChallanNumbers_AreRejected()
    {
        await using var context = Seeded();
        // Tax has to have been withheld before any of it can be deposited, or the deposit is refused
        // for having nothing behind it and the challan is never looked at. 1,000,000 of goods from a
        // filer withholds 10,000, which covers both deposits below.
        await Finance(context).CreateExpenseAsync(new CreateExpenseDto
        { FinanceAccountId = 1, VendorId = 1, CategoryId = 1, Amount = 1_000_000m }, 1);
        var wht = Wht(context);
        await wht.CreateDepositAsync(new SaveWhtDepositDto
        { FinanceAccountId = 1, Amount = 5_000m, ChallanNumber = "CPR-77" }, 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => wht.CreateDepositAsync(new SaveWhtDepositDto
        { FinanceAccountId = 1, Amount = 1_000m, ChallanNumber = "CPR-77" }, 1));
    }

    [Fact]
    public async Task RetiringACategoryThatHasExpenses_SoftDeletesIt_SoHistorySurvives()
    {
        await using var context = Seeded();
        await Finance(context).CreateExpenseAsync(new CreateExpenseDto
        { FinanceAccountId = 1, VendorId = 1, CategoryId = 1, Amount = 1_000m }, 1);

        var categories = new ExpenseCategoryService(context);
        var retired = await categories.DeleteAsync(1);

        Assert.NotNull(retired);
        Assert.False(retired!.IsActive);
        Assert.True(await context.ExpenseCategories.AnyAsync(c => c.Id == 1));

        // An unused one is genuinely removed rather than left as clutter.
        Assert.Null(await categories.DeleteAsync(2));
    }

    [Fact]
    public async Task AnInactiveCategoryCannotBeNewlyChosen_ButStaysValidOnTheExpenseThatUsedIt()
    {
        await using var context = Seeded();
        var finance = Finance(context);
        var created = await finance.CreateExpenseAsync(new CreateExpenseDto
        { FinanceAccountId = 1, VendorId = 1, CategoryId = 1, Amount = 1_000m }, 1);

        await new ExpenseCategoryService(context).DeleteAsync(1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => finance.CreateExpenseAsync(new CreateExpenseDto
        { FinanceAccountId = 1, VendorId = 1, CategoryId = 1, Amount = 500m }, 1));

        // Editing the old row must not force a re-classification.
        var updated = await finance.UpdateExpenseAsync(created.Id, new UpdateExpenseDto
        { FinanceAccountId = 1, VendorId = 1, CategoryId = 1, Amount = 2_000m });
        Assert.Equal(2_000m, updated.Amount);
    }

    // ── Fixtures ────────────────────────────────────────────────────────────────

    // ── Vendor identity survives a rename ───────────────────────────────────────

    [Fact]
    public async Task HistoricSpendStillCounts_AfterTheVendorIsRenamed()
    {
        // Matching legacy rows on the vendor's *current* name works only until someone corrects
        // the business name, at which point the whole history detaches and the annual allowance
        // silently restarts. Renaming has to carry that history with it.
        await using var context = Seeded();
        var finance = Finance(context);
        var vendors = new VendorService(context);

        await finance.CreateExpenseAsync(new CreateExpenseDto
        { FinanceAccountId = 1, CategoryId = 1, Vendor = "ABC Traders", Amount = 70_000m }, 1);

        var vendor = await vendors.GetByIdAsync(1);
        await vendors.UpdateAsync(1, new SaveVendorDto
        {
            Name = "ABC Traders (Pvt) Ltd", Ntn = vendor.Ntn, FilerStatus = vendor.FilerStatus,
            IsActive = true, ConcurrencyToken = vendor.ConcurrencyToken
        });

        var afterRename = await finance.CreateExpenseAsync(new CreateExpenseDto
        { FinanceAccountId = 1, CategoryId = 1, VendorId = 1, Amount = 20_000m }, 1);

        // 70,000 + 20,000 is still over the 75,000 allowance, whatever the vendor is called now.
        Assert.Equal(200m, afterRename.WhtAmount);
        // The link is written once, so it cannot be lost again — and the expense keeps the name
        // that was actually on the payment.
        var adopted = await context.Expenses.AsNoTracking().SingleAsync(e => e.Amount == 70_000m);
        Assert.Equal(1, adopted.VendorId);
        Assert.Equal("ABC Traders", adopted.Vendor);
    }

    [Fact]
    public async Task CreatingAVendor_ClaimsTheFreeTextPaymentsAlreadyMadeToThem()
    {
        await using var context = Seeded();
        var finance = Finance(context);

        await finance.CreateExpenseAsync(new CreateExpenseDto
        { FinanceAccountId = 1, CategoryId = 1, Vendor = "XYZ Steel", Amount = 60_000m }, 1);

        var created = await new VendorService(context).CreateAsync(new SaveVendorDto
        { Name = "XYZ Steel", FilerStatus = FilerStatus.Filer, IsActive = true }, 1);

        var adopted = await context.Expenses.AsNoTracking().SingleAsync(e => e.Amount == 60_000m);
        Assert.Equal(created.Id, adopted.VendorId);
    }

    // ── An edit that cannot move the tax must not move the tax ──────────────────

    [Fact]
    public async Task FixingADescription_AfterTheVendorCrossedTheThreshold_LeavesTheFiledTaxAlone()
    {
        await using var context = Seeded();
        var finance = Finance(context);

        var january = await finance.CreateExpenseAsync(new CreateExpenseDto
        {
            FinanceAccountId = 1, VendorId = 1, CategoryId = 1, Amount = 40_000m,
            Date = new DateTime(2026, 1, 10), Description = "cement purchse"
        }, 1);
        Assert.Equal(0m, january.WhtAmount);

        // February crosses the 75,000 allowance. January is deliberately not retro-assessed.
        await finance.CreateExpenseAsync(new CreateExpenseDto
        {
            FinanceAccountId = 1, VendorId = 1, CategoryId = 1, Amount = 40_000m,
            Date = new DateTime(2026, 2, 10)
        }, 1);

        // Correcting a typo in March re-submits the stored figures, exactly as the form does.
        var corrected = await finance.UpdateExpenseAsync(january.Id, new UpdateExpenseDto
        {
            FinanceAccountId = 1, VendorId = 1, CategoryId = 1, Amount = 40_000m,
            Date = new DateTime(2026, 1, 10), Description = "cement purchase",
            WhtRate = 0m, WhtAmount = 0m
        });

        Assert.Equal("cement purchase", corrected.Description);
        Assert.Equal(0m, corrected.WhtAmount);
        Assert.False(corrected.WhtRateOverridden);
    }

    [Fact]
    public async Task ChangingTheAmount_DoesRecalculateTheTax()
    {
        // The other half of the guard: the four inputs the tax is actually based on must still
        // re-run, or a corrected invoice would keep the tax for the wrong figure.
        await using var context = Seeded();
        var finance = Finance(context);

        var first = await finance.CreateExpenseAsync(new CreateExpenseDto
        { FinanceAccountId = 1, VendorId = 1, CategoryId = 1, Amount = 40_000m }, 1);
        await finance.CreateExpenseAsync(new CreateExpenseDto
        { FinanceAccountId = 1, VendorId = 1, CategoryId = 1, Amount = 40_000m }, 1);

        var raised = await finance.UpdateExpenseAsync(first.Id, new UpdateExpenseDto
        { FinanceAccountId = 1, VendorId = 1, CategoryId = 1, Amount = 50_000m });

        // 40,000 already paid plus 50,000 is over the allowance, so the whole payment is withheld.
        Assert.Equal(500m, raised.WhtAmount);
    }

    [Fact]
    public async Task ChangingTheCategory_DoesRecalculateTheTax()
    {
        await using var context = Seeded();
        var finance = Finance(context);

        var created = await finance.CreateExpenseAsync(new CreateExpenseDto
        { FinanceAccountId = 1, VendorId = 1, CategoryId = 1, Amount = 100_000m }, 1);
        Assert.Equal(1_000m, created.WhtAmount);           // cement, filer 1%

        var moved = await finance.UpdateExpenseAsync(created.Id, new UpdateExpenseDto
        { FinanceAccountId = 1, VendorId = 1, CategoryId = 3, Amount = 100_000m });

        Assert.Equal(6_000m, moved.WhtAmount);             // transport, filer 6%
        Assert.Equal("153(1)(b)", moved.WhtTaxSection);
    }

    // ── Free-text heads are closed to new writes ────────────────────────────────

    [Fact]
    public async Task ANewExpense_CannotUseAFreeTextCategory()
    {
        // Otherwise picking "Custom" and typing "Labour" pays a taxable supplier with nothing
        // withheld, while choosing the managed Labour head withholds 15%.
        await using var context = Seeded();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Finance(context).CreateExpenseAsync(new CreateExpenseDto
            { FinanceAccountId = 1, Category = "Labour", Amount = 500_000m }, 1));

        Assert.Contains("category", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnExpenseRecordedBeforeTheManagedList_StaysEditable()
    {
        // History has to stay correctable, so the rule applies to new writes only.
        await using var context = Seeded();
        context.Expenses.Add(new Expense
        { Id = 90, FinanceAccountId = 1, Amount = 5_000m, Category = "Old Head", Date = new DateTime(2026, 1, 5) });
        await context.SaveChangesAsync();

        var edited = await Finance(context).UpdateExpenseAsync(90, new UpdateExpenseDto
        {
            FinanceAccountId = 1, Amount = 5_000m, Category = "Old Head",
            Date = new DateTime(2026, 1, 5), Description = "corrected note"
        });

        Assert.Equal("Old Head", edited.Category);
        Assert.Null(edited.CategoryId);
        Assert.Equal(0m, edited.WhtAmount);
    }

    // ── Reporting uses the status the tax was withheld under ────────────────────

    [Fact]
    public async Task TheVendorStatement_ReportsTheFilerStatusEachDeductionWasMadeUnder()
    {
        await using var context = Seeded();
        var finance = Finance(context);
        var vendors = new VendorService(context);

        await finance.CreateExpenseAsync(new CreateExpenseDto
        { FinanceAccountId = 1, VendorId = 1, CategoryId = 1, Amount = 1_000_000m }, 1);

        var vendor = await vendors.GetByIdAsync(1);
        await vendors.UpdateAsync(1, new SaveVendorDto
        {
            Name = vendor.Name, Ntn = vendor.Ntn, FilerStatus = FilerStatus.NonFiler,
            IsActive = true, ConcurrencyToken = vendor.ConcurrencyToken
        });

        await finance.CreateExpenseAsync(new CreateExpenseDto
        { FinanceAccountId = 1, VendorId = 1, CategoryId = 1, Amount = 1_000_000m }, 1);

        var lines = await Wht(context).GetByVendorAsync(null, null);

        // One line per status, not one line at today's status: collapsing them would report a
        // filer deduction under a non-filer heading, which is not a statement FBR can accept.
        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.FilerStatus == FilerStatus.Filer && l.WhtAmount == 10_000m);
        Assert.Contains(lines, l => l.FilerStatus == FilerStatus.NonFiler && l.WhtAmount == 20_000m);
        // Identity still comes from the vendor record — only the status is a snapshot.
        Assert.All(lines, l => Assert.Equal("1234567-8", l.Ntn));
    }

    private static AppDbContext Seeded()
    {
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        context.FinanceAccounts.Add(new FinanceAccount
        {
            Id = 1, Name = "Main Bank", Type = FinanceAccountType.Bank,
            AccountHolderName = "Seven Ventures", OpeningBalance = 0m, IsActive = true
        });
        context.FinanceSettings.Add(new FinanceSetting { Id = 1, FinancialYearStartMonth = 7 });
        context.Vendors.Add(new Vendor
        {
            Id = 1, Name = "ABC Traders", Ntn = "1234567-8", FilerStatus = FilerStatus.Filer, IsActive = true,
            RowVersion = [1]
        });
        context.ExpenseCategories.AddRange(
            Category(1, "Cement", "cement", "153(1)(a)", 1m, 2m, 75_000m),
            Category(2, "Bricks & Blocks", "bricks", "153(1)(a)", 5m, 10m, 75_000m),
            Category(3, "Transport & Freight", "transport", "153(1)(b)", 6m, 12m, 30_000m));
        context.SaveChanges();
        return context;
    }

    private static ExpenseCategory Category(
        int id, string name, string code, string section, decimal filer, decimal nonFiler, decimal threshold) => new()
    {
        Id = id, Name = name, Code = code, TaxSection = section, IsWhtApplicable = true,
        FilerRate = filer, NonFilerRate = nonFiler, AnnualThreshold = threshold, IsActive = true,
        // SQL Server generates rowversion; the in-memory provider does not, so seed one or the
        // concurrency token the update path requires would come back empty.
        RowVersion = [1]
    };

    private static FinanceService Finance(AppDbContext context)
    {
        var accounts = new FinanceAccountService(context);
        return new FinanceService(context, new NoopStorage(), accounts,
            new WhtService(context, accounts), NullLogger<FinanceService>.Instance);
    }

    private static WhtService Wht(AppDbContext context) => new(context, new FinanceAccountService(context));

    private sealed class NoopStorage : DAMS.Application.Interfaces.IFinanceAttachmentStorage
    {
        public Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default) => Task.FromResult("unused");
        public Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(null);
        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
