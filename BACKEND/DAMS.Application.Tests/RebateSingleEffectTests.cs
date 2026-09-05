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
/// A rebate reduces what the customer owes exactly ONCE.
/// <para>
/// It can reach the customer's obligation by two roads — the booking's own price, and the credit
/// applied against the balance — and the money must travel exactly one of them. Both at once and the
/// receivable goes negative on a booking nobody has paid; neither and the installment plan keeps
/// demanding money the customer no longer owes, which strands a balance that can never be collected
/// and a sale that can never be completed.
/// </para>
/// <para>
/// Each test here pins one end of that. They are written against the real services end to end,
/// because every one of these defects lived in the seam between two of them rather than inside any
/// one.
/// </para>
/// </summary>
public sealed class RebateSingleEffectTests
{
    private static readonly DateTime Jan = new(2026, 1, 15);
    private static readonly DateTime Feb = new(2026, 2, 15);
    private static readonly DateTime Mar = new(2026, 3, 15);
    private static readonly FinancialWorkflowActor Actor = new(7, "Finance Admin");

    /// <summary>
    /// The defect this suite exists for. A rebate granted as a credit and THEN written into the
    /// booking as a discount takes the same concession off the customer twice: the sale value drops
    /// by it and the balance drops by it again. The booking above ends with a NEGATIVE receivable —
    /// the company owing the customer money on a unit that has never been paid for — and a loss on
    /// a profitable sale.
    /// </summary>
    [Fact]
    public async Task ARebateAlreadyGrantedAsACredit_CannotBeGrantedAgainAsADiscount()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 10_000_000m);
        var booking = await context.Bookings.SingleAsync();
        booking.Status = BookingStatus.AwaitingBookingAmount;
        booking.BookingAmountRequired = 4_000_000m;
        await context.SaveChangesAsync();

        await ApplyRebateAsync(context, world.BookingId, 1_000_000m,
            CustomerRebateMethod.OutstandingBalanceReduction);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Bookings(context).UpdateBookingFinancialsAsync(world.BookingId, new UpdateBookingFinancialsDto
            {
                AgreedSalePrice = 10_000_000m, DiscountPercent = 95m, BookingAmountRequired = 400_000m
            }, 1));
        Assert.Contains("already settled", error.Message);

        // The terms are untouched, so the receivable this booking will raise is still the real one.
        Assert.Equal(10_000_000m, (await context.Bookings.AsNoTracking().SingleAsync()).AgreedSalePrice);
        Assert.Equal(0m, (await context.Bookings.AsNoTracking().SingleAsync()).DiscountAmount);
    }

    /// <summary>
    /// The price may still move — the guard is about the concession, not about freezing the booking.
    /// A discount that leaves the sale worth more than everything already settled on it is ordinary
    /// business and goes through.
    /// </summary>
    [Fact]
    public async Task APriceChangeTheSettledAmountStillFitsInside_IsAllowed()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 10_000_000m);
        var booking = await context.Bookings.SingleAsync();
        booking.Status = BookingStatus.AwaitingBookingAmount;
        booking.BookingAmountRequired = 4_000_000m;
        await context.SaveChangesAsync();

        await ApplyRebateAsync(context, world.BookingId, 1_000_000m,
            CustomerRebateMethod.OutstandingBalanceReduction);

        var updated = await Bookings(context).UpdateBookingFinancialsAsync(world.BookingId,
            new UpdateBookingFinancialsDto
            {
                AgreedSalePrice = 10_000_000m, DiscountPercent = 10m, BookingAmountRequired = 4_000_000m
            }, 1);
        Assert.Equal(9_000_000m, updated.NetSalePrice);
    }

    /// <summary>
    /// A rebate applied AFTER the plan is fixed. The plan cannot be regenerated once collection has
    /// started, so before this fix the credit reduced the balance and nothing else: the final
    /// installment could only ever be part-paid, Overdue reported the whole of it, and the sale could
    /// never be completed because completion requires every installment settled.
    /// </summary>
    [Fact]
    public async Task ARebateAppliedAfterThePlanIsFixed_ComesOffTheScheduleAndClearsTheReceivable()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 10_000_000m, bookingAmount: 4_000_000m);
        var installments = Installments(context);
        var schedule = await installments.GenerateScheduleAsync(world.BookingId, Plan(10_000_000m, 3), 1);
        Assert.Equal(6_000_000m, schedule.Items.Sum(i => i.Amount));

        // Collection begins, so the plan is now fixed.
        var first = schedule.Items.Single(i => i.SequenceNumber == 1);
        await installments.RecordInstallmentPaymentAsync(world.BookingId, first.Id, Receipt(world, 2_000_000m, Jan), 1);
        Assert.False((await installments.GetScheduleAsync(world.BookingId)).CanRegenerate);

        await ApplyRebateAsync(context, world.BookingId, 1_000_000m,
            CustomerRebateMethod.OutstandingBalanceReduction);

        // The concession comes off the tail of the plan, leaving the agreed dates alone.
        var credited = await installments.GetScheduleAsync(world.BookingId);
        Assert.Equal(3_000_000m, credited.ScheduleRemaining);
        Assert.Equal(1_000_000m, credited.Items.Single(i => i.SequenceNumber == 3).RemainingBalance);
        Assert.Equal(2_000_000m, credited.Items.Single(i => i.SequenceNumber == 2).RemainingBalance);

        await Bookings(context).GivePossessionAsync(world.BookingId, Feb, 1);
        foreach (var item in credited.Items.Where(i => i.RemainingBalance > 0m))
            await installments.RecordInstallmentPaymentAsync(world.BookingId, item.Id,
                Receipt(world, item.RemainingBalance, Mar), 1);

        // Nine million of cash and a one million credit settle a ten million sale, exactly once.
        var accounts = new FinanceAccountService(context);
        Assert.Equal(9_000_000m, (await accounts.GetByIdAsync(world.Bank.Id)).CurrentBalance);
        Assert.Equal(0m, (await accounts.GetByIdAsync(world.Receivables.Id)).CurrentBalance);
        Assert.Equal(0m, (await installments.GetScheduleAsync(world.BookingId)).ScheduleRemaining);

        var summary = await Finance(context).GetSummaryAsync(null, null, null);
        Assert.Equal(0m, summary.OutstandingAmount);
        Assert.Equal(0m, summary.OverdueAmount);

        // And the sale can actually finish, which it could not while an installment stayed unsettled.
        Assert.Equal(BookingStatus.SaleCompleted,
            (await Bookings(context).CompleteSaleAsync(world.BookingId, 1)).Status);
        await AssertBalancedAsync(context, Mar);
    }

    /// <summary>
    /// The other order, and the trap in fixing the first one. A plan built AFTER the rebate is
    /// already smaller by it — <c>RemainingInstallmentPool</c> takes it out of the pool — so
    /// allocating it onto that plan as well would take the same concession off twice and leave the
    /// company collecting a million less than the sale is worth.
    /// </summary>
    [Fact]
    public async Task ARebateAppliedBeforeThePlanIsBuilt_IsTakenOutOfThePoolAndNotAgainOffTheSchedule()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 10_000_000m, bookingAmount: 4_000_000m);
        await ApplyRebateAsync(context, world.BookingId, 1_000_000m,
            CustomerRebateMethod.OutstandingBalanceReduction);

        var installments = Installments(context);
        var schedule = await installments.GenerateScheduleAsync(world.BookingId, Plan(10_000_000m, 3), 1);
        Assert.Equal(5_000_000m, schedule.Items.Sum(i => i.Amount));
        // Nothing is credited on top: the plan already carries the reduction.
        Assert.Equal(5_000_000m, schedule.ScheduleRemaining);
        Assert.Empty(context.RebateCreditAllocations);

        await Bookings(context).GivePossessionAsync(world.BookingId, Feb, 1);
        foreach (var item in schedule.Items)
            await installments.RecordInstallmentPaymentAsync(world.BookingId, item.Id, Receipt(world, item.Amount, Mar), 1);

        var accounts = new FinanceAccountService(context);
        Assert.Equal(9_000_000m, (await accounts.GetByIdAsync(world.Bank.Id)).CurrentBalance);
        Assert.Equal(0m, (await accounts.GetByIdAsync(world.Receivables.Id)).CurrentBalance);
        await AssertBalancedAsync(context, Mar);
    }

    /// <summary>
    /// Regenerating the plan under an allocated credit must not double it either: the new pool is
    /// already net of the rebate, so the allocation has to be thrown away rather than carried over.
    /// </summary>
    [Fact]
    public async Task RegeneratingThePlanUnderAnAllocatedCredit_RebuildsTheAllocationRatherThanStackingIt()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 10_000_000m, bookingAmount: 4_000_000m);
        var installments = Installments(context);
        await installments.GenerateScheduleAsync(world.BookingId, Plan(10_000_000m, 3), 1);
        await ApplyRebateAsync(context, world.BookingId, 1_000_000m,
            CustomerRebateMethod.OutstandingBalanceReduction);
        Assert.NotEmpty(context.RebateCreditAllocations);

        // A booking-level credit must not cost the operator the ability to restructure the plan.
        Assert.True((await installments.GetScheduleAsync(world.BookingId)).CanRegenerate);
        var rebuilt = await installments.GenerateScheduleAsync(world.BookingId,
            Plan(10_000_000m, 2, regenerate: true), 1);

        Assert.Equal(5_000_000m, rebuilt.Items.Sum(i => i.Amount));
        Assert.Equal(5_000_000m, rebuilt.ScheduleRemaining);
        Assert.Empty(context.RebateCreditAllocations);
    }

    /// <summary>
    /// Reversing the credit puts the balance back on the plan. The allocation is rebuilt from the
    /// disbursement and reversal rows rather than unwound by hand, which is what lets a PARTIAL
    /// reversal land correctly without a special case.
    /// </summary>
    [Fact]
    public async Task ReversingPartOfACredit_PutsThatMuchBackOnTheSchedule()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 10_000_000m, bookingAmount: 4_000_000m);
        var installments = Installments(context);
        var schedule = await installments.GenerateScheduleAsync(world.BookingId, Plan(10_000_000m, 3), 1);
        await installments.RecordInstallmentPaymentAsync(world.BookingId,
            schedule.Items.Single(i => i.SequenceNumber == 1).Id, Receipt(world, 2_000_000m, Jan), 1);

        var service = CommissionRebates(context);
        var workspace = await ApplyRebateAsync(context, world.BookingId, 1_000_000m,
            CustomerRebateMethod.OutstandingBalanceReduction, service);
        Assert.Equal(3_000_000m, (await installments.GetScheduleAsync(world.BookingId)).ScheduleRemaining);

        var rebate = Assert.Single(workspace.Rebates);
        var disbursement = Assert.Single(rebate.Disbursements);
        await service.ReverseRebateDisbursementAsync(world.BookingId, rebate.Id, disbursement.Id,
            new ReverseMoneyMovementDto
            {
                Amount = 400_000m, Reason = "Benefit reduced", IdempotencyKey = "reverse-part"
            }, Actor);

        var after = await installments.GetScheduleAsync(world.BookingId);
        Assert.Equal(3_400_000m, after.ScheduleRemaining);
        Assert.Equal(1_400_000m, after.Items.Single(i => i.SequenceNumber == 3).RemainingBalance);
        Assert.Equal(600_000m, context.RebateCreditAllocations.Sum(a => a.Amount));
    }

    /// <summary>
    /// A credit larger than one installment spills onto the one before it, latest first, and stops
    /// at the schedule's edge — it never turns an installment negative and never allocates more than
    /// the credit itself.
    /// </summary>
    [Fact]
    public async Task ACreditLargerThanTheLastInstallment_SpillsBackwardsAndNeverOverAllocates()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 10_000_000m, bookingAmount: 4_000_000m);
        var installments = Installments(context);
        var schedule = await installments.GenerateScheduleAsync(world.BookingId, Plan(10_000_000m, 3), 1);
        await installments.RecordInstallmentPaymentAsync(world.BookingId,
            schedule.Items.Single(i => i.SequenceNumber == 1).Id, Receipt(world, 2_000_000m, Jan), 1);

        await ApplyRebateAsync(context, world.BookingId, 3_000_000m,
            CustomerRebateMethod.CreditNote, reference: "CN-BIG");

        var after = await installments.GetScheduleAsync(world.BookingId);
        Assert.Equal(1_000_000m, after.ScheduleRemaining);
        Assert.Equal(0m, after.Items.Single(i => i.SequenceNumber == 3).RemainingBalance);
        Assert.Equal(InstallmentStatus.Paid, after.Items.Single(i => i.SequenceNumber == 3).Status);
        Assert.Equal(1_000_000m, after.Items.Single(i => i.SequenceNumber == 2).RemainingBalance);
        Assert.Equal(3_000_000m, context.RebateCreditAllocations.Sum(a => a.Amount));
    }

    /// <summary>
    /// The allocation is a view of the credit, never a second copy of it. It must not leak into the
    /// booking-level credit total, the receivable, or the cost of the rebate in the P&amp;L — all of
    /// which already read the disbursement itself.
    /// </summary>
    [Fact]
    public async Task AnAllocatedCredit_IsStillCountedOnceInTheBalanceSheetAndTheProfitAndLoss()
    {
        await using var context = Context();
        var world = await SeedAsync(context, netSalePrice: 10_000_000m, bookingAmount: 4_000_000m);
        var installments = Installments(context);
        await installments.GenerateScheduleAsync(world.BookingId, Plan(10_000_000m, 3), 1);
        var workspace = await ApplyRebateAsync(context, world.BookingId, 1_000_000m,
            CustomerRebateMethod.OutstandingBalanceReduction);

        // Split across two installments, so a leak would show up as more than one million.
        Assert.True(context.RebateCreditAllocations.Count() >= 1);
        Assert.Equal(1_000_000m, workspace.RebateCredits);

        await Bookings(context).GivePossessionAsync(world.BookingId, Feb, 1);
        var finance = Finance(context);
        var pnl = await finance.GetProfitAndLossAsync(null, Jan.AddDays(-1), Mar);
        Assert.Equal(10_000_000m, pnl.TotalIncome);
        Assert.Equal(1_000_000m, pnl.TotalExpenses);
        Assert.Equal(9_000_000m, pnl.NetProfit);
        // Ten million recognised, four million collected, one million credited.
        Assert.Equal(5_000_000m, (await new FinanceAccountService(context).GetByIdAsync(world.Receivables.Id)).CurrentBalance);
        await AssertBalancedAsync(context, Mar);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    private sealed record World(int BookingId, FinanceAccount Bank, FinanceAccount Deposits, FinanceAccount Receivables);

    private static async Task<BookingCommissionRebateWorkspaceDto> ApplyRebateAsync(
        AppDbContext context, int bookingId, decimal amount, CustomerRebateMethod method,
        CommissionRebateService? service = null, string? reference = null)
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
            Method = method, Amount = amount, AppliedAt = Jan, Reference = reference,
            IdempotencyKey = $"apply-{bookingId}-{amount}", RebateConcurrencyToken = rebate.ConcurrencyToken
        }, Actor);
    }

    private static GenerateInstallmentPlanDto Plan(decimal price, int count, bool regenerate = false) => new()
    {
        AgreedSalePrice = price, DiscountPercent = 0m, Frequency = InstallmentFrequency.Monthly,
        NumberOfInstallments = count, InstallmentStartDate = Jan, Regenerate = regenerate
    };

    private static RecordInstallmentPaymentDto Receipt(World world, decimal amount, DateTime paidAt) => new()
    {
        Amount = amount, FinanceAccountId = world.Bank.Id, PaymentMethod = PaymentMethod.BankTransfer,
        PaidAt = paidAt, PaymentReference = $"TT-{Guid.NewGuid():N}"[..12]
    };

    private static async Task<World> SeedAsync(AppDbContext context, decimal netSalePrice, decimal bookingAmount = 0m)
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
        var project = new Project { ProjectName = "Floria Heights", Location = "Islamabad", CreatedById = 1 };
        var unit = new Unit { Project = project, UnitNumber = "A-1", UnitType = "Apartment", Price = netSalePrice, Status = UnitStatus.OnPaymentPlan };
        var customer = new Customer { FullName = "Buyer", Phone = "03001112222", Status = CustomerStatus.Active };
        var booking = new Booking
        {
            BookingReference = "BK-REB", Customer = customer, Unit = unit, Source = CustomerSource.Referral,
            Status = BookingStatus.PaymentPlanActive, AgreedSalePrice = netSalePrice, DiscountAmount = 0m,
            BookingAmountRequired = bookingAmount, BookingAmountReceived = bookingAmount, BookingDate = Jan
        };
        if (bookingAmount > 0m)
            booking.Payments.Add(new Payment
            {
                Booking = booking, Amount = bookingAmount, Type = PaymentType.BookingAmount,
                PaymentMethod = PaymentMethod.BankTransfer, PaidAt = Jan
            });
        context.AddRange(bank, deposits, receivables, project, unit, customer, booking);
        await context.SaveChangesAsync();
        foreach (var payment in booking.Payments) payment.FinanceAccountId = bank.Id;
        await context.SaveChangesAsync();
        return new World(booking.Id, bank, deposits, receivables);
    }

    private static async Task AssertBalancedAsync(AppDbContext context, DateTime asAt)
    {
        var sheet = await Finance(context).GetBalanceSheetAsync(null, asAt);
        Assert.True(sheet.IsBalanced, $"Balance sheet out by {sheet.Imbalance}: {string.Join(", ", sheet.UnbalancedAccounts ?? [])}");
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
