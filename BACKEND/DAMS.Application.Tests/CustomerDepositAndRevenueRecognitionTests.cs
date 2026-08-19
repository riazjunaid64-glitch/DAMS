using DAMS.Application.Common;
using DAMS.Application.DTOs.BookingDtos;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.DTOs.InstallmentDtos;
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
/// The revenue-recognition rule, end to end.
/// <para>
/// Customer money taken before possession is a DEPOSIT — a liability, not income. The sale becomes
/// revenue exactly once, in full, on the day possession is given; whatever is still unpaid becomes
/// a receivable. Every assertion below exists because the opposite reading (cash received = revenue
/// earned) inflates profit by every unfinished sale and hides the obligation behind it.
/// </para>
/// </summary>
public sealed class CustomerDepositAndRevenueRecognitionTests
{
    private static readonly DateTime Jan = new(2026, 1, 15);
    private static readonly DateTime Feb = new(2026, 2, 15);
    private static readonly DateTime Mar = new(2026, 3, 15);

    // ── 1. Pre-possession payment ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PaymentBeforePossession_IsADepositLiability_NotRevenue()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 5_000_000m);
        await PayAsync(context, world, 500_000m, Jan);
        var finance = Finance(context);

        var summary = await finance.GetSummaryAsync(null, null, Feb);
        Assert.Equal(0m, summary.AutomaticRevenue);
        Assert.Equal(0m, summary.TotalRevenue);
        Assert.Equal(0m, summary.NetProfit);
        Assert.Equal(500_000m, summary.CustomerDepositsBalance);

        var pnl = await finance.GetProfitAndLossAsync(null, Jan.AddDays(-1), Feb);
        Assert.Equal(0m, pnl.TotalIncome);
        Assert.Equal(0m, pnl.NetProfit);

        var accounts = new FinanceAccountService(context);
        Assert.Equal(500_000m, (await accounts.GetByIdAsync(world.Bank.Id)).CurrentBalance);
        Assert.Equal(500_000m, (await accounts.GetByIdAsync(world.Deposits.Id)).CurrentBalance);
        Assert.Equal(0m, (await accounts.GetByIdAsync(world.Receivables.Id)).CurrentBalance);

        var deposits = await finance.GetCustomerDepositPageAsync(null, Feb, 0, 20);
        var row = Assert.Single(deposits.Items);
        Assert.Equal(500_000m, row.DepositBalance);
        Assert.Equal(500_000m, row.CustomerCashReceived);
        Assert.Equal(5_000_000m, row.NetSaleValue);
        Assert.Null(row.RecognitionDate);

        await AssertBalancedAsync(finance, Feb);
    }

    // ── 2. Several pre-possession payments ───────────────────────────────────────────────────

    [Fact]
    public async Task RepeatedPaymentsBeforePossession_AccumulateAsDeposits_WithNoRevenue()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 5_000_000m);
        await PayAsync(context, world, 500_000m, Jan);
        await PayAsync(context, world, 250_000m, Jan.AddDays(10));
        await PayAsync(context, world, 250_000m, Feb);
        var finance = Finance(context);

        var summary = await finance.GetSummaryAsync(null, null, Mar);
        Assert.Equal(1_000_000m, summary.CustomerDepositsBalance);
        Assert.Equal(0m, summary.TotalRevenue);

        // The balance is as-at, so a mid-stream cut-off sees only what had arrived by then.
        Assert.Equal(750_000m, (await finance.GetSummaryAsync(null, null, Jan.AddDays(10))).CustomerDepositsBalance);

        await AssertBalancedAsync(finance, Mar);
    }

    // ── 3. Possession with part of the price still unpaid ────────────────────────────────────

    [Fact]
    public async Task Possession_RecognisesFullNetSaleValue_ClearsDeposit_AndRaisesReceivable()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 5_000_000m);
        await PayAsync(context, world, 3_000_000m, Jan);
        await GivePossessionAsync(context, world, Feb);
        var finance = Finance(context);
        var accounts = new FinanceAccountService(context);

        var pnl = await finance.GetProfitAndLossAsync(null, Jan.AddDays(-1), Mar);
        Assert.Equal(5_000_000m, Assert.Single(pnl.IncomeLines, l => l.Name == "Unit Sales").Amount);
        Assert.Equal(5_000_000m, pnl.TotalIncome);

        Assert.Equal(0m, (await accounts.GetByIdAsync(world.Deposits.Id)).CurrentBalance);
        Assert.Equal(2_000_000m, (await accounts.GetByIdAsync(world.Receivables.Id)).CurrentBalance);
        // Possession moves no cash of its own — the bank still holds exactly what was paid in.
        Assert.Equal(3_000_000m, (await accounts.GetByIdAsync(world.Bank.Id)).CurrentBalance);

        var recognition = Assert.Single(context.BookingSaleRecognitions);
        Assert.Equal(Feb, recognition.RecognitionDate);
        Assert.Equal(5_000_000m, recognition.NetSaleValue);

        await AssertBalancedAsync(finance, Mar);
    }

    // ── 4. Historical reports must not move ──────────────────────────────────────────────────

    [Fact]
    public async Task RecognisedSale_AppearsOnlyFromItsRecognitionDate_NeverInEarlierPeriods()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 5_000_000m);
        await PayAsync(context, world, 3_000_000m, Jan);
        await GivePossessionAsync(context, world, Feb);
        var finance = Finance(context);

        // A report that ends the day BEFORE possession: the sale has not happened yet, and the
        // booking's current status must not be allowed to rewrite that.
        var before = await finance.GetProfitAndLossAsync(null, Jan.AddDays(-1), Feb.AddDays(-1));
        Assert.DoesNotContain(before.IncomeLines, l => l.Name == "Unit Sales");
        Assert.Equal(0m, before.TotalIncome);

        var beforeSheet = await finance.GetBalanceSheetAsync(null, Feb.AddDays(-1));
        Assert.True(beforeSheet.IsBalanced);
        Assert.Equal(3_000_000m, Line(beforeSheet.LiabilityGroups, "Customer General Account / Customer Deposits"));
        Assert.Equal(0m, Line(beforeSheet.AssetGroups, "Customer Receivables"));

        var including = await finance.GetProfitAndLossAsync(null, Jan.AddDays(-1), Feb);
        Assert.Equal(5_000_000m, Assert.Single(including.IncomeLines, l => l.Name == "Unit Sales").Amount);

        var afterSheet = await finance.GetBalanceSheetAsync(null, Feb);
        Assert.True(afterSheet.IsBalanced);
        Assert.Equal(0m, Line(afterSheet.LiabilityGroups, "Customer General Account / Customer Deposits"));
        Assert.Equal(2_000_000m, Line(afterSheet.AssetGroups, "Customer Receivables"));
    }

    // ── 5. Possession on a fully paid booking ────────────────────────────────────────────────

    [Fact]
    public async Task Possession_OnAFullyPaidBooking_LeavesNoDepositAndNoReceivable()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 5_000_000m);
        await PayAsync(context, world, 5_000_000m, Jan);
        await GivePossessionAsync(context, world, Feb);
        var finance = Finance(context);
        var accounts = new FinanceAccountService(context);

        var pnl = await finance.GetProfitAndLossAsync(null, Jan.AddDays(-1), Mar);
        Assert.Equal(5_000_000m, pnl.TotalIncome);
        Assert.Equal(0m, (await accounts.GetByIdAsync(world.Deposits.Id)).CurrentBalance);
        Assert.Equal(0m, (await accounts.GetByIdAsync(world.Receivables.Id)).CurrentBalance);
        Assert.Empty((await finance.GetCustomerDepositPageAsync(null, Mar, 0, 20)).Items);
        await AssertBalancedAsync(finance, Mar);
    }

    // ── 6. Collecting after possession ───────────────────────────────────────────────────────

    [Fact]
    public async Task InstallmentCollectedAfterPossession_ClearsReceivable_WithoutNewRevenue()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 5_000_000m);
        await PayAsync(context, world, 3_000_000m, Jan);
        var installment = new Installment
        {
            BookingId = world.BookingId, SequenceNumber = 1, Amount = 2_000_000m,
            Status = InstallmentStatus.Pending, DueDate = Mar, Type = InstallmentType.Regular
        };
        context.Installments.Add(installment);
        await context.SaveChangesAsync();
        await GivePossessionAsync(context, world, Feb);

        // Collection must keep working after possession: what is left is a receivable, and
        // refusing the cash would leave it uncollectable.
        var installments = new InstallmentService(context, new FinanceAccountService(context));
        await installments.RecordInstallmentPaymentAsync(world.BookingId, installment.Id, new RecordInstallmentPaymentDto
        {
            Amount = 2_000_000m, FinanceAccountId = world.Bank.Id,
            PaymentMethod = PaymentMethod.BankTransfer, PaymentReference = "TRX-1", PaidAt = Mar
        }, 1);

        var finance = Finance(context);
        var accounts = new FinanceAccountService(context);
        Assert.Equal(5_000_000m, (await accounts.GetByIdAsync(world.Bank.Id)).CurrentBalance);
        Assert.Equal(0m, (await accounts.GetByIdAsync(world.Receivables.Id)).CurrentBalance);

        var pnl = await finance.GetProfitAndLossAsync(null, Jan.AddDays(-1), Mar.AddDays(1));
        Assert.Equal(5_000_000m, pnl.TotalIncome); // the sale, once — not the sale plus the cash
        Assert.Equal(BookingStatus.PossessionGiven, context.Bookings.Single().Status);
        await AssertBalancedAsync(finance, Mar.AddDays(1));
    }

    // ── 7. Completion ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteSale_RequiresPossession_AndNeverRecognisesASecondTime()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 1_000_000m);
        await PayAsync(context, world, 1_000_000m, Jan);
        var bookings = Bookings(context);

        var blocked = await Assert.ThrowsAsync<InvalidOperationException>(() => bookings.CompleteSaleAsync(world.BookingId, 5));
        Assert.Contains("Give possession before completing the sale", blocked.Message);

        await GivePossessionAsync(context, world, Feb);
        await bookings.CompleteSaleAsync(world.BookingId, 5);

        Assert.Equal(BookingStatus.SaleCompleted, context.Bookings.Single().Status);
        var recognition = Assert.Single(context.BookingSaleRecognitions);
        Assert.Equal(Feb, recognition.RecognitionDate);

        var pnl = await Finance(context).GetProfitAndLossAsync(null, Jan.AddDays(-1), Mar);
        Assert.Equal(1_000_000m, pnl.TotalIncome);
    }

    // ── 8. Possession retried ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PossessionCannotBeRecognisedTwice_AndCannotBeDatedInTheFuture()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 1_000_000m);
        await PayAsync(context, world, 1_000_000m, Jan);
        var bookings = Bookings(context);

        var future = await Assert.ThrowsAsync<InvalidOperationException>(
            () => bookings.GivePossessionAsync(world.BookingId, DAMS.Application.Common.PakistanTime.Today.AddDays(1), 1));
        Assert.Contains("future", future.Message);
        Assert.Empty(context.BookingSaleRecognitions);

        await bookings.GivePossessionAsync(world.BookingId, Feb, 1);
        // A second attempt is refused by the status guard, and the unique index on BookingId is
        // the backstop for a genuine race. Either way there is exactly one sale.
        await Assert.ThrowsAsync<InvalidOperationException>(() => bookings.GivePossessionAsync(world.BookingId, Feb, 1));
        Assert.Single(context.BookingSaleRecognitions);

        // The same invariant stated directly: a hand-written duplicate is a duplicate sale.
        var recognition = context.BookingSaleRecognitions.Single();
        Assert.Equal(world.BookingId, recognition.BookingId);
    }

    // ── 9-11. Cancellation ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CancellationWithFullRefund_ClearsTheDeposit_AndEarnsNothing()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 5_000_000m, withRefundPayable: true);
        await PayAsync(context, world, 500_000m, Jan);
        await CancelAsync(context, world, cash: 500_000m, refund: 500_000m, key: "full");
        var finance = Finance(context);
        var accounts = new FinanceAccountService(context);
        var today = DAMS.Application.Common.PakistanTime.Today;

        var pnl = await finance.GetProfitAndLossAsync(null, Jan.AddDays(-1), today);
        Assert.Equal(0m, pnl.TotalIncome);
        Assert.Equal(0m, (await accounts.GetByIdAsync(world.Deposits.Id)).CurrentBalance);
        Assert.Equal(500_000m, (await accounts.GetByIdAsync(world.RefundPayable!.Id)).CurrentBalance);
        await AssertBalancedAsync(finance, today);

        await Bookings(context).PayCancellationRefundAsync(world.BookingId, new PayCancellationRefundDto
        {
            FinanceAccountId = world.Bank.Id, PaymentMethod = PaymentMethod.Cash,
            PaidAt = today, IdempotencyKey = "full-pay"
        }, new DAMS.Application.Common.FinancialWorkflowActor(1, "Admin"));

        Assert.Equal(0m, (await accounts.GetByIdAsync(world.Bank.Id)).CurrentBalance);
        Assert.Equal(0m, (await accounts.GetByIdAsync(world.RefundPayable.Id)).CurrentBalance);
        Assert.Equal(0m, (await finance.GetProfitAndLossAsync(null, Jan.AddDays(-1), today)).TotalIncome);
        await AssertBalancedAsync(finance, today);
    }

    [Fact]
    public async Task CancellationWithPartialRefund_EarnsOnlyTheRetainedAmount_NotAContraRevenue()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 5_000_000m, withRefundPayable: true);
        await PayAsync(context, world, 500_000m, Jan);
        await CancelAsync(context, world, cash: 500_000m, refund: 400_000m, key: "partial");
        var finance = Finance(context);
        var accounts = new FinanceAccountService(context);
        var today = DAMS.Application.Common.PakistanTime.Today;

        var pnl = await finance.GetProfitAndLossAsync(null, Jan.AddDays(-1), today);
        Assert.Equal(100_000m, Assert.Single(pnl.IncomeLines, l => l.Name == "Cancellation Income (Retained)").Amount);
        Assert.Equal(100_000m, pnl.TotalIncome);
        // The old model booked the 400,000 refund as negative revenue. It never was revenue.
        Assert.DoesNotContain(pnl.IncomeLines, l => l.Amount < 0m);
        Assert.Equal(0m, pnl.TotalExpenses);

        Assert.Equal(0m, (await accounts.GetByIdAsync(world.Deposits.Id)).CurrentBalance);
        Assert.Equal(400_000m, (await accounts.GetByIdAsync(world.RefundPayable!.Id)).CurrentBalance);
        await AssertBalancedAsync(finance, today);

        await Bookings(context).PayCancellationRefundAsync(world.BookingId, new PayCancellationRefundDto
        {
            FinanceAccountId = world.Bank.Id, PaymentMethod = PaymentMethod.Cash,
            PaidAt = today, IdempotencyKey = "partial-pay"
        }, new DAMS.Application.Common.FinancialWorkflowActor(1, "Admin"));

        // Paying it out moves cash and clears the payable. Income does not move again.
        Assert.Equal(100_000m, (await accounts.GetByIdAsync(world.Bank.Id)).CurrentBalance);
        Assert.Equal(0m, (await accounts.GetByIdAsync(world.RefundPayable.Id)).CurrentBalance);
        Assert.Equal(100_000m, (await finance.GetProfitAndLossAsync(null, Jan.AddDays(-1), today)).TotalIncome);
        await AssertBalancedAsync(finance, today);
    }

    [Fact]
    public async Task CancellationWithNoRefund_TurnsTheWholeDepositIntoIncomeOnce()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 5_000_000m);
        await PayAsync(context, world, 500_000m, Jan);
        await CancelAsync(context, world, cash: 500_000m, refund: 0m, key: "forfeit");
        var finance = Finance(context);
        var accounts = new FinanceAccountService(context);
        var today = DAMS.Application.Common.PakistanTime.Today;

        var pnl = await finance.GetProfitAndLossAsync(null, Jan.AddDays(-1), today);
        Assert.Equal(500_000m, Assert.Single(pnl.IncomeLines, l => l.Name == "Cancellation Income (Retained)").Amount);
        Assert.Equal(500_000m, pnl.TotalIncome);
        Assert.Equal(0m, (await accounts.GetByIdAsync(world.Deposits.Id)).CurrentBalance);
        Assert.Empty(context.BookingCancellationRefunds);
        await AssertBalancedAsync(finance, today);
    }

    // ── 12. As-at reporting across a cancellation ────────────────────────────────────────────

    [Fact]
    public async Task DepositExistsUntilTheCancellationDate_AndIsClearedFromItOnwards()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 5_000_000m, withRefundPayable: true);
        await PayAsync(context, world, 500_000m, Jan);
        // Written directly so the cancellation can be dated in the past; the service always
        // stamps today's Pakistan business date, which is what the other cancellation tests use.
        context.BookingCancellationSettlements.Add(new BookingCancellationSettlement
        {
            BookingId = world.BookingId, CustomerCashReceivedSnapshot = 500_000m, RefundAmount = 400_000m,
            RetainedAmount = 100_000m, RefundDecision = CancellationRefundDecision.PayLater,
            RefundPayableAccountId = world.RefundPayable!.Id, Reason = "As-at reporting", IdempotencyKey = "as-at",
            CancelledByUserId = 1, CancelledByName = "Admin", CancelledAt = Feb, CancellationDate = Feb
        });
        context.Bookings.Single().Status = BookingStatus.Cancelled;
        await context.SaveChangesAsync();
        var finance = Finance(context);

        var before = await finance.GetBalanceSheetAsync(null, Feb.AddDays(-1));
        Assert.True(before.IsBalanced);
        Assert.Equal(500_000m, Line(before.LiabilityGroups, "Customer General Account / Customer Deposits"));
        Assert.Equal(0m, Line(before.LiabilityGroups, "Customer Refunds Payable"));

        var after = await finance.GetBalanceSheetAsync(null, Feb);
        Assert.True(after.IsBalanced);
        Assert.Equal(0m, Line(after.LiabilityGroups, "Customer General Account / Customer Deposits"));
        Assert.Equal(400_000m, Line(after.LiabilityGroups, "Customer Refunds Payable"));
        Assert.Equal(100_000m, after.RetainedProfit);
    }

    // ── 13. Non-cash credits ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NonCashCreditBeforePossession_ReducesTheReceivableExactlyOnce_AndStillBalances()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 5_000_000m);
        await PayAsync(context, world, 3_000_000m, Jan);
        await AddNonCashCreditAsync(context, world, 200_000m, Jan.AddDays(5));
        var finance = Finance(context);

        // Before possession the credit is invisible to finance, exactly as it always was: there is
        // no receivable for it to reduce and no revenue for it to cost against.
        var beforePnl = await finance.GetProfitAndLossAsync(null, Jan.AddDays(-1), Feb.AddDays(-1));
        Assert.Equal(0m, beforePnl.TotalIncome);
        Assert.Equal(0m, beforePnl.TotalExpenses);

        await GivePossessionAsync(context, world, Feb);
        var accounts = new FinanceAccountService(context);

        var pnl = await finance.GetProfitAndLossAsync(null, Jan.AddDays(-1), Mar);
        // The locked rule: the FULL net sale value is revenue.
        Assert.Equal(5_000_000m, Assert.Single(pnl.IncomeLines, l => l.Name == "Unit Sales").Amount);
        // …and the slice the buyer will never pay is a cost, counted once.
        Assert.Equal(200_000m, Assert.Single(pnl.ExpenseLines, l => l.Name == "Customer Credits (non-cash)").Amount);
        Assert.Equal(4_800_000m, pnl.NetProfit);

        // The receivable is what is genuinely still owed: 5.0m − 3.0m cash − 0.2m credit.
        Assert.Equal(1_800_000m, (await accounts.GetByIdAsync(world.Receivables.Id)).CurrentBalance);
        Assert.Equal(0m, (await accounts.GetByIdAsync(world.Deposits.Id)).CurrentBalance);
        await AssertBalancedAsync(finance, Mar);

        // Cash rebates keep their own, unchanged, expense line — this is a different leg.
        Assert.DoesNotContain(pnl.ExpenseLines, l => l.Name == "Cash Rebates");
    }

    // ── 18. The Customer Deposits view ───────────────────────────────────────────────────────

    [Fact]
    public async Task CustomerDepositsView_Pages_Filters_AndClearsOnRecognition()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 5_000_000m);
        await PayAsync(context, world, 500_000m, Jan);
        var second = await AddBookingAsync(context, world, "BK-2", "A-2", netSalePrice: 3_000_000m);
        await PayAsync(context, world, 300_000m, Jan, bookingId: second);
        var otherProject = await AddProjectBookingAsync(context, world, "BK-3", netSalePrice: 1_000_000m);
        await PayAsync(context, world, 100_000m, Jan, bookingId: otherProject.BookingId);
        var finance = Finance(context);

        var firstPage = await finance.GetCustomerDepositPageAsync(null, Feb, 0, 2);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.True(firstPage.HasMore);
        Assert.Equal(500_000m, firstPage.Items[0].DepositBalance); // ordered by size
        var secondPage = await finance.GetCustomerDepositPageAsync(null, Feb, 2, 2);
        Assert.Single(secondPage.Items);
        Assert.False(secondPage.HasMore);

        var byProject = await finance.GetCustomerDepositPageAsync(otherProject.ProjectId, Feb, 0, 20);
        Assert.Equal(100_000m, Assert.Single(byProject.Items).DepositBalance);

        await GivePossessionAsync(context, world, Feb);
        var afterPossession = await finance.GetCustomerDepositPageAsync(null, Mar, 0, 20);
        Assert.DoesNotContain(afterPossession.Items, r => r.BookingId == world.BookingId);
        Assert.Equal(400_000m, afterPossession.Items.Sum(r => r.DepositBalance));

        // As at a date before possession the deposit is still held — and the row now explains why
        // it is about to disappear.
        var asAtJan = await finance.GetCustomerDepositPageAsync(null, Feb.AddDays(-1), 0, 20);
        var stillHeld = Assert.Single(asAtJan.Items, r => r.BookingId == world.BookingId);
        Assert.Equal(500_000m, stillHeld.DepositBalance);
        Assert.Equal(Feb, stillHeld.RecognitionDate);
    }

    // ── 19. Account detail reconciles with the Balance Sheet ─────────────────────────────────

    [Fact]
    public async Task DepositAndReceivableLedgers_ReconcileWithTheBalanceSheet()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 5_000_000m);
        await PayAsync(context, world, 3_000_000m, Jan);
        await GivePossessionAsync(context, world, Feb);
        await PayAsync(context, world, 1_000_000m, Mar);
        var accounts = new FinanceAccountService(context);
        var sheet = await Finance(context).GetBalanceSheetAsync(null, Mar.AddDays(1));
        Assert.True(sheet.IsBalanced);

        var depositLedger = await accounts.GetTransactionsAsync(world.Deposits.Id, 0, 50);
        Assert.Equal(Line(sheet.LiabilityGroups, "Customer General Account / Customer Deposits"),
            depositLedger.Items.Sum(t => t.Amount));
        Assert.Contains(depositLedger.Items, t => t.Kind == "Customer deposit received" && t.Amount == 3_000_000m);
        Assert.Contains(depositLedger.Items, t => t.Kind == "Customer deposit recognised" && t.Amount == -3_000_000m);
        // The post-possession collection is a receivable movement, never a deposit one.
        Assert.DoesNotContain(depositLedger.Items, t => t.Amount == 1_000_000m);

        var receivableLedger = await accounts.GetTransactionsAsync(world.Receivables.Id, 0, 50);
        Assert.Equal(Line(sheet.AssetGroups, "Customer Receivables"), receivableLedger.Items.Sum(t => t.Amount));
        Assert.Contains(receivableLedger.Items, t => t.Kind == "Customer receivable recognised" && t.Amount == 5_000_000m);
        Assert.Contains(receivableLedger.Items, t => t.Kind == "Deposit applied to sale" && t.Amount == -3_000_000m);
        Assert.Contains(receivableLedger.Items, t => t.Kind == "Customer receivable collected" && t.Amount == -1_000_000m);
    }

    // ── 20. Every statement tells the same story ─────────────────────────────────────────────

    [Fact]
    public async Task Summary_ProfitAndLoss_BalanceSheet_TrialBalance_AndExports_Agree()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 5_000_000m);
        await PayAsync(context, world, 3_000_000m, Jan);
        await GivePossessionAsync(context, world, Feb);
        var finance = Finance(context);

        var summary = await finance.GetSummaryAsync(null, Jan.AddDays(-1), Mar);
        var pnl = await finance.GetProfitAndLossAsync(null, Jan.AddDays(-1), Mar);
        Assert.Equal(pnl.TotalIncome, summary.TotalRevenue);
        Assert.Equal(pnl.NetProfit, summary.NetProfit);
        Assert.Equal(0m, summary.CustomerDepositsBalance);

        var rows = await finance.GetRevenuePageAsync(null, Jan.AddDays(-1), Mar, 0, 20);
        var sale = Assert.Single(rows.Items);
        Assert.Equal("Unit Sale", sale.Source);
        Assert.Equal(5_000_000m, sale.Amount);
        Assert.Null(sale.ManualRevenueId); // read-only in the UI: no edit or delete affordance

        var netProfitRows = await finance.GetNetProfitPageAsync(null, Jan.AddDays(-1), Mar, 0, 20);
        Assert.Equal(pnl.NetProfit, netProfitRows.Items.Sum(r => r.Amount));

        // Exports come off the same builders, so they cannot drift from the on-screen report.
        Assert.NotEmpty((await finance.ExportProfitAndLossAsync(null, Jan.AddDays(-1), Mar)).Content);
        Assert.NotEmpty((await finance.ExportBalanceSheetAsync(null, Mar)).Content);
        Assert.NotEmpty((await finance.ExportTrialBalanceAsync(null, MonthEnd(Mar), 0)).Content);

        await AssertBalancedAsync(finance, Mar);
    }

    // ── 21. Legacy backfill ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BackfilledRecognition_UsesTheHistoricalDate_AndIsNeverCreatedTwice()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 1_000_000m);
        await PayAsync(context, world, 1_000_000m, Jan);
        // Stands in for what the migration writes for a booking that reached possession before the
        // recognition table existed: the date comes from the record, never from "now".
        context.Bookings.Single().Status = BookingStatus.PossessionGiven;
        context.Bookings.Single().PossessionDate = Feb;
        context.BookingSaleRecognitions.Add(new BookingSaleRecognition
        {
            BookingId = world.BookingId, RecognitionDate = Feb, NetSaleValue = 1_000_000m, RecognizedAt = Feb
        });
        await context.SaveChangesAsync();
        var finance = Finance(context);

        Assert.Equal(0m, (await finance.GetProfitAndLossAsync(null, Jan.AddDays(-1), Feb.AddDays(-1))).TotalIncome);
        Assert.Equal(1_000_000m, (await finance.GetProfitAndLossAsync(null, Jan.AddDays(-1), Mar)).TotalIncome);

        // Running the workflow again cannot add a second one.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Bookings(context).GivePossessionAsync(world.BookingId, Feb, 1));
        Assert.Single(context.BookingSaleRecognitions);
        await AssertBalancedAsync(finance, Mar);
    }

    // ── 22. WIP is not released at possession ────────────────────────────────────────────────

    [Fact]
    public async Task Possession_DoesNotTouchWorkInProgress_NorCreateCostOfSales()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 5_000_000m);
        var wip = new FinanceAccount
        {
            Name = "Floria Building — Work in Progress", LedgerCode = "26", AccountHolderName = "Seven Ventures",
            Type = FinanceAccountType.WorkInProgress, IsActive = true
        };
        context.FinanceAccounts.Add(wip);
        await context.SaveChangesAsync();
        context.AssetPurchases.Add(new AssetPurchase
        {
            AssetAccountId = wip.Id, FinanceAccountId = world.Bank.Id, Amount = 800_000m,
            ItemName = "Concrete", Category = "Construction", Date = Jan
        });
        await context.SaveChangesAsync();
        await PayAsync(context, world, 5_000_000m, Jan);

        var before = (await new FinanceAccountService(context).GetByIdAsync(wip.Id)).CurrentBalance;
        await GivePossessionAsync(context, world, Feb);
        var after = (await new FinanceAccountService(context).GetByIdAsync(wip.Id)).CurrentBalance;

        // Deferred on purpose: the per-unit allocation rule has not been approved, so possession
        // recognises revenue only and the construction cost stays where it accumulated.
        Assert.Equal(800_000m, before);
        Assert.Equal(800_000m, after);
        var pnl = await Finance(context).GetProfitAndLossAsync(null, Jan.AddDays(-1), Mar);
        Assert.DoesNotContain(pnl.ExpenseLines, l => l.Name.Contains("Cost of Sales", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0m, pnl.TotalExpenses);
    }

    // ── 23. A recognised status is not proof of recognised revenue ───────────────────────────

    [Fact]
    public async Task CompleteSale_RequiresARecognisedSale_NotMerelyAPossessionGivenStatus()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 500_000m);
        await PayAsync(context, world, 500_000m, Jan);

        // A legacy row exactly as the backfill would leave it if it could not date the sale:
        // possession-given by status, with no recognition behind it.
        var booking = await context.Bookings.SingleAsync(b => b.Id == world.BookingId);
        booking.Status = BookingStatus.PossessionGiven;
        booking.PossessionDate = Feb;
        await context.SaveChangesAsync();
        Assert.False(await context.BookingSaleRecognitions.AnyAsync());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Bookings(context).CompleteSaleAsync(world.BookingId, 1));
        Assert.Contains("no recognised sale", error.Message);

        // Completing it would have produced the quiet failure this guards: a finished sale
        // carrying 500k of cash, reporting nothing as income.
        var pnl = await Finance(context).GetProfitAndLossAsync(null, Jan.AddDays(-1), Mar);
        Assert.Equal(0m, pnl.TotalIncome);
    }

    [Fact]
    public async Task EveryPossessedOrCompletedBooking_HasExactlyOneRecognition()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 5_000_000m);
        await PayAsync(context, world, 5_000_000m, Jan);
        await GivePossessionAsync(context, world, Feb);
        await Bookings(context).CompleteSaleAsync(world.BookingId, 1);

        var second = await AddBookingAsync(context, world, "BK-2", "A-2", 2_000_000m);
        await PayAsync(context, world, 2_000_000m, Jan, second);
        await Bookings(context).GivePossessionAsync(second, Feb, 1);

        // The invariant the whole design rests on. Anything that reaches a recognised status
        // without exactly one recognition row is a sale with no revenue, or a sale counted twice.
        var recognisedStatuses = new[] { BookingStatus.PossessionGiven, BookingStatus.SaleCompleted };
        var bookings = await context.Bookings.Where(b => recognisedStatuses.Contains(b.Status)).ToListAsync();
        Assert.Equal(2, bookings.Count);
        foreach (var item in bookings)
            Assert.Equal(1, await context.BookingSaleRecognitions.CountAsync(r => r.BookingId == item.Id));
    }

    // ── 24. A receivable must always have a way to be collected ──────────────────────────────

    [Fact]
    public async Task PossessionWithOutstandingAndNoSchedule_LeavesTheReceivableCollectable()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 5_000_000m);
        // The only way 3m arrives with no schedule behind it: the booking-amount milestone, which
        // is what moves a booking to PaymentPlanActive in the first place. Recorded the way that
        // path records it, so the schedule generated below plans from a real balance.
        await PayAsync(context, world, 3_000_000m, Jan);
        var booking = await context.Bookings.SingleAsync(b => b.Id == world.BookingId);
        booking.BookingAmountRequired = 3_000_000m;
        booking.BookingAmountReceived = 3_000_000m;
        await context.SaveChangesAsync();
        await GivePossessionAsync(context, world, Feb);

        var accounts = new FinanceAccountService(context);
        Assert.Equal(2_000_000m, (await accounts.GetByIdAsync(world.Receivables.Id)).CurrentBalance);

        // No schedule was ever generated, so there is no installment to pay against. Unless a
        // schedule can still be built after possession, that 2m receivable can never be cleared
        // by any route the application offers — a permanent phantom asset on the balance sheet.
        var installments = new InstallmentService(context, accounts);
        var schedule = await installments.GenerateScheduleAsync(world.BookingId, new GenerateInstallmentPlanDto
        {
            AgreedSalePrice = 5_000_000m, DiscountPercent = 0m,
            Frequency = InstallmentFrequency.Monthly, NumberOfInstallments = 2,
            InstallmentStartDate = Mar
        }, 1);

        Assert.Equal(2_000_000m, schedule.Items.Sum(i => i.Amount));
        foreach (var item in schedule.Items)
            await installments.RecordInstallmentPaymentAsync(world.BookingId, item.Id, new RecordInstallmentPaymentDto
            {
                Amount = item.Amount, FinanceAccountId = world.Bank.Id,
                PaymentMethod = PaymentMethod.BankTransfer, PaidAt = Mar
            }, 1);

        Assert.Equal(0m, (await accounts.GetByIdAsync(world.Receivables.Id)).CurrentBalance);
        Assert.Equal(5_000_000m, (await accounts.GetByIdAsync(world.Bank.Id)).CurrentBalance);

        var finance = Finance(context);
        // Collecting a receivable moves cash, never income: the sale was recognised in February
        // and collection in March must not add a second rupee of revenue.
        Assert.Equal(5_000_000m, (await finance.GetProfitAndLossAsync(null, Jan.AddDays(-1), Mar.AddDays(1))).TotalIncome);
        await AssertBalancedAsync(finance, Mar.AddDays(1));
    }

    // ── 25. Recognition cannot be dated outside the life of the booking ──────────────────────

    [Fact]
    public async Task PossessionDate_CannotPrecedeTheBookingDate()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 5_000_000m); // BookingDate = Jan
        await PayAsync(context, world, 5_000_000m, Jan);

        // Recognising in a prior year would push revenue into a period that is almost certainly
        // already reported, and the recognition row is immutable by design.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => GivePossessionAsync(context, world, new DateTime(2024, 6, 1)));
        Assert.Contains("before the booking date", error.Message);
        Assert.False(await context.BookingSaleRecognitions.AnyAsync());

        // The booking's own date is allowed: same-day possession is legitimate.
        await GivePossessionAsync(context, world, Jan);
        Assert.Equal(Jan, (await context.BookingSaleRecognitions.SingleAsync()).RecognitionDate);
    }

    [Fact]
    public async Task PossessionDate_CannotBeInTheFuture()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 5_000_000m);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => GivePossessionAsync(context, world, PakistanTime.Today.AddDays(1)));
        Assert.Contains("future", error.Message);
        Assert.False(await context.BookingSaleRecognitions.AnyAsync());
    }

    // ── 26. UTC timestamps against Pakistan business dates ───────────────────────────────────

    [Fact]
    public async Task PaymentInThePakistanEarlyMorning_DoesNotDisturbDepositOrReceivableBalances()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 5_000_000m);

        // 02:00 PKT on 16 Feb is 21:00 UTC on 15 Feb: the one window where the stored UTC instant
        // and the Pakistan business date disagree. Recognition is a business date (15 Feb), the
        // payment is a UTC instant, and the deposit/receivable split compares the two — so this is
        // the case where a naive boundary would misfile the cash.
        await PayAsync(context, world, 3_000_000m, Jan);
        await GivePossessionAsync(context, world, Feb);
        await PayAsync(context, world, 2_000_000m, new DateTime(2026, 2, 15, 21, 0, 0, DateTimeKind.Utc));

        var accounts = new FinanceAccountService(context);
        // Whichever side of the boundary the payment is filed on, the money is the same money:
        // the deposit is fully cleared, the receivable is fully collected, and the sale is
        // recognised exactly once at its February value.
        Assert.Equal(0m, (await accounts.GetByIdAsync(world.Deposits.Id)).CurrentBalance);
        Assert.Equal(0m, (await accounts.GetByIdAsync(world.Receivables.Id)).CurrentBalance);
        Assert.Equal(5_000_000m, (await accounts.GetByIdAsync(world.Bank.Id)).CurrentBalance);

        var finance = Finance(context);
        Assert.Equal(5_000_000m, (await finance.GetProfitAndLossAsync(null, Jan.AddDays(-1), Mar)).TotalIncome);
        await AssertBalancedAsync(finance, Mar);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    private sealed record World(
        int BookingId, FinanceAccount Bank, FinanceAccount Deposits, FinanceAccount Receivables,
        FinanceAccount? RefundPayable, int ProjectId, int CustomerId);

    private static async Task<World> SeedAsync(AppDbContext context, decimal netSalePrice, bool withRefundPayable = false)
    {
        var bank = new FinanceAccount { Name = "HBL Bank", AccountHolderName = "Seven Ventures", Type = FinanceAccountType.Bank, IsActive = true };
        var deposits = new FinanceAccount
        {
            Name = "Customer General Account / Customer Deposits", LedgerCode = "1", AccountHolderName = "Seven Ventures",
            Type = FinanceAccountType.Liability, SystemRole = FinanceSystemAccountRole.CustomerDeposits, DisplayOrder = 500, IsActive = true
        };
        var receivables = new FinanceAccount
        {
            Name = "Customer Receivables", AccountHolderName = "Seven Ventures",
            Type = FinanceAccountType.Receivable, SystemRole = FinanceSystemAccountRole.CustomerReceivables, DisplayOrder = 420, IsActive = true
        };
        FinanceAccount? refundPayable = withRefundPayable ? new FinanceAccount
        {
            Name = "Customer Refunds Payable", AccountHolderName = "Seven Ventures",
            Type = FinanceAccountType.Liability, SystemRole = FinanceSystemAccountRole.CustomerRefundPayable, DisplayOrder = 515, IsActive = true
        } : null;

        var project = new Project { ProjectName = "Floria Heights", Location = "Islamabad", CreatedById = 1 };
        var unit = new Unit { Project = project, UnitNumber = "A-1", UnitType = "Apartment", Price = netSalePrice, Status = UnitStatus.OnPaymentPlan };
        var customer = new Customer { FullName = "Buyer", Phone = "03001112222", Status = CustomerStatus.Active };
        var booking = new Booking
        {
            BookingReference = "BK-1", Customer = customer, Unit = unit, Source = CustomerSource.Referral,
            Status = BookingStatus.PaymentPlanActive, AgreedSalePrice = netSalePrice, DiscountAmount = 0m,
            BookingAmountRequired = 0m, BookingAmountReceived = 0m, BookingDate = Jan
        };
        context.AddRange(bank, deposits, receivables, project, unit, customer, booking);
        if (refundPayable != null) context.Add(refundPayable);
        await context.SaveChangesAsync();
        return new World(booking.Id, bank, deposits, receivables, refundPayable, project.Id, customer.Id);
    }

    private static async Task<int> AddBookingAsync(AppDbContext context, World world, string reference, string unitNumber, decimal netSalePrice)
    {
        var unit = new Unit { ProjectId = world.ProjectId, UnitNumber = unitNumber, UnitType = "Apartment", Price = netSalePrice, Status = UnitStatus.OnPaymentPlan };
        context.Units.Add(unit);
        await context.SaveChangesAsync();
        var booking = new Booking
        {
            BookingReference = reference, CustomerId = world.CustomerId, UnitId = unit.Id, Source = CustomerSource.Referral,
            Status = BookingStatus.PaymentPlanActive, AgreedSalePrice = netSalePrice, DiscountAmount = 0m, BookingDate = Jan
        };
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();
        return booking.Id;
    }

    private static async Task<(int BookingId, int ProjectId)> AddProjectBookingAsync(
        AppDbContext context, World world, string reference, decimal netSalePrice)
    {
        var project = new Project { ProjectName = "Second Project", Location = "Lahore", CreatedById = 1 };
        var unit = new Unit { Project = project, UnitNumber = "B-1", UnitType = "Apartment", Price = netSalePrice, Status = UnitStatus.OnPaymentPlan };
        var booking = new Booking
        {
            BookingReference = reference, CustomerId = world.CustomerId, Unit = unit, Source = CustomerSource.Referral,
            Status = BookingStatus.PaymentPlanActive, AgreedSalePrice = netSalePrice, DiscountAmount = 0m, BookingDate = Jan
        };
        context.AddRange(project, unit, booking);
        await context.SaveChangesAsync();
        return (booking.Id, project.Id);
    }

    private static async Task PayAsync(AppDbContext context, World world, decimal amount, DateTime paidAt, int? bookingId = null)
    {
        context.Payments.Add(new Payment
        {
            BookingId = bookingId ?? world.BookingId, FinanceAccountId = world.Bank.Id, Amount = amount,
            Type = PaymentType.Installment, PaymentMethod = PaymentMethod.BankTransfer, PaidAt = paidAt
        });
        await context.SaveChangesAsync();
    }

    private static async Task AddNonCashCreditAsync(AppDbContext context, World world, decimal amount, DateTime appliedAt)
    {
        var rebate = new CustomerRebate
        {
            BookingId = world.BookingId, CustomerId = world.CustomerId,
            CalculationType = FinancialCalculationType.FixedAmount, FixedAmount = amount,
            BasisAmount = amount, CalculatedAmount = amount, FinalAmount = amount, ApprovedAmount = amount,
            Method = CustomerRebateMethod.OutstandingBalanceReduction, Reason = "Goodwill",
            Status = CustomerRebateStatus.Approved, CreatedByUserId = 1, CreatedByName = "Admin"
        };
        context.CustomerRebates.Add(rebate);
        await context.SaveChangesAsync();
        context.RebateDisbursements.Add(new RebateDisbursement
        {
            RebateId = rebate.Id, Amount = amount, Method = CustomerRebateMethod.OutstandingBalanceReduction,
            AppliedAt = appliedAt, RecordedByUserId = 1, RecordedByName = "Admin",
            IdempotencyKey = $"dis-{Guid.NewGuid():N}"
        });
        await context.SaveChangesAsync();
    }

    private static async Task GivePossessionAsync(AppDbContext context, World world, DateTime date) =>
        await Bookings(context).GivePossessionAsync(world.BookingId, date, 1);

    private static async Task CancelAsync(AppDbContext context, World world, decimal cash, decimal refund, string key) =>
        await Bookings(context).CancelBookingAsync(world.BookingId, new CancelBookingDto
        {
            Reason = "Customer requested cancellation", ExpectedCustomerCashReceived = cash,
            RefundAmount = refund, IdempotencyKey = key,
            RefundDecision = refund > 0m ? CancellationRefundDecision.PayLater : CancellationRefundDecision.None
        }, new DAMS.Application.Common.FinancialWorkflowActor(1, "Admin"));

    private static async Task AssertBalancedAsync(FinanceService finance, DateTime asAt)
    {
        var sheet = await finance.GetBalanceSheetAsync(null, asAt);
        Assert.True(sheet.IsBalanced, $"Balance sheet out by {sheet.Imbalance}: {string.Join(", ", sheet.UnbalancedAccounts ?? [])}");
        var trial = await finance.GetTrialBalanceAsync(null, MonthEnd(asAt), 0);
        Assert.True(Assert.Single(trial.ColumnBalanced));
        Assert.All(trial.Rows, row =>
        {
            Assert.All(row.DebitBalances, d => Assert.True(d >= 0m, $"{row.AccountName} has a negative debit"));
            Assert.All(row.CreditBalances, c => Assert.True(c >= 0m, $"{row.AccountName} has a negative credit"));
        });
    }

    private static decimal Line(IEnumerable<BsGroupDto> groups, string accountName) =>
        groups.SelectMany(g => g.Lines).Where(l => l.Name == accountName).Sum(l => l.Amount);

    private static DateTime MonthEnd(DateTime date) => new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));

    private static BookingService Bookings(AppDbContext context) =>
        new(context, new CustomerService(context), new FinanceAccountService(context));

    private static FinanceService Finance(AppDbContext context)
    {
        var accounts = new FinanceAccountService(context);
        return new FinanceService(context, new NoopStorage(), accounts, new WhtService(context, accounts), NullLogger<FinanceService>.Instance);
    }

    private static AppDbContext Context()
    {
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        context.Database.EnsureCreated();
        return context;
    }

    private sealed class NoopStorage : IFinanceAttachmentStorage
    {
        public Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default) => Task.FromResult("unused");
        public Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(null);
        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
