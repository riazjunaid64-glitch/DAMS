using DAMS.Api.Controllers;
using DAMS.Application.Common;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// What the Finance dashboard claims about itself: that a card's drill-down explains the card, that
/// the chart under the cards covers the same period they do, and that a selected account's balance
/// is the same number the account ledger and the Balance Sheet report for it.
/// </summary>
public sealed class FinanceDashboardIntegrityTests
{
    private static readonly DateTime PeriodStart = new(2026, 8, 1);
    private static readonly DateTime PeriodEnd = new(2026, 8, 31);

    /// <summary>
    /// The Total Expenses card is ordinary expenses PLUS commissions, rebates, non-cash customer
    /// credits, loan interest and fixed assets, each net of its reversals. Clicking it used to open
    /// the expense table, which holds only the first of those — so the operator was handed a list
    /// that could not add up to the figure they had just clicked, with the difference simply absent.
    /// </summary>
    [Fact]
    public async Task TheTotalExpensesDrillDown_AddsUpToTheCard_AcrossEveryComponentOfIt()
    {
        await using var context = Context();
        var world = await SeedEveryCostComponentAsync(context);
        var service = Finance(context);

        var summary = await service.GetSummaryAsync(null, PeriodStart, PeriodEnd);
        var breakdown = await service.GetCostBreakdownPageAsync(null, PeriodStart, PeriodEnd, 0, 200);

        // 1,000,000 expenses + 200,000 commission − 50,000 reversal + 80,000 cash rebate
        // − 30,000 reversal + 60,000 credit − 10,000 reversal + 50,000 interest + 500,000 asset.
        Assert.Equal(1_800_000m, summary.TotalExpenses);
        Assert.Equal(summary.TotalExpenses, breakdown.Items.Sum(i => i.Amount));
        Assert.False(breakdown.HasMore);
        Assert.Equal(9, breakdown.Items.Count);

        // Two kinds only, and reversals are the negative one — a row the reader has to know to
        // subtract by hand is a row that will be added instead.
        Assert.All(breakdown.Items, i => Assert.True(i.Kind is "cost" or "reduction", "unexpected kind: " + i.Kind));
        Assert.Equal(-50_000m, Assert.Single(breakdown.Items, i => i.Label == "Commission payout reversal").Amount);
        Assert.Equal(500_000m, Assert.Single(breakdown.Items, i => i.Label.Contains("Server rack")).Amount);
        Assert.Equal(50_000m, Assert.Single(breakdown.Items, i => i.Label == "Loan Interest").Amount);

        // Only the ordinary expense can be opened and corrected; the rest are recorded by their own
        // workflows, and offering an edit affordance on them would be a lie.
        Assert.Equal(world.ExpenseId, Assert.Single(breakdown.Items, i => i.ExpenseId != null).ExpenseId);

        // And the reason this exists: the expense table alone is 800,000 short of its own heading.
        var expensesOnly = await service.GetExpensePageAsync(null, PeriodStart, PeriodEnd, 0, 200);
        Assert.Equal(1_000_000m, expensesOnly.Items.Sum(i => i.Amount));
    }

    /// <summary>
    /// The bars and the cards must answer for the same period. Every day of the range belongs to
    /// exactly one bucket, so the bars total the cards exactly.
    /// </summary>
    [Fact]
    public async Task TheTrendCoversEveryDayOfTheRange_AndTotalsTheCards()
    {
        await using var context = Context();
        await SeedEveryCostComponentAsync(context);
        var service = Finance(context);

        var dashboard = await service.GetDashboardAsync(null, PeriodStart, PeriodEnd);

        Assert.Equal(dashboard.Summary.TotalRevenue, dashboard.Trend.Sum(b => b.Revenue));
        Assert.Equal(dashboard.Summary.TotalExpenses, dashboard.Trend.Sum(b => b.Expense));
        AssertCoversExactly(dashboard.Trend, PeriodStart, PeriodEnd);

        // And the pie is the revenue card, split up — not a separate reading of it.
        Assert.Equal(dashboard.Summary.TotalRevenue, dashboard.Distribution.Sum(s => s.Revenue));
    }

    /// <summary>
    /// A long range is AGGREGATED into wider bars, never sampled down to every n-th bar. Sampling
    /// left whole quarters out of a chart sitting under a card that covered them, so the picture and
    /// the total described different periods and nothing on screen said so.
    /// </summary>
    [Fact]
    public async Task ALongRange_IsAggregatedIntoWiderBars_AndLosesNothing()
    {
        await using var context = Context();
        var bank = new FinanceAccount { Name = "Bank", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank, IsActive = true };
        var head = new RevenueCategory { Name = "Other Income", Code = "other_income", DisplayOrder = 10 };
        context.AddRange(bank, head);
        await context.SaveChangesAsync();
        // One entry in every quarter of four years — sixteen periods, every one of them non-zero.
        for (var year = 2023; year <= 2026; year++)
            foreach (var month in new[] { 2, 5, 8, 11 })
                context.ManualRevenues.Add(new ManualRevenue
                {
                    FinanceAccountId = bank.Id, RevenueCategoryId = head.Id, RevenueType = "Other Income",
                    RevenueTypeName = "Other Income", Amount = 100_000m, Date = new DateTime(year, month, 15)
                });
        await context.SaveChangesAsync();

        var from = new DateTime(2023, 1, 1);
        var to = new DateTime(2026, 12, 31);
        var dashboard = await Finance(context).GetDashboardAsync(null, from, to);

        Assert.True(dashboard.Trend.Count <= 12, $"{dashboard.Trend.Count} buckets is more than a chart can show");
        AssertCoversExactly(dashboard.Trend, from, to);
        // Every rupee still on the chart. Under sampling, half the quarters were simply not drawn.
        Assert.Equal(1_600_000m, dashboard.Summary.TotalRevenue);
        Assert.Equal(1_600_000m, dashboard.Trend.Sum(b => b.Revenue));
        Assert.DoesNotContain(dashboard.Trend, b => b.Revenue == 0m);
    }

    /// <summary>
    /// One end of a custom range alone, or a From after its To, is refused rather than interpreted.
    /// Both used to be accepted by the cards and quietly re-read by the chart — as "all time" and as
    /// the financial year respectively — so the screen answered two questions at once in silence.
    /// </summary>
    [Fact]
    public void AHalfOpenOrBackwardsRange_IsRefused_NotReinterpreted()
    {
        var fromOnly = Assert.Throws<InvalidOperationException>(
            () => FinanceService.EnsureFilterRange(PeriodStart, null));
        Assert.Contains("both a From and a To", fromOnly.Message);
        Assert.Throws<InvalidOperationException>(() => FinanceService.EnsureFilterRange(null, PeriodEnd));

        var backwards = Assert.Throws<InvalidOperationException>(
            () => FinanceService.EnsureFilterRange(PeriodEnd, PeriodStart));
        Assert.Contains("cannot be after", backwards.Message);

        // Both ends, or neither, are the two usable states.
        FinanceService.EnsureFilterRange(PeriodStart, PeriodEnd);
        FinanceService.EnsureFilterRange(null, null);
        FinanceService.EnsureFilterRange(PeriodStart, PeriodStart);
    }

    /// <summary>
    /// The three places DAMS states an account's balance have to state the same number. The dashboard
    /// used to compute its own, out of a list of movements that knew about payments, expenses, assets,
    /// loans in cash and staff floats — and nothing about partner capital. A bank holding nothing but
    /// a partner's contribution therefore read as zero on this screen while the account ledger and the
    /// Balance Sheet both read five million.
    /// </summary>
    [Fact]
    public async Task ASelectedAccountsBalance_MatchesTheLedgerAndTheBalanceSheet_IncludingPartnerCapital()
    {
        await using var context = Context();
        var bank = new FinanceAccount { Name = "HBL", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank, IsActive = true };
        var equity = new FinanceAccount { Name = "A Capital", AccountHolderName = "A", Type = FinanceAccountType.Capital, IsActive = true };
        var partner = new CapitalPartner { Name = "A", ProfitSharePercent = 100m, FinanceAccount = equity };
        context.AddRange(bank, partner);
        await context.SaveChangesAsync();
        context.CapitalTransactions.AddRange(
            new CapitalTransaction
            {
                CapitalPartnerId = partner.Id, Type = CapitalTransactionType.Contribution,
                Amount = 5_000_000m, Date = new DateTime(2026, 8, 5), FinanceAccountId = bank.Id
            },
            new CapitalTransaction
            {
                CapitalPartnerId = partner.Id, Type = CapitalTransactionType.Withdrawal,
                Amount = 1_000_000m, Date = new DateTime(2026, 8, 20), FinanceAccountId = bank.Id
            });
        await context.SaveChangesAsync();
        var service = Finance(context);
        var accounts = new FinanceAccountService(context);

        var summary = await service.GetSummaryAsync(null, PeriodStart, PeriodEnd, bank.Id);
        var ledger = await accounts.GetByIdAsync(bank.Id);
        var sheet = await service.GetBalanceSheetAsync(null, PeriodEnd);
        var sheetLine = sheet.AssetGroups.SelectMany(g => g.Lines).Single(l => l.AccountId == bank.Id);

        Assert.Equal(4_000_000m, summary.AccountCurrentBalance);
        Assert.Equal(4_000_000m, ledger.CurrentBalance);
        Assert.Equal(4_000_000m, sheetLine.Amount);
        // The cash MOVEMENT figure beside it was missing the same two transactions.
        Assert.Equal(4_000_000m, summary.AccountNetMovement);
        Assert.True(sheet.IsBalanced);
    }

    /// <summary>
    /// An opening balance dated 1 August is not part of what an account held on 31 July. The formal
    /// reports have always known that; the dashboard did not, so the same card could show an opening
    /// balance the current balance beside it correctly excluded.
    /// </summary>
    [Fact]
    public async Task ASelectedAccountsOpeningBalance_AppearsOnlyOnceTheGoLiveDateIsReached()
    {
        await using var context = Context();
        var bank = new FinanceAccount
        {
            Name = "HBL", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank,
            IsActive = true, OpeningBalance = 10_000_000m
        };
        context.Add(bank);
        await context.SaveChangesAsync();
        context.OpeningBalanceSets.Add(new OpeningBalanceSet
        {
            AsAtDate = new DateTime(2026, 8, 1), IsCommitted = true, CommittedAt = new DateTime(2026, 8, 1, 6, 0, 0, DateTimeKind.Utc)
        });
        await context.SaveChangesAsync();
        var service = Finance(context);

        var before = await service.GetSummaryAsync(null, new DateTime(2026, 7, 1), new DateTime(2026, 7, 31), bank.Id);
        var onTheDay = await service.GetSummaryAsync(null, new DateTime(2026, 8, 1), new DateTime(2026, 8, 1), bank.Id);
        var after = await service.GetSummaryAsync(null, PeriodStart, PeriodEnd, bank.Id);

        Assert.Equal(0m, before.AccountCurrentBalance);
        Assert.Equal(0m, before.AccountOpeningBalance);
        Assert.Equal(10_000_000m, onTheDay.AccountCurrentBalance);
        Assert.Equal(10_000_000m, onTheDay.AccountOpeningBalance);
        Assert.Equal(10_000_000m, after.AccountCurrentBalance);

        // The formal report says the same thing on the same three dates, which is the point.
        var julySheet = await service.GetBalanceSheetAsync(null, new DateTime(2026, 7, 31));
        Assert.DoesNotContain(julySheet.AssetGroups.SelectMany(g => g.Lines), l => l.AccountId == bank.Id && l.Amount != 0m);
        var augustSheet = await service.GetBalanceSheetAsync(null, new DateTime(2026, 8, 1));
        Assert.Equal(10_000_000m, augustSheet.AssetGroups.SelectMany(g => g.Lines).Single(l => l.AccountId == bank.Id).Amount);
    }

    /// <summary>
    /// A bank account is not a business unit, and the cards must stop pretending it is.
    /// <para>
    /// A sale is recognised at possession and moves no cash, so it belongs to no bank account. With
    /// a bank selected, that revenue vanished from the cards while every cost paid out of the bank
    /// stayed — and the difference was still printed under the heading "Net Profit". Here the sale
    /// is 5,000,000 and the bank paid 1,750,000 of costs: the old screen reported a 1,750,000 LOSS
    /// for the account that had funded a 3,200,000 profit.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ABankFilter_ReportsWhatThatAccountHolds_AndRefusesToCallItNetProfit()
    {
        await using var context = Context();
        var world = await SeedEveryCostComponentAsync(context);
        var service = Finance(context);

        var business = await service.GetSummaryAsync(null, PeriodStart, PeriodEnd);
        var onTheBank = await service.GetSummaryAsync(null, PeriodStart, PeriodEnd, world.BankId);

        // Unfiltered, the period is profitable and Net Profit is reported.
        Assert.False(business.AccountFilterApplied);
        Assert.Equal(5_000_000m, business.TotalRevenue);
        Assert.Equal(1_800_000m, business.TotalExpenses);
        Assert.Equal(3_200_000m, business.NetProfit);

        // Filtered to the bank: the recognised sale is correctly absent (it never touched the bank),
        // the bank's own costs are all there — and NO profit figure is offered for the difference.
        Assert.True(onTheBank.AccountFilterApplied);
        Assert.Equal(0m, onTheBank.TotalRevenue);
        Assert.Equal(0m, onTheBank.AutomaticRevenue);
        Assert.Equal(1_750_000m, onTheBank.TotalExpenses);
        Assert.Null(onTheBank.NetProfit);
        // What the account CAN answer, in its place.
        Assert.NotNull(onTheBank.AccountNetMovement);

        // Balances that belong to bookings rather than accounts stay suppressed, as before.
        Assert.Equal(0m, onTheBank.CustomerDepositsBalance);
        Assert.Equal(0m, onTheBank.OutstandingAmount);

        // And the drill-down is refused rather than answered with a table that cannot add up to a
        // figure the cards no longer report.
        var controller = new FinanceController(service);
        var refused = Assert.IsType<BadRequestObjectResult>(await controller.GetRows(
            "netProfit", null, PeriodStart, PeriodEnd, world.BankId.ToString(), 0, 100));
        Assert.Contains("not for a single", refused.Value!.ToString(), StringComparison.OrdinalIgnoreCase);
        // Unfiltered, the same drill-down still works and still totals the card.
        var allowed = Assert.IsType<OkObjectResult>(await controller.GetRows(
            "netProfit", null, PeriodStart, PeriodEnd, null, 0, 100));
        var rows = Assert.IsType<PagedResult<NetProfitLineDto>>(allowed.Value);
        Assert.Equal(business.NetProfit, rows.Items.Sum(i => i.Amount));
    }

    /// <summary>
    /// A date the database cannot store, or a To date whose exclusive end cannot be formed at all,
    /// is a bad request — not a 500. Both used to reach the query builder: the low end came back as
    /// a SQL conversion failure, and 31 Dec 9999 threw an ArgumentOutOfRangeException out of
    /// <c>AddDays(1)</c> before any SQL was even generated.
    /// </summary>
    [Fact]
    public async Task ADateOutsideWhatCanBeStored_IsRefused_NotFaulted()
    {
        await using var context = Context();
        await SeedEveryCostComponentAsync(context);
        var service = Finance(context);
        var controller = new FinanceController(service);

        var tooEarly = new DateTime(1752, 12, 31);
        var tooLate = DateTime.MaxValue.Date; // 31 Dec 9999 — To + 1 day does not exist.

        Assert.Contains("1753", Assert.Throws<InvalidOperationException>(
            () => FinanceService.EnsureFilterRange(tooEarly, PeriodEnd)).Message);
        Assert.Contains("9999", Assert.Throws<InvalidOperationException>(
            () => FinanceService.EnsureFilterRange(PeriodStart, tooLate)).Message);
        // The last day that CAN be answered for is accepted.
        FinanceService.EnsureFilterRange(FinanceService.MinFilterDate, FinanceService.MaxFilterDate);

        // Through the controller, on all three endpoints the screen uses, as a 400 with a message.
        foreach (var result in new IActionResult[]
        {
            await controller.GetSummary(null, PeriodStart, tooLate, null, default),
            await controller.GetDashboard(null, PeriodStart, tooLate, null, default),
            await controller.GetRows("revenue", null, tooEarly, PeriodEnd, null, 0, 100)
        })
        {
            Assert.IsType<BadRequestObjectResult>(result);
        }

        // And the service itself refuses rather than throwing arithmetic or handing SQL Server a
        // date it cannot convert: a caller that skips the controller still gets a message an
        // operator could act on. BOTH ends, because ExclusiveEnd only sees the upper one.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetSummaryAsync(null, PeriodStart, tooLate));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetSummaryAsync(null, tooEarly, PeriodEnd));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetDashboardAsync(null, PeriodStart, tooLate));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetDashboardAsync(null, tooEarly, PeriodEnd));

        // An open-ended balance-as-at-a-date call is still legitimate and still works: the bounds
        // rule is not the both-or-neither rule, and conflating them would break it.
        Assert.NotNull(await service.GetSummaryAsync(null, null, PeriodEnd));
    }

    /// <summary>
    /// Customer Deposits is a BALANCE as at the end of the range, not the deposits taken during it.
    /// The card sits in a row of period totals, so the distinction has to be provable: here August
    /// takes no deposits at all and releases a million, yet the August card correctly reads 300,000
    /// — money received in July and still held.
    /// </summary>
    [Fact]
    public async Task CustomerDeposits_IsTheBalanceAtThePeriodEnd_NotThePeriodsMovement()
    {
        await using var context = Context();
        var world = await SeedEveryCostComponentAsync(context);

        // A second booking that never reaches possession, so its deposit is still held.
        var project = await context.Projects.SingleAsync();
        var unit = new Unit { ProjectId = project.Id, UnitNumber = "U-2", UnitType = "Apartment", Price = 2_000_000m, Status = UnitStatus.OnPaymentPlan };
        var customer = new Customer { FullName = "Second Buyer", Phone = "03003334444", Status = CustomerStatus.Active };
        context.AddRange(unit, customer);
        await context.SaveChangesAsync();
        var held = new Booking
        {
            BookingReference = "BK-DASH-2", CustomerId = customer.Id, UnitId = unit.Id,
            Status = BookingStatus.PaymentPlanActive, Source = CustomerSource.Referral,
            AgreedSalePrice = 2_000_000m, DiscountAmount = 0m, BookingDate = new DateTime(2026, 7, 1)
        };
        context.Bookings.Add(held);
        await context.SaveChangesAsync();

        // Both deposits are taken in JULY. The first booking's possession is 2 August, which
        // releases its million; the second is still held.
        var recognised = await context.Bookings.SingleAsync(b => b.BookingReference == "BK-DASH-1");
        context.Payments.AddRange(
            new Payment
            {
                BookingId = recognised.Id, FinanceAccountId = world.BankId, Amount = 1_000_000m,
                Type = PaymentType.BookingAmount, PaidAt = new DateTime(2026, 7, 5),
                CreatedAt = new DateTime(2026, 7, 5, 6, 0, 0, DateTimeKind.Utc)
            },
            new Payment
            {
                BookingId = held.Id, FinanceAccountId = world.BankId, Amount = 300_000m,
                Type = PaymentType.BookingAmount, PaidAt = new DateTime(2026, 7, 6),
                CreatedAt = new DateTime(2026, 7, 6, 6, 0, 0, DateTimeKind.Utc)
            });
        await context.SaveChangesAsync();
        var service = Finance(context);

        var july = await service.GetSummaryAsync(null, new DateTime(2026, 7, 1), new DateTime(2026, 7, 31));
        var august = await service.GetSummaryAsync(null, PeriodStart, PeriodEnd);
        var june = await service.GetSummaryAsync(null, new DateTime(2026, 6, 1), new DateTime(2026, 6, 30));

        // Both still held at the end of July.
        Assert.Equal(1_300_000m, july.CustomerDepositsBalance);
        // August: nothing was received and a million was released, so a period-movement reading
        // would show 0 or −1,000,000. The balance still standing is 300,000 of July's money.
        Assert.Equal(300_000m, august.CustomerDepositsBalance);
        // Before either payment, nothing.
        Assert.Equal(0m, june.CustomerDepositsBalance);

        // The From date has nothing to do with it: the same To date gives the same balance whatever
        // the range starts at, which is exactly why the card is labelled "at period end".
        var longRange = await service.GetSummaryAsync(null, new DateTime(2026, 1, 1), PeriodEnd);
        Assert.Equal(august.CustomerDepositsBalance, longRange.CustomerDepositsBalance);
    }

    /// <summary>
    /// The Balance Sheet's disclosed fixed-asset charge belongs to the BALANCE SHEET's own window,
    /// and nothing may present it as a reconciliation against an arbitrary P&amp;L.
    /// <para>
    /// Retained Profit accumulates from the opening baseline to the as-at date. A P&amp;L is run for
    /// whatever period the operator picked. Subtracting one from the other is arithmetic across two
    /// different questions and only happens to work when the two windows coincide — which is why
    /// this test pins both cases: the identity holds for matching windows, and is expected NOT to
    /// hold for mismatched ones.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheDisclosedFixedAssetCharge_BelongsToTheSheetsOwnWindow_NotToAnyPnl()
    {
        await using var context = Context();
        await SeedEveryCostComponentAsync(context);
        var service = Finance(context);

        var asAt = new DateTime(2026, 9, 30);
        var sheet = await service.GetBalanceSheetAsync(null, asAt);

        // No committed baseline here, so the sheet's window runs from the beginning of the records.
        Assert.Null(sheet.RetainedProfitStart);
        // Scoped to THAT window: the 500,000 server rack bought in August is inside it.
        Assert.Equal(500_000m, sheet.UnpostedFixedAssetCharge);

        // Matching windows: the identity holds, and this is the only case in which it means anything.
        var matching = await service.GetProfitAndLossAsync(null, new DateTime(2026, 1, 1), asAt);
        Assert.Equal(matching.NetProfit, sheet.RetainedProfit - sheet.UnpostedFixedAssetCharge);

        // Mismatched windows: September on its own recognised nothing and bought nothing, so its Net
        // Profit is zero while the sheet still discloses August's purchase. Subtracting one from the
        // other produces a number that describes nothing, which is why no screen, DTO or export
        // presents the disclosure as a gap against "the P&L".
        var september = await service.GetProfitAndLossAsync(null, new DateTime(2026, 9, 1), asAt);
        Assert.Equal(0m, september.NetProfit);
        Assert.NotEqual(september.NetProfit, sheet.RetainedProfit - sheet.UnpostedFixedAssetCharge);

        // The export says which window the charge belongs to, and does not claim the statements
        // reconcile — the earlier wording asserted "Retained Profit is higher than P&L Net Profit",
        // which is a comparison against a period the reader never chose.
        var export = SheetXml(await service.ExportBalanceSheetAsync(null, asAt));
        Assert.Contains("open accountant decision", export, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("the window Retained Profit above covers", export, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("higher than", export, StringComparison.OrdinalIgnoreCase);
    }

    private static string SheetXml(DTOs.FinanceDtos.FinanceExportDto export)
    {
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(export.Content));
        using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        return reader.ReadToEnd();
    }

    /// <summary>Contiguous, gapless, non-overlapping, and clipped to the requested range — the
    /// property that makes "the bars total the cards" true rather than lucky.</summary>
    private static void AssertCoversExactly(
        IReadOnlyList<DTOs.FinanceDtos.FinanceTrendBucketDto> buckets, DateTime from, DateTime to)
    {
        Assert.NotEmpty(buckets);
        Assert.Equal(from, buckets[0].From);
        Assert.Equal(to, buckets[^1].To);
        for (var i = 0; i < buckets.Count; i++)
        {
            Assert.True(buckets[i].From <= buckets[i].To, $"bucket {buckets[i].Label} ends before it starts");
            if (i > 0) Assert.Equal(buckets[i - 1].To.AddDays(1), buckets[i].From);
        }
    }

    private sealed record CostWorld(int ExpenseId, int BankId);

    /// <summary>
    /// One of every component the Total Expenses card adds up, each with a reversal where it can have
    /// one, seeded as domain rows so the drill-down and the card are provably reading the same data.
    /// </summary>
    private static async Task<CostWorld> SeedEveryCostComponentAsync(AppDbContext context)
    {
        var bank = new FinanceAccount { Name = "HBL", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank, IsActive = true };
        var equipment = new FinanceAccount { Name = "Office Equipment", AccountHolderName = "DAMS", Type = FinanceAccountType.FixedAsset, IsActive = true };
        var loanAccount = new FinanceAccount { Name = "Term Loan", AccountHolderName = "DAMS", Type = FinanceAccountType.Liability, IsActive = true };
        var project = new Project { ProjectName = "Dashboard Integrity", Location = "Karachi", CreatedById = 1 };
        var unit = new Unit { Project = project, UnitNumber = "U-1", UnitType = "Apartment", Price = 5_000_000m, Status = UnitStatus.Sold };
        var customer = new Customer { FullName = "Buyer", Phone = "03001112222", Status = CustomerStatus.Active };
        var booking = new Booking
        {
            BookingReference = "BK-DASH-1", Customer = customer, Unit = unit,
            Status = BookingStatus.PossessionGiven, Source = CustomerSource.Referral,
            AgreedSalePrice = 5_000_000m, DiscountAmount = 0m, BookingAmountRequired = 1_000_000m,
            BookingAmountReceived = 1_000_000m, BookingDate = new DateTime(2026, 7, 1)
        };
        var partner = new ThirdPartyPartner { Name = "Broker", PartnerType = "Broker", InternalCode = "BR-1" };
        context.AddRange(bank, equipment, loanAccount, project, unit, customer, booking, partner);
        await context.SaveChangesAsync();

        // Recognised at possession — the credits below are only a cost once the sale is income.
        context.BookingSaleRecognitions.Add(new BookingSaleRecognition
        {
            BookingId = booking.Id, RecognitionDate = new DateTime(2026, 8, 2),
            NetSaleValue = 5_000_000m, RecognizedAt = new DateTime(2026, 8, 2, 6, 0, 0, DateTimeKind.Utc)
        });
        var expense = new Expense
        {
            FinanceAccountId = bank.Id, Category = "Office Rent", Amount = 1_000_000m,
            Date = new DateTime(2026, 8, 3)
        };
        context.Expenses.Add(expense);
        var commission = new BookingCommission
        {
            BookingId = booking.Id, PartnerId = partner.Id, PartnerNameSnapshot = "Broker",
            PartnerTypeSnapshot = "Broker", PartnerInternalCodeSnapshot = "BR-1",
            BasisAmount = 5_000_000m, CalculatedAmount = 200_000m, FinalAmount = 200_000m
        };
        var rebate = new CustomerRebate
        {
            BookingId = booking.Id, CustomerId = customer.Id, BasisAmount = 5_000_000m,
            CalculatedAmount = 140_000m, FinalAmount = 140_000m, Reason = "Goodwill",
            Method = CustomerRebateMethod.CashOrBankPayment, Status = CustomerRebateStatus.Approved
        };
        var loan = new Loan { Name = "Working capital", FinanceAccountId = loanAccount.Id };
        context.AddRange(commission, rebate, loan);
        await context.SaveChangesAsync();

        var payout = new CommissionPayout
        {
            CommissionId = commission.Id, FinanceAccountId = bank.Id, Amount = 200_000m,
            PaymentDate = new DateTime(2026, 8, 6), IdempotencyKey = "payout-1"
        };
        var cashRebate = new RebateDisbursement
        {
            RebateId = rebate.Id, FinanceAccountId = bank.Id, Method = CustomerRebateMethod.CashOrBankPayment,
            Amount = 80_000m, AppliedAt = new DateTime(2026, 8, 8), IdempotencyKey = "reb-cash-1"
        };
        var creditNote = new RebateDisbursement
        {
            RebateId = rebate.Id, Method = CustomerRebateMethod.CreditNote,
            Amount = 60_000m, AppliedAt = new DateTime(2026, 8, 9), IdempotencyKey = "reb-credit-1"
        };
        context.AddRange(payout, cashRebate, creditNote);
        await context.SaveChangesAsync();

        context.CommissionPayoutReversals.Add(new CommissionPayoutReversal
        {
            PayoutId = payout.Id, Amount = 50_000m, Reason = "Overpaid",
            ReversedAt = new DateTime(2026, 8, 12), IdempotencyKey = "payout-rev-1"
        });
        context.RebateDisbursementReversals.AddRange(
            new RebateDisbursementReversal
            {
                DisbursementId = cashRebate.Id, Amount = 30_000m, Reason = "Partially recovered",
                ReversedAt = new DateTime(2026, 8, 14), IdempotencyKey = "reb-cash-rev-1"
            },
            new RebateDisbursementReversal
            {
                DisbursementId = creditNote.Id, Amount = 10_000m, Reason = "Credit withdrawn",
                ReversedAt = new DateTime(2026, 8, 16), IdempotencyKey = "reb-credit-rev-1"
            });
        context.LoanTransactions.Add(new LoanTransaction
        {
            LoanId = loan.Id, Type = LoanTransactionType.Repayment, PrincipalAmount = 0m,
            InterestAmount = 50_000m, Date = new DateTime(2026, 8, 18), FinanceAccountId = bank.Id
        });
        context.AssetPurchases.Add(new AssetPurchase
        {
            AssetAccountId = equipment.Id, FinanceAccountId = bank.Id, Amount = 500_000m,
            ItemName = "Server rack", Category = "Equipment", Date = new DateTime(2026, 8, 20)
        });
        await context.SaveChangesAsync();
        return new CostWorld(expense.Id, bank.Id);
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
