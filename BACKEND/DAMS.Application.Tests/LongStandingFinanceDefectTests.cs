using DAMS.Application.Common;
using DAMS.Application.DTOs.BookingDtos;
using DAMS.Application.DTOs.CommissionRebateDtos;
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
/// Long-standing defects across the commission, rebate, installment and reporting seams. Every test
/// here fails on the behaviour that shipped before this change, and each one names the exact wrong
/// answer that behaviour gave.
/// </summary>
public sealed class LongStandingFinanceDefectTests
{
    private static readonly DateTime Jan = new(2026, 1, 15);
    private static readonly DateTime Feb = new(2026, 2, 15);
    private static readonly FinancialWorkflowActor Actor = new(9, "Finance Admin");

    // ── Commission is a cost when it is AGREED, not when it is paid ──────────────────────────

    /// <summary>
    /// The obligation reaches the formal statements on the day the commission is agreed. Derived
    /// from payouts, a commission agreed and not yet paid was neither a cost in the P&amp;L nor a
    /// liability on the Balance Sheet — the company reported a profit it did not have and a debt it
    /// did not show.
    /// </summary>
    [Fact]
    public async Task AnAgreedCommission_IsACostAndAPayable_BeforeAnyMoneyMoves()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var commission = await AgreeCommissionAsync(context, world, 100_000m);

        var accrual = Assert.Single(context.CommissionAccruals.ToList());
        Assert.Equal(100_000m, accrual.Amount);
        Assert.Equal(CommissionAccrualKind.Recognition, accrual.Kind);
        Assert.Equal(PakistanTime.Today, accrual.AccruedOn);
        Assert.Equal(100_000m, commission.FinalAmount);

        var finance = Finance(context);
        var pnl = await finance.GetProfitAndLossAsync(null, PakistanTime.Today, PakistanTime.Today);
        Assert.Equal(100_000m, Assert.Single(pnl.ExpenseLines, l => l.Name == "Partner Commissions").Amount);

        var sheet = await finance.GetBalanceSheetAsync(null, PakistanTime.Today);
        Assert.Equal(100_000m, PayableBalance(sheet));
        Assert.True(sheet.IsBalanced, $"Balance sheet out by {sheet.Imbalance}");
    }

    /// <summary>
    /// Paying it settles the payable and costs nothing extra. Before this change the payout WAS the
    /// cost, so a commission agreed in one month and paid in the next was charged to the wrong month
    /// — and if it had also been accrued it would have been charged twice.
    /// </summary>
    [Fact]
    public async Task PayingACommission_SettlesThePayable_AndIsNotASecondCost()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var commission = await AgreeCommissionAsync(context, world, 100_000m);

        await CommissionRebates(context).RecordPayoutAsync(world.BookingId, commission.Id,
            new RecordCommissionPayoutDto
            {
                FinanceAccountId = world.Bank.Id, Amount = 60_000m, PaymentDate = PakistanTime.Today,
                PaymentMethod = PaymentMethod.BankTransfer, PaymentReference = "TT-1",
                IdempotencyKey = "pay-1", CommissionConcurrencyToken = commission.ConcurrencyToken
            }, Actor);

        var finance = Finance(context);
        var pnl = await finance.GetProfitAndLossAsync(null, PakistanTime.Today, PakistanTime.Today);
        Assert.Equal(100_000m, Assert.Single(pnl.ExpenseLines, l => l.Name == "Partner Commissions").Amount);

        var sheet = await finance.GetBalanceSheetAsync(null, PakistanTime.Today);
        Assert.Equal(40_000m, PayableBalance(sheet));
        Assert.True(sheet.IsBalanced, $"Balance sheet out by {sheet.Imbalance}");

        // The Total Expenses card and its drill-down are the same arithmetic, so they carry the
        // accrual and never the payout.
        var summary = await finance.GetSummaryAsync(null, PakistanTime.Today, PakistanTime.Today);
        var breakdown = await finance.GetCostBreakdownPageAsync(null, PakistanTime.Today, PakistanTime.Today, 0, 100);
        Assert.Equal(100_000m, summary.TotalExpenses);
        Assert.Equal(summary.TotalExpenses, breakdown.Items.Sum(i => i.Amount));
    }

    /// <summary>
    /// Correcting a pending commission moves the obligation by the DIFFERENCE, dated when the
    /// correction was made. Restating the original accrual would rewrite a period that has already
    /// been reported.
    /// </summary>
    [Fact]
    public async Task CorrectingAPendingCommission_AccruesOnlyTheDifference()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var commission = await AgreeCommissionAsync(context, world, 100_000m);

        var workspace = await CommissionRebates(context).UpdateCommissionAsync(world.BookingId, commission.Id,
            new UpdateBookingCommissionDto
            {
                PartnerId = world.PartnerId, IsManual = true,
                ManualCalculationType = FinancialCalculationType.FixedAmount,
                ManualCalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
                ManualFixedAmount = 130_000m,
                ConcurrencyToken = commission.ConcurrencyToken, ChangeReason = "Renegotiated"
            }, Actor);

        Assert.Equal(130_000m, Assert.Single(workspace.Commissions).FinalAmount);
        var rows = context.CommissionAccruals.OrderBy(a => a.Id).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(100_000m, rows[0].Amount);
        Assert.Equal(30_000m, rows[1].Amount);
        Assert.Equal(CommissionAccrualKind.Adjustment, rows[1].Kind);
        Assert.Equal(130_000m, rows.Sum(a => a.Amount));
    }

    /// <summary>
    /// Cancelling takes the whole obligation back off — the expense and the payable both go, and
    /// they go on the day the decision was made rather than by unwinding the period it was agreed in.
    /// </summary>
    [Fact]
    public async Task CancellingACommission_ReleasesTheWholeObligation()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var commission = await AgreeCommissionAsync(context, world, 100_000m);

        await CommissionRebates(context).ChangeCommissionStatusAsync(world.BookingId, commission.Id,
            new CommissionStatusChangeDto
            {
                TargetStatus = BookingCommissionStatus.Cancelled, Reason = "Partner withdrew",
                ConcurrencyToken = commission.ConcurrencyToken
            }, Actor);

        var rows = context.CommissionAccruals.OrderBy(a => a.Id).ToList();
        Assert.Equal(-100_000m, rows[^1].Amount);
        Assert.Equal(CommissionAccrualKind.Release, rows[^1].Kind);
        Assert.Equal(0m, rows.Sum(a => a.Amount));

        var sheet = await Finance(context).GetBalanceSheetAsync(null, PakistanTime.Today);
        Assert.Equal(0m, PayableBalance(sheet));
        Assert.True(sheet.IsBalanced, $"Balance sheet out by {sheet.Imbalance}");
    }

    /// <summary>
    /// The commission dies with its booking. Nothing is owed on a void sale, so the obligation is
    /// released whether or not money had already gone out — a paid one leaves a negative payable,
    /// which is the amount recoverable from the partner, not a cost of the cancelled sale.
    /// </summary>
    [Fact]
    public async Task CancellingTheBooking_ReleasesTheCommissionObligation()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var commission = await AgreeCommissionAsync(context, world, 100_000m);

        await CommissionRebates(context).HandleBookingCancelledAsync(
            world.BookingId, "Buyer walked away", Actor);
        await context.SaveChangesAsync();

        Assert.Equal(0m, context.CommissionAccruals.Sum(a => a.Amount));
        Assert.Equal(BookingCommissionStatus.Cancelled,
            (await context.BookingCommissions.SingleAsync(c => c.Id == commission.Id)).Status);
    }

    // ── E6: a closed obligation has no Remaining ─────────────────────────────────────────────

    /// <summary>
    /// A cancelled commission or rebate owes nothing. Subtracting movements from the final amount
    /// regardless of status left a cancelled Rs 100,000 commission reading "Remaining 100,000" on
    /// the booking screen — an unpaid debt on a record that had been closed.
    /// </summary>
    [Fact]
    public async Task AClosedCommissionOrRebate_ShowsNothingRemaining()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var service = CommissionRebates(context);
        var commission = await AgreeCommissionAsync(context, world, 100_000m, service);
        await service.ChangeCommissionStatusAsync(world.BookingId, commission.Id, new CommissionStatusChangeDto
        {
            TargetStatus = BookingCommissionStatus.Cancelled, Reason = "Withdrawn",
            ConcurrencyToken = commission.ConcurrencyToken
        }, Actor);

        var rebateWorkspace = await service.CreateRebateAsync(world.BookingId, new CreateCustomerRebateDto
        {
            CalculationType = FinancialCalculationType.FixedAmount,
            CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
            FixedAmount = 50_000m, Reason = "Goodwill", Method = CustomerRebateMethod.OutstandingBalanceReduction
        }, Actor);
        var rebate = Assert.Single(rebateWorkspace.Rebates);
        var afterCancel = await service.ChangeRebateStatusAsync(world.BookingId, rebate.Id, new RebateStatusChangeDto
        {
            TargetStatus = CustomerRebateStatus.Cancelled, Reason = "Withdrawn",
            ConcurrencyToken = rebate.ConcurrencyToken
        }, Actor);

        var closedCommission = Assert.Single(afterCancel.Commissions);
        var closedRebate = Assert.Single(afterCancel.Rebates);
        Assert.Equal(BookingCommissionStatus.Cancelled, closedCommission.Status);
        Assert.Equal(0m, closedCommission.OutstandingAmount);
        Assert.Equal(CustomerRebateStatus.Cancelled, closedRebate.Status);
        Assert.Equal(0m, closedRebate.OutstandingAmount);
        // The agreed figures are still readable — they are history, not a live balance.
        Assert.Equal(100_000m, closedCommission.FinalAmount);
        Assert.Equal(50_000m, closedRebate.FinalAmount);
    }

    // ── E4: credits count toward the booking-amount milestone ────────────────────────────────

    /// <summary>
    /// Editing the terms advances the booking when the booking amount is covered in cash OR credit.
    /// Comparing cash against the raw requirement left a credit-covered booking in
    /// AwaitingBookingAmount, where the payment service would refuse every further receipt — the
    /// plan could never be generated and the sale never finished.
    /// </summary>
    [Fact]
    public async Task EditingTermsAdvancesABookingWhoseBookingAmountACreditHasAlreadyCovered()
    {
        await using var context = Context();
        var world = await SeedAsync(context, status: BookingStatus.AwaitingBookingAmount, bookingAmount: 0m);
        var booking = await context.Bookings.SingleAsync();
        booking.BookingAmountRequired = 2_000_000m;
        await context.SaveChangesAsync();

        // The credit does not cover the requirement as it stands, so the booking correctly stays put
        // when it is applied. The terms edit is the door this defect lived behind.
        await ApplyCreditAsync(context, world.BookingId, 1_000_000m);
        Assert.Equal(BookingStatus.AwaitingBookingAmount, (await context.Bookings.AsNoTracking().SingleAsync()).Status);

        var updated = await Bookings(context).UpdateBookingFinancialsAsync(world.BookingId,
            new UpdateBookingFinancialsDto
            {
                AgreedSalePrice = 10_000_000m, DiscountPercent = 0m, BookingAmountRequired = 800_000m
            }, 1);

        Assert.Equal(BookingStatus.PaymentPlanActive, updated.Status);
        Assert.Equal(UnitStatus.OnPaymentPlan, (await context.Units.SingleAsync()).Status);
        // …and the screen agrees with the payment service about what is left to collect: nothing.
        Assert.Equal(0m, updated.BookingAmountRemaining);
        Assert.Equal(1_000_000m, updated.RebateCredits);
    }

    /// <summary>The same edit on a booking with no credit still waits for the cash.</summary>
    [Fact]
    public async Task EditingTermsLeavesAnUncoveredBookingAwaitingItsBookingAmount()
    {
        await using var context = Context();
        var world = await SeedAsync(context, status: BookingStatus.AwaitingBookingAmount, bookingAmount: 0m);

        var updated = await Bookings(context).UpdateBookingFinancialsAsync(world.BookingId,
            new UpdateBookingFinancialsDto
            {
                AgreedSalePrice = 10_000_000m, DiscountPercent = 0m, BookingAmountRequired = 1_000_000m
            }, 1);

        Assert.Equal(BookingStatus.AwaitingBookingAmount, updated.Status);
        Assert.Equal(1_000_000m, updated.BookingAmountRemaining);
    }

    // ── E8: booking projections count credits, and keep the cash separate ────────────────────

    /// <summary>
    /// An installment settled by a credit is settled. Counting only cash made the booking projection
    /// claim a balance the schedule screen no longer showed, and the difference was exactly the
    /// rebate. The cash actually received stays its own figure so the two are never conflated.
    /// </summary>
    [Fact]
    public async Task ABookingProjection_CountsCreditsAsSettled_AndStillReportsTheCashSeparately()
    {
        await using var context = Context();
        var world = await SeedAsync(context, bookingAmount: 1_000_000m);
        await Installments(context).GenerateScheduleAsync(world.BookingId, Plan(10_000_000m, 3), 1);

        var schedule = await Installments(context).GetScheduleAsync(world.BookingId);
        var first = schedule.Items[0];
        await Installments(context).RecordInstallmentPaymentAsync(world.BookingId, first.Id,
            new RecordInstallmentPaymentDto
            {
                Amount = 500_000m, FinanceAccountId = world.Bank.Id,
                PaymentMethod = PaymentMethod.BankTransfer, PaidAt = Feb
            }, 1);
        await ApplyCreditAsync(context, world.BookingId, 900_000m);

        var projection = await Bookings(context).GetBookingByIdAsync(world.BookingId);
        var afterCredit = await Installments(context).GetScheduleAsync(world.BookingId);

        Assert.NotNull(projection);
        // One number for "settled", and it is the schedule screen's own.
        Assert.Equal(afterCredit.SchedulePaid, projection!.InstallmentPaid);
        Assert.Equal(afterCredit.ScheduleRemaining, projection.InstallmentRemaining);
        Assert.Equal(1_400_000m, projection.InstallmentPaid);
        // …and the cash is still identifiable on its own.
        Assert.Equal(500_000m, projection.InstallmentCashReceived);
        Assert.Equal(900_000m, projection.RebateCredits);
    }

    // ── E5: a plan pinned by a historical reference cannot be regenerated ────────────────────

    /// <summary>
    /// A disbursement aimed at an installment keeps that installment id for ever — a reversal is a
    /// new row, never an edit of the original — and the foreign key is restrictive. Allowing
    /// regeneration once the balance was fully reversed let validation pass and the SAVE blow up on
    /// a foreign key, with a raw 500 where a plain refusal belonged.
    /// </summary>
    [Fact]
    public async Task AFullyReversedInstallmentCredit_StillPinsTheSchedule()
    {
        await using var context = Context();
        var world = await SeedAsync(context, bookingAmount: 1_000_000m);
        await Installments(context).GenerateScheduleAsync(world.BookingId, Plan(10_000_000m, 3), 1);
        var schedule = await Installments(context).GetScheduleAsync(world.BookingId);
        Assert.True(schedule.CanRegenerate);

        var service = CommissionRebates(context);
        var applied = await ApplyCreditAsync(context, world.BookingId, 100_000m, service,
            CustomerRebateMethod.InstallmentAdjustment, schedule.Items[0].Id);
        var rebate = Assert.Single(applied.Rebates);
        var disbursement = Assert.Single(rebate.Disbursements);
        await service.ReverseRebateDisbursementAsync(world.BookingId, rebate.Id, disbursement.Id,
            new ReverseMoneyMovementDto { Amount = 100_000m, Reason = "Wrong installment", IdempotencyKey = "rev-1" },
            Actor);

        var afterReversal = await Installments(context).GetScheduleAsync(world.BookingId);
        Assert.False(afterReversal.CanRegenerate);
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Installments(context).GenerateScheduleAsync(world.BookingId, Plan(10_000_000m, 4, regenerate: true), 1));
        Assert.Contains("cannot be regenerated", refusal.Message);
        // The historical row is still there, pointing at the installment it was aimed at.
        Assert.NotNull((await context.RebateDisbursements.SingleAsync()).InstallmentId);
    }

    // ── E2: a reversal must not strand debt the schedule cannot collect ──────────────────────

    /// <summary>
    /// A plan generated while a credit stood was built SMALLER by it, so reversing the credit
    /// restores principal that has no installment to sit on. Once the plan is pinned by a payment
    /// there is no way to collect it: it silently blocks completion for ever while the schedule
    /// shows nothing owing. The reversal is refused instead.
    /// </summary>
    [Fact]
    public async Task ReversingACreditTheLockedPlanAbsorbed_IsRefused()
    {
        await using var context = Context();
        var world = await SeedAsync(context, bookingAmount: 1_000_000m);
        var service = CommissionRebates(context);
        var applied = await ApplyCreditAsync(context, world.BookingId, 900_000m, service);
        var rebate = Assert.Single(applied.Rebates);
        var disbursement = Assert.Single(rebate.Disbursements);

        // Plan built AFTER the credit: the pool is 10,000,000 − 1,000,000 − 900,000 = 8,100,000.
        await Installments(context).GenerateScheduleAsync(world.BookingId, Plan(10_000_000m, 3), 1);
        var schedule = await Installments(context).GetScheduleAsync(world.BookingId);
        Assert.Equal(8_100_000m, schedule.ScheduleTotal);

        // A payment pins the plan.
        await Installments(context).RecordInstallmentPaymentAsync(world.BookingId, schedule.Items[0].Id,
            new RecordInstallmentPaymentDto
            {
                Amount = 100_000m, FinanceAccountId = world.Bank.Id,
                PaymentMethod = PaymentMethod.BankTransfer, PaidAt = Feb
            }, 1);

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReverseRebateDisbursementAsync(world.BookingId, rebate.Id, disbursement.Id,
                new ReverseMoneyMovementDto { Amount = 900_000m, Reason = "Granted in error", IdempotencyKey = "rev-2" },
                Actor));
        Assert.Contains("no way to collect", refusal.Message);

        // Nothing was written: the credit still stands and the schedule still adds up.
        Assert.Empty(context.RebateDisbursementReversals.ToList());
        var after = await Installments(context).GetScheduleAsync(world.BookingId);
        Assert.Equal(8_100_000m, after.ScheduleTotal);
    }

    /// <summary>
    /// The same reversal goes through while the plan can still be regenerated — the refusal is about
    /// stranded debt, not about freezing a rebate the moment a schedule exists.
    /// </summary>
    [Fact]
    public async Task ReversingACreditIsAllowedWhileThePlanCanStillBeRebuilt()
    {
        await using var context = Context();
        var world = await SeedAsync(context, bookingAmount: 1_000_000m);
        var service = CommissionRebates(context);
        var applied = await ApplyCreditAsync(context, world.BookingId, 900_000m, service);
        var rebate = Assert.Single(applied.Rebates);
        var disbursement = Assert.Single(rebate.Disbursements);
        await Installments(context).GenerateScheduleAsync(world.BookingId, Plan(10_000_000m, 3), 1);

        await service.ReverseRebateDisbursementAsync(world.BookingId, rebate.Id, disbursement.Id,
            new ReverseMoneyMovementDto { Amount = 900_000m, Reason = "Granted in error", IdempotencyKey = "rev-3" },
            Actor);

        var schedule = await Installments(context).GetScheduleAsync(world.BookingId);
        Assert.True(schedule.CanRegenerate);
        // The pool the operator is told to rebuild to now carries the restored principal.
        Assert.Equal(9_000_000m, schedule.InstallmentPool);
    }

    /// <summary>
    /// The reversal is allowed while the plan is rebuildable, so the COLLECTION is what waits for the
    /// rebuild. Taking the next receipt first would pin the under-sized plan and reach the very dead
    /// end the reversal guard exists to prevent — the same stranded principal, one ordering later.
    /// </summary>
    [Fact]
    public async Task AfterReversingAnAbsorbedCredit_NoPaymentIsTakenUntilThePlanIsRebuilt()
    {
        await using var context = Context();
        var world = await SeedAsync(context, bookingAmount: 1_000_000m);
        var service = CommissionRebates(context);
        var applied = await ApplyCreditAsync(context, world.BookingId, 900_000m, service);
        var rebate = Assert.Single(applied.Rebates);
        var disbursement = Assert.Single(rebate.Disbursements);
        await Installments(context).GenerateScheduleAsync(world.BookingId, Plan(10_000_000m, 3), 1);

        await service.ReverseRebateDisbursementAsync(world.BookingId, rebate.Id, disbursement.Id,
            new ReverseMoneyMovementDto { Amount = 400_000m, Reason = "Granted in error", IdempotencyKey = "rev-5" },
            Actor);

        // The plan now collects 8,100,000 while 8,500,000 is owed. The screen says so…
        var shortOfBalance = await Installments(context).GetScheduleAsync(world.BookingId);
        Assert.Equal(8_100_000m, shortOfBalance.ScheduleTotal);
        Assert.Equal(400_000m, shortOfBalance.UnscheduledBalance);
        Assert.True(shortOfBalance.CanRegenerate);

        // …and no receipt is accepted against it, because that receipt would pin the plan.
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Installments(context).RecordInstallmentPaymentAsync(world.BookingId, shortOfBalance.Items[0].Id,
                new RecordInstallmentPaymentDto
                {
                    Amount = 100_000m, FinanceAccountId = world.Bank.Id,
                    PaymentMethod = PaymentMethod.BankTransfer, PaidAt = Feb
                }, 1));
        Assert.Contains("Regenerate the installment plan", refusal.Message);
        Assert.Empty(context.Payments.Where(p => p.Type == PaymentType.Installment).ToList());

        // Regenerating repairs it, and collection resumes against a plan that adds up.
        await Installments(context).GenerateScheduleAsync(world.BookingId, Plan(10_000_000m, 3, regenerate: true), 1);
        var rebuilt = await Installments(context).GetScheduleAsync(world.BookingId);
        Assert.Equal(8_500_000m, rebuilt.ScheduleTotal);
        Assert.Equal(0m, rebuilt.UnscheduledBalance);
        await Installments(context).RecordInstallmentPaymentAsync(world.BookingId, rebuilt.Items[0].Id,
            new RecordInstallmentPaymentDto
            {
                Amount = 100_000m, FinanceAccountId = world.Bank.Id,
                PaymentMethod = PaymentMethod.BankTransfer, PaidAt = Feb
            }, 1);

        // What the customer owes and what the plan can collect are the same number again.
        var booking = await context.Bookings.AsNoTracking().SingleAsync();
        var credits = await context.RebateDisbursements.AsNoTracking().SumAsync(d => d.Amount)
            - await context.RebateDisbursementReversals.AsNoTracking().SumAsync(r => r.Amount);
        var collected = await context.Payments.AsNoTracking().SumAsync(p => p.Amount);
        var owed = booking.AgreedSalePrice - booking.DiscountAmount - collected - credits;
        var settled = await Installments(context).GetScheduleAsync(world.BookingId);
        Assert.Equal(owed, settled.ScheduleRemaining);
    }

    /// <summary>
    /// Cancelling voids the sale, so there is no receivable to collect and no plan to rebuild. The
    /// collectibility guard must stay out of that lifecycle entirely — otherwise a cancelled
    /// booking's applied rebate is stuck in ReversalRequired for ever, unable to reach Reversed.
    /// </summary>
    [Fact]
    public async Task ACancelledBookingsCredit_CanStillBeReversed_EvenWithAPinnedUnderSizedPlan()
    {
        await using var context = Context();
        var world = await SeedAsync(context, bookingAmount: 1_000_000m);
        var service = CommissionRebates(context);
        var applied = await ApplyCreditAsync(context, world.BookingId, 900_000m, service);
        var rebate = Assert.Single(applied.Rebates);
        var disbursement = Assert.Single(rebate.Disbursements);
        await Installments(context).GenerateScheduleAsync(world.BookingId, Plan(10_000_000m, 3), 1);
        var schedule = await Installments(context).GetScheduleAsync(world.BookingId);
        await Installments(context).RecordInstallmentPaymentAsync(world.BookingId, schedule.Items[0].Id,
            new RecordInstallmentPaymentDto
            {
                Amount = 100_000m, FinanceAccountId = world.Bank.Id,
                PaymentMethod = PaymentMethod.BankTransfer, PaidAt = Feb
            }, 1);

        // Cancel through the lifecycle the booking service uses, so the rebate reaches the state a
        // real cancellation leaves it in.
        await service.HandleBookingCancelledAsync(world.BookingId, "Buyer walked away", Actor);
        var booking = await context.Bookings.SingleAsync();
        booking.Status = BookingStatus.Cancelled;
        await context.SaveChangesAsync();
        Assert.Equal(CustomerRebateStatus.ReversalRequired,
            (await context.CustomerRebates.AsNoTracking().SingleAsync()).Status);

        var after = await service.ReverseRebateDisbursementAsync(world.BookingId, rebate.Id, disbursement.Id,
            new ReverseMoneyMovementDto { Amount = 900_000m, Reason = "Recovered", IdempotencyKey = "rev-6" },
            Actor);

        Assert.Equal(CustomerRebateStatus.Reversed, Assert.Single(after.Rebates).Status);
        Assert.Equal(900_000m, context.RebateDisbursementReversals.Sum(r => r.Amount));
    }

    /// <summary>
    /// A booking with no schedule at all has nothing to strand, so the guard stays out of the way.
    /// </summary>
    [Fact]
    public async Task ReversingACreditOnABookingWithNoSchedule_IsUnaffected()
    {
        await using var context = Context();
        var world = await SeedAsync(context, bookingAmount: 1_000_000m);
        var service = CommissionRebates(context);
        var applied = await ApplyCreditAsync(context, world.BookingId, 900_000m, service);
        var rebate = Assert.Single(applied.Rebates);
        var disbursement = Assert.Single(rebate.Disbursements);

        await service.ReverseRebateDisbursementAsync(world.BookingId, rebate.Id, disbursement.Id,
            new ReverseMoneyMovementDto { Amount = 900_000m, Reason = "Granted in error", IdempotencyKey = "rev-4" },
            Actor);

        Assert.Single(context.RebateDisbursementReversals.ToList());
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────

    private static decimal PayableBalance(DTOs.FinanceDtos.BalanceSheetDto sheet) =>
        sheet.LiabilityGroups.SelectMany(g => g.Lines)
            .Where(l => l.Name == "Commission Payable").Sum(l => l.Amount);

    private static async Task<BookingCommissionDto> AgreeCommissionAsync(
        AppDbContext context, World world, decimal amount, CommissionRebateService? service = null)
    {
        service ??= CommissionRebates(context);
        var workspace = await service.CreateCommissionAsync(world.BookingId, new CreateBookingCommissionDto
        {
            PartnerId = world.PartnerId, IsManual = true,
            ManualCalculationType = FinancialCalculationType.FixedAmount,
            ManualCalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
            ManualFixedAmount = amount
        }, Actor);
        return Assert.Single(workspace.Commissions);
    }

    private static async Task<BookingCommissionRebateWorkspaceDto> ApplyCreditAsync(
        AppDbContext context, int bookingId, decimal amount, CommissionRebateService? service = null,
        CustomerRebateMethod method = CustomerRebateMethod.OutstandingBalanceReduction,
        int? installmentId = null)
    {
        service ??= CommissionRebates(context);
        var workspace = await service.CreateRebateAsync(bookingId, new CreateCustomerRebateDto
        {
            CalculationType = FinancialCalculationType.FixedAmount,
            CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount,
            FixedAmount = amount, Reason = "Customer retention", Method = method
        }, Actor);
        var rebate = Assert.Single(workspace.Rebates);
        return await service.RecordRebateDisbursementAsync(bookingId, rebate.Id, new RecordRebateDisbursementDto
        {
            Method = method, Amount = amount, AppliedAt = Jan, InstallmentId = installmentId,
            Reference = method == CustomerRebateMethod.CreditNote ? "CN-1" : null,
            IdempotencyKey = $"credit-{bookingId}-{amount}-{installmentId}",
            RebateConcurrencyToken = rebate.ConcurrencyToken
        }, Actor);
    }

    private static GenerateInstallmentPlanDto Plan(decimal price, int count, bool regenerate = false) => new()
    {
        AgreedSalePrice = price, DiscountPercent = 0m, Frequency = InstallmentFrequency.Monthly,
        NumberOfInstallments = count, InstallmentStartDate = Jan, Regenerate = regenerate
    };

    private sealed record World(int BookingId, int PartnerId, FinanceAccount Bank);

    private static async Task<World> SeedAsync(
        AppDbContext context,
        decimal netSalePrice = 10_000_000m,
        decimal bookingAmount = 0m,
        BookingStatus status = BookingStatus.PaymentPlanActive)
    {
        var bank = new FinanceAccount { Name = "Collection Bank", AccountHolderName = "SV", Type = FinanceAccountType.Bank, IsActive = true };
        var deposits = new FinanceAccount
        {
            Name = "Customer Deposits", AccountHolderName = "SV", Type = FinanceAccountType.Liability,
            SystemRole = FinanceSystemAccountRole.CustomerDeposits, DisplayOrder = 500, IsActive = true
        };
        var receivables = new FinanceAccount
        {
            Name = "Customer Receivables", AccountHolderName = "SV", Type = FinanceAccountType.Receivable,
            SystemRole = FinanceSystemAccountRole.CustomerReceivables, DisplayOrder = 420, IsActive = true
        };
        var commissionPayable = new FinanceAccount
        {
            Name = "Commission Payable", AccountHolderName = "SV", Type = FinanceAccountType.Liability,
            SystemRole = FinanceSystemAccountRole.CommissionPayable, DisplayOrder = 517, IsActive = true
        };
        var project = new Project { ProjectName = "Floria Heights", Location = "Islamabad", CreatedById = 1 };
        var unit = new Unit { Project = project, UnitNumber = "A-1", UnitType = "Apartment", Price = netSalePrice, Status = UnitStatus.OnPaymentPlan };
        var customer = new Customer { FullName = "Buyer", Phone = "03001112222", Status = CustomerStatus.Active };
        var booking = new Booking
        {
            BookingReference = "BK-LSD", Customer = customer, Unit = unit, Source = CustomerSource.Referral,
            Status = status, AgreedSalePrice = netSalePrice, DiscountAmount = 0m,
            BookingAmountRequired = bookingAmount, BookingAmountReceived = bookingAmount, BookingDate = Jan
        };
        if (bookingAmount > 0m)
            booking.Payments.Add(new Payment
            {
                Booking = booking, Amount = bookingAmount, Type = PaymentType.BookingAmount,
                PaymentMethod = PaymentMethod.BankTransfer, PaidAt = Jan
            });
        var partner = new ThirdPartyPartner
        {
            Name = "ABC Broker", PartnerType = "Broker", InternalCode = "ABC-1", IsActive = true,
            BankName = "Test Bank", AccountTitle = "ABC Broker", AccountNumber = "00123456789"
        };
        context.AddRange(bank, deposits, receivables, commissionPayable, project, unit, customer, booking, partner);
        await context.SaveChangesAsync();
        foreach (var payment in booking.Payments) payment.FinanceAccountId = bank.Id;
        await context.SaveChangesAsync();
        return new World(booking.Id, partner.Id, bank);
    }

    private static BookingService Bookings(AppDbContext context) =>
        new(context, new CustomerService(context), new FinanceAccountService(context));

    private static InstallmentService Installments(AppDbContext context) =>
        new(context, new FinanceAccountService(context));

    private static CommissionRebateService CommissionRebates(AppDbContext context) =>
        new(context, new FinanceAccountService(context), new NoopEvidence());

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

    private sealed class NoopEvidence : IFinancialEvidenceStorage
    {
        public Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default) => Task.FromResult("unused");
        public Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(null);
        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
