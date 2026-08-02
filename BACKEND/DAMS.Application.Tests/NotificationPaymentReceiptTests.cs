using DAMS.Application.Common;
using DAMS.Application.DTOs.BookingDtos;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// The payment receipt is the notification platform's most consequential job: it carries
/// money, it is contractual, and it must never be sent twice or block a payment.
/// </summary>
public sealed class NotificationPaymentReceiptTests
{
    [Fact]
    public async Task RecordingAPaymentCreatesExactlyOneReceiptNotificationForTheCustomer()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();

        var paymentId = await h.RecordPaymentAsync(250_000m);

        var notifications = await h.NotificationsAsync();
        var receipt = Assert.Single(notifications, n => n.Type == NotificationType.PaymentReceipt);

        Assert.Equal(h.CustomerUserId, receipt.RecipientUserId);
        Assert.Equal(NotificationEntityType.Payment, receipt.EntityType);
        Assert.Equal(paymentId, receipt.EntityId);
        Assert.Equal($"/receipt/{h.BookingId}/{paymentId}", receipt.DeepLink);
    }

    [Fact]
    public async Task TheReceiptCarriesTheOfficialPaymentDataRatherThanRecalculatingIt()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();

        var paymentId = await h.RecordPaymentAsync(312_500m);
        var payment = await h.Db.Payments.AsNoTracking().FirstAsync(p => p.Id == paymentId);

        var receipt = await h.Db.Notifications.AsNoTracking()
            .FirstAsync(n => n.Type == NotificationType.PaymentReceipt);

        Assert.NotNull(payment.ReceiptNumber);
        Assert.Contains(payment.ReceiptNumber!, receipt.Title);
        Assert.Contains("312,500.00", receipt.DataJson);
        Assert.Contains(payment.ReceiptNumber!, receipt.DataJson);
        Assert.Contains("BK-000001", receipt.DataJson);

        // The receipt document is built from the booking service's own projection, so the
        // attachment and the on-screen receipt cannot disagree.
        var projection = await h.Bookings.GetPaymentReceiptAsync(h.BookingId, paymentId);
        Assert.Equal(payment.Amount, projection.Amount);
        Assert.Equal(payment.ReceiptNumber, projection.ReceiptNumber);
    }

    [Fact]
    public async Task ARepeatedPaymentEventNeverProducesASecondReceiptNotification()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();

        var paymentId = await h.RecordPaymentAsync();

        // Every way the same event can arrive twice: an impatient second click, a background
        // reconciliation sweep, and an explicit re-raise.
        await h.Events.NotifyPaymentRecordedAsync(paymentId);
        await h.Events.NotifyPaymentRecordedAsync(paymentId);
        await h.Events.ReconcilePaymentReceiptsAsync(50);

        var receipts = (await h.NotificationsAsync()).Count(n => n.Type == NotificationType.PaymentReceipt);
        Assert.Equal(1, receipts);
    }

    [Fact]
    public async Task AnEmailProviderOutageDoesNotUndoThePayment()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();
        h.Email.TransientFailuresRemaining = 99;

        var paymentId = await h.RecordPaymentAsync(400_000m);
        await h.Processor.ProcessDueDeliveriesAsync(20);

        // The money is recorded and the booking has moved on, whatever the provider did.
        var payment = await h.Db.Payments.AsNoTracking().FirstAsync(p => p.Id == paymentId);
        Assert.Equal(400_000m, payment.Amount);
        Assert.NotNull(payment.ReceiptNumber);

        var booking = await h.Db.Bookings.AsNoTracking().FirstAsync(b => b.Id == h.BookingId);
        Assert.Equal(400_000m, booking.BookingAmountReceived);

        // And the failure is visible rather than silently swallowed.
        var email = Assert.Single(await h.DeliveriesForAsync(NotificationChannel.Email));
        Assert.Equal(NotificationDeliveryStatus.Retrying, email.Status);
        Assert.Equal(1, email.AttemptCount);
        Assert.Contains("connection refused", email.FailureReason);
    }

    [Fact]
    public async Task ARetryAfterAnOutageSendsTheEmailOnceAndDoesNotRepeatTheChannelsThatWorked()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();
        await h.AddPushSubscriptionAsync(h.CustomerUserId, "device-1");

        // The first attempt: push succeeds, email does not.
        h.Email.TransientFailuresRemaining = 1;
        await h.RecordPaymentAsync();
        await h.Processor.ProcessDueDeliveriesAsync(20);

        Assert.Single(h.PushSender.Sent);
        Assert.Empty(h.Email.Sent);

        // Move past the back-off and sweep again.
        h.Clock.Set(h.Clock.UtcNow.AddMinutes(10));
        await h.Processor.ProcessDueDeliveriesAsync(20);

        // Exactly one email, and no second push: success is recorded per channel.
        Assert.Single(h.Email.Sent);
        Assert.Single(h.PushSender.Sent);

        var deliveries = await h.DeliveriesAsync();
        Assert.All(deliveries, d => Assert.Equal(NotificationDeliveryStatus.Sent, d.Status));

        // Sweeping again changes nothing.
        h.Clock.Set(h.Clock.UtcNow.AddMinutes(10));
        await h.Processor.ProcessDueDeliveriesAsync(20);
        Assert.Single(h.Email.Sent);
        Assert.Single(h.PushSender.Sent);
    }

    [Fact]
    public async Task AProviderThatKeepsFailingIsGivenUpOnAndBecomesVisibleToAdmin()
    {
        await using var h = await NotificationTestHarness.CreateAsync(new NotificationOptions { MaxAttempts = 3 });
        await h.EnableChannelsAsync();
        h.Email.TransientFailuresRemaining = 99;

        await h.RecordPaymentAsync();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await h.Processor.ProcessDueDeliveriesAsync(20);
            h.Clock.Set(h.Clock.UtcNow.AddHours(2));
        }

        var email = Assert.Single(await h.DeliveriesForAsync(NotificationChannel.Email));
        Assert.Equal(NotificationDeliveryStatus.Failed, email.Status);
        Assert.True(email.IsPermanentFailure);
        Assert.Equal(3, email.AttemptCount);
        Assert.Contains("gave up after 3 attempts", email.FailureReason);

        // Permanently failed rows are never picked up again.
        h.Clock.Set(h.Clock.UtcNow.AddDays(1));
        Assert.Equal(0, await h.Processor.ProcessDueDeliveriesAsync(20));

        var history = await h.Admin.GetDeliveryHistoryAsync(new DTOs.NotificationDtos.DeliveryHistoryFilterDto
        {
            FailuresOnly = true
        });
        Assert.Contains(history.Items, i => i.Channel == NotificationChannel.Email && i.CanRetry);
    }

    [Fact]
    public async Task AHardBounceSuppressesTheAddressAndIsNotRetried()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();
        h.Email.FailAsHardBounce = true;

        await h.RecordPaymentAsync();
        await h.Processor.ProcessDueDeliveriesAsync(20);

        var email = Assert.Single(await h.DeliveriesForAsync(NotificationChannel.Email));
        Assert.Equal(NotificationDeliveryStatus.Bounced, email.Status);
        Assert.True(email.IsPermanentFailure);

        var suppression = Assert.Single(await h.Db.EmailSuppressions.AsNoTracking().ToListAsync());
        Assert.Equal("client@dams.test", suppression.Email);

        // An admin retry clears the block so a corrected address can be tried again.
        h.Email.FailAsHardBounce = false;
        await h.Admin.RetryDeliveryAsync(email.Id, h.AdminCtx);
        await h.Processor.ProcessDueDeliveriesAsync(20);

        Assert.Single(h.Email.Sent);
        Assert.Null(await h.Db.EmailSuppressions.AsNoTracking()
            .Where(s => s.ClearedAt == null)
            .Select(s => (int?)s.Id)
            .FirstOrDefaultAsync());
    }

    [Fact]
    public async Task ACustomerWithNoLoginStillReceivesTheirReceiptByEmail()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();

        var booking = await CreateBookingForAsync(h, h.LoginlessCustomerId, h.SecondUnitId, "BK-000002");
        await h.Bookings.RecordBookingAmountPaymentAsync(booking,
            new RecordBookingAmountPaymentDto { Amount = 100_000m, PaymentMethod = PaymentMethod.Cash }, h.AdminUserId);

        var receipt = await h.Db.Notifications.AsNoTracking()
            .FirstAsync(n => n.Type == NotificationType.PaymentReceipt);

        Assert.Null(receipt.RecipientUserId);
        Assert.Equal("walkin@dams.test", receipt.RecipientEmail);

        // No account means no inbox and no browser to push to — email only.
        var deliveries = await h.Db.NotificationDeliveries.AsNoTracking()
            .Where(d => d.NotificationId == receipt.Id)
            .ToListAsync();
        Assert.Single(deliveries);
        Assert.Equal(NotificationChannel.Email, deliveries[0].Channel);

        await h.Processor.ProcessDueDeliveriesAsync(20);
        Assert.Single(h.Email.Sent);
        Assert.Equal("walkin@dams.test", h.Email.Sent[0].To);
    }

    [Fact]
    public async Task AContactOnlyReceiptIsNotSentToAnAddressThatNoLongerOwnsThePayment()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();

        var booking = await CreateBookingForAsync(h, h.LoginlessCustomerId, h.SecondUnitId, "BK-000099");
        await h.Bookings.RecordBookingAmountPaymentAsync(booking,
            new RecordBookingAmountPaymentDto { Amount = 100_000m, PaymentMethod = PaymentMethod.Cash }, h.AdminUserId);

        var customer = await h.Db.Customers.SingleAsync(item => item.Id == h.LoginlessCustomerId);
        customer.Email = "corrected@dams.test";
        await h.Db.SaveChangesAsync();

        await h.Processor.ProcessDueDeliveriesAsync(20);

        Assert.Empty(h.Email.Sent);
        var delivery = Assert.Single(await h.DeliveriesForAsync(NotificationChannel.Email));
        Assert.Equal(NotificationDeliveryStatus.Skipped, delivery.Status);
        Assert.True(delivery.IsPermanentFailure);
    }

    [Fact]
    public async Task ACustomerWithNoAddressIsRecordedAsUnavailableRatherThanRetriedForever()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();

        var customer = await h.Db.Customers.FirstAsync(c => c.Id == h.CustomerId);
        customer.Email = null;
        var user = await h.Db.Users.FirstAsync(u => u.UserId == h.CustomerUserId);
        user.Email = "not-an-address";
        await h.Db.SaveChangesAsync();

        await h.RecordPaymentAsync();
        await h.Processor.ProcessDueDeliveriesAsync(20);

        var email = Assert.Single(await h.DeliveriesForAsync(NotificationChannel.Email));
        Assert.Equal(NotificationDeliveryStatus.Unavailable, email.Status);
        Assert.True(email.IsPermanentFailure);
        Assert.Contains("no valid email address", email.FailureReason);

        // The payment stands and the in-app notification is still there.
        Assert.Single(await h.Db.Payments.AsNoTracking().Where(p => p.BookingId == h.BookingId).ToListAsync());
        var inApp = Assert.Single(await h.DeliveriesForAsync(NotificationChannel.InApp));
        Assert.Equal(NotificationDeliveryStatus.Sent, inApp.Status);
    }

    [Fact]
    public async Task ReconciliationRaisesAReceiptThatWasLostBetweenCommitAndNotification()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();

        // Simulates the application dying between the payment commit and its notification.
        await h.RecordPaymentAsync();
        var receipt = await h.Db.Notifications.FirstAsync(n => n.Type == NotificationType.PaymentReceipt);
        var payment = await h.Db.Payments.SingleAsync();
        payment.CreatedAt = DateTime.UtcNow.AddYears(-2);
        payment.PaidAt = payment.CreatedAt;
        h.Db.Notifications.Remove(receipt);
        await h.Db.SaveChangesAsync();

        var recovered = await h.Events.ReconcilePaymentReceiptsAsync(50);

        Assert.Equal(1, recovered);
        Assert.Single(await h.Db.Notifications.AsNoTracking()
            .Where(n => n.Type == NotificationType.PaymentReceipt)
            .ToListAsync());
    }

    [Fact]
    public async Task InstallmentPaymentsAlsoRaiseAReceipt()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();

        await h.RecordPaymentAsync(1_000_000m);

        var booking = await h.Db.Bookings.FirstAsync(b => b.Id == h.BookingId);
        Assert.Equal(BookingStatus.PaymentPlanActive, booking.Status);

        var installment = new Domain.Entities.Installment
        {
            BookingId = h.BookingId,
            SequenceNumber = 1,
            Amount = 500_000m,
            DueDate = DateTime.UtcNow.AddDays(30),
            Status = InstallmentStatus.Pending,
            Type = InstallmentType.Regular
        };
        h.Db.Installments.Add(installment);
        await h.Db.SaveChangesAsync();

        await h.Installments.RecordInstallmentPaymentAsync(h.BookingId, installment.Id,
            new DTOs.InstallmentDtos.RecordInstallmentPaymentDto
            {
                Amount = 500_000m,
                PaymentMethod = PaymentMethod.BankTransfer
            }, h.AdminUserId);

        var receipts = await h.Db.Notifications.AsNoTracking()
            .Where(n => n.Type == NotificationType.PaymentReceipt)
            .ToListAsync();

        Assert.Equal(2, receipts.Count);
        Assert.All(receipts, r => Assert.Equal(h.CustomerUserId, r.RecipientUserId));
    }

    internal static async Task<int> CreateBookingForAsync(NotificationTestHarness h, int customerId, int unitId, string reference)
    {
        var booking = new Domain.Entities.Booking
        {
            BookingReference = reference,
            CustomerId = customerId,
            UnitId = unitId,
            Status = BookingStatus.AwaitingBookingAmount,
            ListPrice = 5_000_000m,
            AgreedSalePrice = 5_000_000m,
            BookingAmountRequired = 500_000m,
            TotalInstallmentAmount = 4_500_000m,
            BookingDate = DateTime.UtcNow,
            CreatedByUserId = h.AdminUserId
        };

        h.Db.Bookings.Add(booking);
        await h.Db.SaveChangesAsync();
        return booking.Id;
    }
}
