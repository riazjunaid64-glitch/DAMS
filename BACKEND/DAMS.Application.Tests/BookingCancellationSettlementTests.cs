using DAMS.Application.Common;
using DAMS.Application.DTOs.BookingDtos;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

public sealed class BookingCancellationSettlementTests
{
    private static readonly FinancialWorkflowActor Actor = new(42, "Finance Admin");

    // ── Core settlement outcomes ────────────────────────────────────────────

    [Fact]
    public async Task Cancel_WithNoPayments_RequiresNoRefund()
    {
        var h = await Harness.Create(paid: 0m);
        var result = await h.Service.CancelBookingAsync(h.BookingId, h.CancelDto(0m, 0m, CancellationRefundDecision.None), Actor);

        Assert.Equal(BookingStatus.Cancelled, result.Status);
        Assert.Equal(UnitStatus.Available, h.Context.Units.Single().Status);
        var settlement = result.CancellationSettlement!;
        Assert.Equal(0m, settlement.CustomerCashReceivedSnapshot);
        Assert.Equal(0m, settlement.RefundAmount);
        Assert.Equal(0m, settlement.RetainedAmount);
        Assert.Equal(CancellationRefundStatus.NotRequired, settlement.RefundStatus);
        Assert.Null(settlement.RefundPayableAccountId);
    }

    [Fact]
    public async Task Cancel_FullRefundPayNow_ClearsPayableImmediately()
    {
        var h = await Harness.Create(paid: 500_000m);
        var result = await h.Service.CancelBookingAsync(h.BookingId,
            h.CancelDto(500_000m, 500_000m, CancellationRefundDecision.PayNow, payNow: true), Actor);

        var settlement = result.CancellationSettlement!;
        Assert.Equal(500_000m, settlement.RefundAmount);
        Assert.Equal(0m, settlement.RetainedAmount);
        Assert.Equal(CancellationRefundStatus.Paid, settlement.RefundStatus);
        Assert.Equal(500_000m, settlement.Refund!.Amount);
        Assert.NotNull(settlement.RefundPayableAccountId);

        var payable = h.Context.FinanceAccounts.Single(a => a.SystemRole == FinanceSystemAccountRole.CustomerRefundPayable);
        Assert.Equal(FinanceAccountType.Liability, payable.Type);
    }

    [Fact]
    public async Task Cancel_PartialRefundPayNow_RetainsRemainder()
    {
        var h = await Harness.Create(paid: 500_000m);
        var result = await h.Service.CancelBookingAsync(h.BookingId,
            h.CancelDto(500_000m, 450_000m, CancellationRefundDecision.PayNow, payNow: true), Actor);

        var settlement = result.CancellationSettlement!;
        Assert.Equal(450_000m, settlement.RefundAmount);
        Assert.Equal(50_000m, settlement.RetainedAmount);
    }

    [Fact]
    public async Task Cancel_ZeroRefund_FullRetention_NoPayoutAccountRequired()
    {
        var h = await Harness.Create(paid: 500_000m);
        var result = await h.Service.CancelBookingAsync(h.BookingId,
            h.CancelDto(500_000m, 0m, CancellationRefundDecision.None), Actor);

        var settlement = result.CancellationSettlement!;
        Assert.Equal(500_000m, settlement.RetainedAmount);
        Assert.Equal(CancellationRefundStatus.NotRequired, settlement.RefundStatus);
        Assert.Null(settlement.RefundPayableAccountId);
    }

    [Fact]
    public async Task Cancel_PayLater_RecognisesLiability_WithoutMovingCash()
    {
        var h = await Harness.Create(paid: 500_000m);
        var result = await h.Service.CancelBookingAsync(h.BookingId,
            h.CancelDto(500_000m, 450_000m, CancellationRefundDecision.PayLater), Actor);

        var settlement = result.CancellationSettlement!;
        Assert.Equal(CancellationRefundDecision.PayLater, settlement.RefundDecision);
        Assert.Equal(CancellationRefundStatus.Pending, settlement.RefundStatus);
        Assert.Null(settlement.Refund);
        Assert.NotNull(settlement.RefundPayableAccountId);
        Assert.False(await h.Context.BookingCancellationRefunds.AnyAsync());
    }

    [Fact]
    public async Task PayPendingRefund_MarksPaid_AndCannotBePaidTwice()
    {
        var h = await Harness.Create(paid: 500_000m);
        await h.Service.CancelBookingAsync(h.BookingId,
            h.CancelDto(500_000m, 450_000m, CancellationRefundDecision.PayLater), Actor);

        var pay = new PayCancellationRefundDto
        {
            FinanceAccountId = h.CashAccountId, PaymentMethod = PaymentMethod.Cash,
            PaidAt = DateTime.UtcNow, IdempotencyKey = "pay-1"
        };
        var result = await h.Service.PayCancellationRefundAsync(h.BookingId, pay, Actor);
        Assert.Equal(CancellationRefundStatus.Paid, result.CancellationSettlement!.RefundStatus);
        Assert.Equal(450_000m, result.CancellationSettlement.Refund!.Amount);

        var again = new PayCancellationRefundDto
        {
            FinanceAccountId = h.CashAccountId, PaymentMethod = PaymentMethod.Cash,
            PaidAt = DateTime.UtcNow, IdempotencyKey = "pay-2"
        };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.PayCancellationRefundAsync(h.BookingId, again, Actor));
        Assert.Contains("already been paid", error.Message);
    }

    // ── Validation ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancel_RefundGreaterThanPaid_IsRejected()
    {
        var h = await Harness.Create(paid: 500_000m);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Service.CancelBookingAsync(h.BookingId, h.CancelDto(500_000m, 600_000m, CancellationRefundDecision.PayLater), Actor));
        Assert.Contains("cannot exceed", error.Message);
    }

    [Fact]
    public async Task Cancel_NegativeRefund_IsRejected()
    {
        var h = await Harness.Create(paid: 500_000m);
        var dto = h.CancelDto(500_000m, 0m, CancellationRefundDecision.None);
        dto.RefundAmount = -1m;
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.CancelBookingAsync(h.BookingId, dto, Actor));
    }

    [Fact]
    public async Task Cancel_PayLaterWithPayoutFields_IsRejected()
    {
        var h = await Harness.Create(paid: 500_000m);
        var dto = h.CancelDto(500_000m, 450_000m, CancellationRefundDecision.PayLater);
        dto.RefundFinanceAccountId = h.CashAccountId;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.CancelBookingAsync(h.BookingId, dto, Actor));
        Assert.Contains("must not include payout details", error.Message);
    }

    [Fact]
    public async Task Cancel_PayNowWithoutAccount_IsRejected()
    {
        var h = await Harness.Create(paid: 500_000m);
        var dto = h.CancelDto(500_000m, 450_000m, CancellationRefundDecision.PayNow);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.CancelBookingAsync(h.BookingId, dto, Actor));
        Assert.Contains("refund source account is required", error.Message);
    }

    [Fact]
    public async Task Cancel_NonCashRefund_WithoutReference_IsRejected()
    {
        var h = await Harness.Create(paid: 500_000m);
        var dto = h.CancelDto(500_000m, 450_000m, CancellationRefundDecision.PayNow, payNow: true);
        dto.RefundPaymentMethod = PaymentMethod.BankTransfer;
        dto.RefundPaymentReference = null;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.CancelBookingAsync(h.BookingId, dto, Actor));
        Assert.Contains("reference is required", error.Message);
    }

    [Fact]
    public async Task Cancel_RefundFromLiabilityAccount_IsRejected()
    {
        var h = await Harness.Create(paid: 500_000m);
        var liability = new FinanceAccount { Name = "Some Liability", Type = FinanceAccountType.Liability, AccountHolderName = "Co", IsActive = true };
        h.Context.FinanceAccounts.Add(liability);
        await h.Context.SaveChangesAsync();

        var dto = h.CancelDto(500_000m, 450_000m, CancellationRefundDecision.PayNow, payNow: true);
        dto.RefundFinanceAccountId = liability.Id;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.CancelBookingAsync(h.BookingId, dto, Actor));
        Assert.Contains("cash-like", error.Message);
    }

    [Fact]
    public async Task Cancel_RefundFromStaffFloat_IsRejected()
    {
        var h = await Harness.Create(paid: 500_000m);
        var staffFloat = new FinanceAccount { Name = "Staff Float", Type = FinanceAccountType.StaffFloat, AccountHolderName = "Employee", IsActive = true };
        h.Context.FinanceAccounts.Add(staffFloat);
        await h.Context.SaveChangesAsync();

        var dto = h.CancelDto(500_000m, 450_000m, CancellationRefundDecision.PayNow, payNow: true);
        dto.RefundFinanceAccountId = staffFloat.Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.CancelBookingAsync(h.BookingId, dto, Actor));
    }

    [Fact]
    public async Task Cancel_IsBlocked_AfterPossessionGiven()
    {
        var h = await Harness.Create(paid: 0m, status: BookingStatus.PossessionGiven);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Service.CancelBookingAsync(h.BookingId, h.CancelDto(0m, 0m, CancellationRefundDecision.None), Actor));
        Assert.Contains("possession or completion", error.Message);
    }

    [Fact]
    public async Task Cancel_IsBlocked_AfterSaleCompleted()
    {
        var h = await Harness.Create(paid: 0m, status: BookingStatus.SaleCompleted);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Service.CancelBookingAsync(h.BookingId, h.CancelDto(0m, 0m, CancellationRefundDecision.None), Actor));
    }

    [Fact]
    public async Task Cancel_AlreadyCancelledBooking_IsRejected()
    {
        var h = await Harness.Create(paid: 0m);
        await h.Service.CancelBookingAsync(h.BookingId, h.CancelDto(0m, 0m, CancellationRefundDecision.None, key: "first"), Actor);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Service.CancelBookingAsync(h.BookingId, h.CancelDto(0m, 0m, CancellationRefundDecision.None, key: "second"), Actor));
        Assert.Contains("already been cancelled", error.Message);
    }

    // ── Idempotency & concurrency ───────────────────────────────────────────

    [Fact]
    public async Task Cancel_ExactRetryWithSameKey_ReturnsOriginalResult_WithoutDuplicating()
    {
        var h = await Harness.Create(paid: 500_000m);
        var dto = h.CancelDto(500_000m, 450_000m, CancellationRefundDecision.PayLater, key: "retry-key");
        var first = await h.Service.CancelBookingAsync(h.BookingId, dto, Actor);
        var second = await h.Service.CancelBookingAsync(h.BookingId, dto, Actor);

        Assert.Equal(first.CancellationSettlement!.Id, second.CancellationSettlement!.Id);
        Assert.Single(h.Context.BookingCancellationSettlements);
    }

    [Fact]
    public async Task Cancel_SameKeyDifferentPayload_IsRejected()
    {
        var h = await Harness.Create(paid: 500_000m);
        var dto = h.CancelDto(500_000m, 450_000m, CancellationRefundDecision.PayLater, key: "shared-key");
        await h.Service.CancelBookingAsync(h.BookingId, dto, Actor);

        var differentDto = h.CancelDto(500_000m, 400_000m, CancellationRefundDecision.PayLater, key: "shared-key");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.CancelBookingAsync(h.BookingId, differentDto, Actor));
        Assert.Contains("different cancellation", error.Message);
    }

    [Fact]
    public async Task Cancel_StaleCashSnapshot_IsRejected_AndDoesNotSilentlyRecompute()
    {
        var h = await Harness.Create(paid: 500_000m);
        var dto = h.CancelDto(500_000m, 500_000m, CancellationRefundDecision.PayLater);

        // Another admin records a further payment after the dialog captured its snapshot.
        h.Context.Payments.Add(new Payment { BookingId = h.BookingId, Amount = 100_000m, Type = PaymentType.Installment, PaymentMethod = PaymentMethod.Cash });
        await h.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.CancelBookingAsync(h.BookingId, dto, Actor));
        Assert.Contains("Reload and review", error.Message);
        Assert.False(await h.Context.BookingCancellationSettlements.AnyAsync());
    }

    [Fact]
    public async Task Cancel_StaleConcurrencyToken_IsRejected()
    {
        var h = await Harness.Create(paid: 0m);
        var dto = h.CancelDto(0m, 0m, CancellationRefundDecision.None);
        dto.ConcurrencyToken = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => h.Service.CancelBookingAsync(h.BookingId, dto, Actor));
    }

    // ── History & side effects ──────────────────────────────────────────────

    [Fact]
    public async Task Cancel_LeavesOriginalPaymentRowsUntouched()
    {
        var h = await Harness.Create(paid: 500_000m);
        var paymentIdsBefore = await h.Context.Payments.Select(p => new { p.Id, p.Amount }).ToListAsync();

        await h.Service.CancelBookingAsync(h.BookingId, h.CancelDto(500_000m, 200_000m, CancellationRefundDecision.PayLater), Actor);

        var paymentsAfter = await h.Context.Payments.Select(p => new { p.Id, p.Amount }).ToListAsync();
        Assert.Equal(paymentIdsBefore, paymentsAfter);
    }

    [Fact]
    public async Task Cancel_RunsCommissionCancellationLifecycle_ExactlyOnce()
    {
        var storage = new NullEvidenceStorage();
        var h = await Harness.Create(paid: 0m, withCommissionLifecycle: storage);

        var partner = new ThirdPartyPartner { Name = "Broker", IsActive = true, PartnerType = "Broker", InternalCode = "BRK-1" };
        h.Context.ThirdPartyPartners.Add(partner);
        await h.Context.SaveChangesAsync();
        var commission = new BookingCommission
        {
            BookingId = h.BookingId, PartnerId = partner.Id, CalculationType = FinancialCalculationType.Percentage,
            CalculationBasis = FinancialCalculationBasis.NetSalePriceAfterDiscount, BasisAmount = 1000m, CalculatedAmount = 100m,
            FinalAmount = 100m, ApprovedAmount = 100m, Status = BookingCommissionStatus.Payable
        };
        h.Context.BookingCommissions.Add(commission);
        await h.Context.SaveChangesAsync();

        await h.Service.CancelBookingAsync(h.BookingId, h.CancelDto(0m, 0m, CancellationRefundDecision.None), Actor);

        var saved = await h.Context.BookingCommissions.SingleAsync(c => c.Id == commission.Id);
        Assert.Equal(BookingCommissionStatus.Cancelled, saved.Status);
    }

    [Fact]
    public async Task LegacyCancelledBooking_WithoutSettlement_MapsToNullSettlement()
    {
        var h = await Harness.Create(paid: 0m);
        var booking = await h.Context.Bookings.SingleAsync();
        booking.Status = BookingStatus.Cancelled;
        booking.Unit.Status = UnitStatus.Available;
        await h.Context.SaveChangesAsync();

        var response = await h.Service.GetBookingByIdAsync(h.BookingId);
        Assert.Equal(BookingStatus.Cancelled, response!.Status);
        Assert.Null(response.CancellationSettlement);
    }

    [Fact]
    public async Task Settlement_And_Refund_AreImmutable()
    {
        var h = await Harness.Create(paid: 500_000m);
        var result = await h.Service.CancelBookingAsync(h.BookingId,
            h.CancelDto(500_000m, 450_000m, CancellationRefundDecision.PayNow, payNow: true), Actor);

        var settlement = await h.Context.BookingCancellationSettlements.SingleAsync();
        settlement.RefundAmount = 1m;
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Context.SaveChangesAsync());

        h.Context.Entry(settlement).Reload();
        var refund = await h.Context.BookingCancellationRefunds.SingleAsync();
        h.Context.BookingCancellationRefunds.Remove(refund);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task EnsureSystemAccountAsync_CreatesAccountOnce_AndReusesIt()
    {
        var h = await Harness.Create(paid: 0m);
        var accountService = new FinanceAccountService(h.Context);
        var first = await accountService.EnsureSystemAccountAsync(FinanceSystemAccountRole.CustomerRefundPayable);
        var second = await accountService.EnsureSystemAccountAsync(FinanceSystemAccountRole.CustomerRefundPayable);
        Assert.Equal(first, second);
        Assert.Single(h.Context.FinanceAccounts.Where(a => a.SystemRole == FinanceSystemAccountRole.CustomerRefundPayable));
    }

    // ── Harness ──────────────────────────────────────────────────────────────

    private sealed class Harness
    {
        public AppDbContext Context { get; }
        public BookingService Service { get; }
        public int BookingId { get; private set; }
        public int CashAccountId { get; private set; }

        private Harness(AppDbContext context, BookingService service) { Context = context; Service = service; }

        public static async Task<Harness> Create(decimal paid, BookingStatus status = BookingStatus.PaymentPlanActive, NullEvidenceStorage? withCommissionLifecycle = null)
        {
            var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            context.Database.EnsureCreated();

            var accountService = new FinanceAccountService(context);
            var commissionLifecycle = withCommissionLifecycle != null
                ? new CommissionRebateService(context, accountService, withCommissionLifecycle)
                : null;
            var service = new BookingService(context, new CustomerService(context), accountService,
                notifications: null, commissionLifecycle: commissionLifecycle);
            var harness = new Harness(context, service);

            var project = new Project { ProjectName = "Cancellation Test", Location = "Karachi", CreatedById = 1 };
            var unit = new Unit { Project = project, UnitNumber = $"U-{Guid.NewGuid():N}"[..8], UnitType = "Apartment", Price = 1_000_000m, Status = UnitStatus.OnPaymentPlan };
            var customer = new Customer { FullName = "Buyer", Phone = "03001112222", Status = CustomerStatus.Active };
            var booking = new Booking
            {
                BookingReference = $"BK-{Guid.NewGuid():N}", Customer = customer, Unit = unit,
                Status = status, Source = CustomerSource.Referral,
                AgreedSalePrice = 1_000_000m, DiscountAmount = 0m, BookingAmountRequired = 500_000m,
                BookingAmountReceived = paid, BookingDate = DateTime.UtcNow
            };
            if (paid > 0m)
                booking.Payments.Add(new Payment { Booking = booking, Amount = paid, Type = PaymentType.BookingAmount, PaymentMethod = PaymentMethod.Cash });

            var cash = new FinanceAccount { Name = "Cash Account", Type = FinanceAccountType.Cash, AccountHolderName = "Company", IsActive = true };
            context.AddRange(project, unit, customer, booking, cash);
            await context.SaveChangesAsync();

            harness.BookingId = booking.Id;
            harness.CashAccountId = cash.Id;
            return harness;
        }

        public CancelBookingDto CancelDto(decimal expectedCash, decimal refundAmount, CancellationRefundDecision decision,
            bool payNow = false, string key = "cancel-key") => new()
        {
            Reason = "Customer requested cancellation",
            ExpectedCustomerCashReceived = expectedCash,
            RefundAmount = refundAmount,
            RefundDecision = decision,
            IdempotencyKey = key,
            RefundFinanceAccountId = payNow ? CashAccountId : null,
            RefundPaymentMethod = payNow ? PaymentMethod.Cash : null,
            RefundPaidAt = payNow ? DateTime.UtcNow : null
        };
    }

    private sealed class NullEvidenceStorage : IFinancialEvidenceStorage
    {
        public Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default) =>
            Task.FromResult(Guid.NewGuid().ToString());
        public Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(null);
        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
