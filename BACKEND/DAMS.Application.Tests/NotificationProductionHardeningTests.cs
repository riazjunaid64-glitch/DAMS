using DAMS.Application.Common;
using DAMS.Application.DTOs.NotificationDtos;
using DAMS.Application.Services.Notifications;
using DAMS.Domain.Enums;
using DAMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

public sealed class NotificationProductionHardeningTests
{
    [Fact]
    public async Task PushRegistrationRejectsAHostOutsideTheServerAllowList()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        var (p256dh, auth) = SubscriptionKeys();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Push.RegisterAsync(h.CustomerCtx, new RegisterPushSubscriptionDto
            {
                Endpoint = "https://127.0.0.1/internal",
                P256dh = p256dh,
                Auth = auth
            }));
    }

    [Fact]
    public async Task AStoredLegacyPrivateEndpointIsDeactivatedWithoutARequest()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();
        var (p256dh, auth) = SubscriptionKeys();
        h.Db.PushSubscriptions.Add(new PushSubscription
        {
            UserId = h.CustomerUserId,
            Endpoint = "https://127.0.0.1/internal",
            P256dh = p256dh,
            Auth = auth,
            IsActive = true
        });
        await h.Db.SaveChangesAsync();

        await h.Dispatcher.DispatchAsync(new NotificationRequest
        {
            Type = NotificationType.AdminAnnouncement,
            RecipientUserId = h.CustomerUserId,
            DedupKey = "legacy-private-endpoint",
            Title = "Notice",
            Message = "Body",
            ChannelMask = NotificationChannel.WebPush
        });
        await h.Processor.ProcessDueDeliveriesAsync(10);

        Assert.Empty(h.PushSender.Sent);
        Assert.False((await h.Db.PushSubscriptions.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task PushDoesNotAdvertiseItselfAsReadyWithoutAValidVapidContact()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        var (publicKey, privateKey) = WebPushClient.GenerateVapidKeys();
        await h.Settings.SetAsync(NotificationSettingKeys.PushEnabled, "true", h.AdminUserId);
        await h.Settings.SetAsync(NotificationSettingKeys.PushVapidPublicKey, publicKey, h.AdminUserId);
        await h.Settings.SetAsync(NotificationSettingKeys.PushVapidPrivateKey, privateKey, h.AdminUserId);
        await h.Settings.SetAsync(NotificationSettingKeys.PushVapidSubject, "mailto:not-an-address", h.AdminUserId);
        await h.Db.SaveChangesAsync();

        Assert.False((await h.Push.GetConfigAsync(h.CustomerCtx)).Enabled);
        Assert.Null(await h.Push.GetCredentialsAsync());
    }

    [Fact]
    public async Task PushRegistrationEnforcesThePerAccountDeviceLimit()
    {
        await using var h = await NotificationTestHarness.CreateAsync(
            new NotificationOptions { MaxPushSubscriptionsPerUser = 1 });
        var (p256dh, auth) = SubscriptionKeys();

        await h.Push.RegisterAsync(h.CustomerCtx, new RegisterPushSubscriptionDto
        {
            Endpoint = "https://push.test/one", P256dh = p256dh, Auth = auth
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Push.RegisterAsync(h.CustomerCtx, new RegisterPushSubscriptionDto
            {
                Endpoint = "https://push.test/two", P256dh = p256dh, Auth = auth
            }));
    }

    [Fact]
    public async Task ReactivatingAnOldPushEndpointCannotBypassTheDeviceLimit()
    {
        await using var h = await NotificationTestHarness.CreateAsync(
            new NotificationOptions { MaxPushSubscriptionsPerUser = 1 });
        var (p256dh, auth) = SubscriptionKeys();

        await h.Push.RegisterAsync(h.CustomerCtx, new RegisterPushSubscriptionDto
        {
            Endpoint = "https://push.test/old", P256dh = p256dh, Auth = auth
        });
        var old = await h.Db.PushSubscriptions.SingleAsync();
        old.IsActive = false;
        old.DeactivatedAt = DateTime.UtcNow;
        await h.Db.SaveChangesAsync();

        await h.Push.RegisterAsync(h.CustomerCtx, new RegisterPushSubscriptionDto
        {
            Endpoint = "https://push.test/current", P256dh = p256dh, Auth = auth
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Push.RegisterAsync(h.CustomerCtx, new RegisterPushSubscriptionDto
            {
                Endpoint = "https://push.test/old", P256dh = p256dh, Auth = auth
            }));
    }

    [Fact]
    public async Task InboxRetentionArchivesButKeepsTheDedupAndDeliveryAudit()
    {
        await using var h = await NotificationTestHarness.CreateAsync(
            new NotificationOptions { InboxRetentionDays = 1 });

        await h.Dispatcher.DispatchAsync(new NotificationRequest
        {
            Type = NotificationType.AdminAnnouncement,
            RecipientUserId = h.CustomerUserId,
            DedupKey = "retention-audit-test",
            Title = "Old notice",
            Message = "Body"
        });

        var notification = await h.Db.Notifications.Include(n => n.Deliveries).SingleAsync();
        notification.IsRead = true;
        notification.CreatedAt = h.Clock.UtcNow.AddDays(-10);
        await h.Db.SaveChangesAsync();

        Assert.Equal(1, await h.Processor.PruneAsync());

        var retained = await h.Db.Notifications.AsNoTracking().SingleAsync();
        Assert.True(retained.IsArchived);
        Assert.Equal("retention-audit-test", retained.DedupKey);
        Assert.Single(await h.Db.NotificationDeliveries.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task PreviewUsesTheUnsavedDraftAndPushRejectsSensitiveVariables()
    {
        await using var h = await NotificationTestHarness.CreateAsync();

        var preview = await h.Configuration.PreviewTemplateAsync(
            NotificationType.AdminAnnouncement,
            new SaveNotificationTemplateDto
            {
                Channel = NotificationChannel.Email,
                Subject = "DRAFT {{title}}",
                Body = "DRAFT BODY {{message}}",
                IsEnabled = true
            });

        Assert.StartsWith("DRAFT", preview.Subject);
        Assert.Contains("DRAFT BODY", preview.Html);

        await Assert.ThrowsAsync<NotificationTemplateException>(() =>
            h.Configuration.SaveTemplateAsync(
                NotificationType.PaymentReceipt,
                NotificationChannel.WebPush,
                new SaveNotificationTemplateDto
                {
                    Subject = "Payment received",
                    Body = "Customer paid {{amount}}",
                    IsEnabled = true
                },
                h.AdminCtx));
    }

    [Fact]
    public async Task LegacyUnsafePushTemplateFallsBackToPrivateWordingAtSendTime()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        h.Db.NotificationTemplates.Add(new NotificationTemplate
        {
            Type = NotificationType.PaymentReceipt,
            Channel = NotificationChannel.WebPush,
            Category = NotificationCategory.PaymentsAndReceipts,
            Name = "Legacy payment push",
            Subject = "Payment {{amount}}",
            Body = "{{customerName}} paid {{amount}}",
            IsEnabled = true
        });
        await h.Db.SaveChangesAsync();

        var push = await h.Renderer.RenderPushAsync(new Notification
        {
            Type = NotificationType.PaymentReceipt,
            Title = "Payment from Private Customer",
            Message = "PKR 999,999.00",
            DataJson = """{"customerName":"Private Customer","amount":"PKR 999,999.00"}"""
        });

        Assert.Equal("Payment received", push.Title);
        Assert.Equal("Open DAMS to view your receipt.", push.Body);
        Assert.DoesNotContain("999", push.Title + push.Body);
        Assert.DoesNotContain("Private Customer", push.Title + push.Body);
    }

    [Fact]
    public async Task LoginlessCustomersAreOnlyInAnAudienceWhenEmailIsSelected()
    {
        await using var h = await NotificationTestHarness.CreateAsync();

        var inAppOnly = await h.Admin.PreviewAudienceAsync(new ComposeNotificationDto
        {
            Title = "Notice",
            Message = "Body",
            Audience = new NotificationAudienceDto { Type = NotificationAudienceType.AllCustomers }
        });
        var withEmail = await h.Admin.PreviewAudienceAsync(new ComposeNotificationDto
        {
            Title = "Notice",
            Message = "Body",
            SendEmail = true,
            Audience = new NotificationAudienceDto { Type = NotificationAudienceType.AllCustomers }
        });

        Assert.Equal(2, inAppOnly.TotalRecipients);
        Assert.Equal(3, withEmail.TotalRecipients);
    }

    [Fact]
    public async Task AHardBounceSuppressesFutureEssentialMessagesToo()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();
        h.Email.FailAsHardBounce = true;

        await h.RecordPaymentAsync(100m);
        await h.Processor.ProcessDueDeliveriesAsync(20);

        h.Email.FailAsHardBounce = false;
        await h.RecordPaymentAsync(200m);
        await h.Processor.ProcessDueDeliveriesAsync(20);

        Assert.Empty(h.Email.Sent);
        Assert.Contains(await h.DeliveriesForAsync(NotificationChannel.Email),
            d => d.Status == NotificationDeliveryStatus.Skipped);
    }

    [Fact]
    public async Task InstallmentReminderBatchesAdvancePastAlreadyNotifiedRows()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        var due = PakistanTime.Today.AddDays(1);

        for (var sequence = 1; sequence <= 51; sequence++)
        {
            h.Db.Installments.Add(new Installment
            {
                BookingId = h.BookingId,
                SequenceNumber = sequence,
                Amount = sequence,
                DueDate = due,
                Status = InstallmentStatus.Pending,
                Type = InstallmentType.Regular
            });
        }
        await h.Db.SaveChangesAsync();

        Assert.Equal(50, await h.Events.RunInstallmentRemindersAsync(50));
        Assert.Equal(1, await h.Events.RunInstallmentRemindersAsync(50));
        Assert.Equal(51, await h.Db.Notifications.CountAsync(
            n => n.Type == NotificationType.InstallmentDue));
    }

    [Fact]
    public async Task ReconciliationRecoversAnApprovedBookingNotificationAfterACommitGap()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        var request = new BookingRequest
        {
            UnitId = h.UnitId,
            UserId = h.CustomerUserId,
            FullName = "Client Customer",
            Phone = "03001234567",
            Email = "client@dams.test",
            CNIC = "42101-1234567-1",
            Address = "Karachi",
            Status = BookingRequestStatus.Approved,
            CreatedAt = DateTime.UtcNow
        };
        h.Db.BookingRequests.Add(request);
        await h.Db.SaveChangesAsync();
        var booking = await h.Db.Bookings.SingleAsync(b => b.Id == h.BookingId);
        booking.BookingRequestId = request.Id;
        await h.Db.SaveChangesAsync();

        await h.Events.ReconcileBusinessEventsAsync(50);

        Assert.Contains(await h.NotificationsAsync(),
            n => n.Type == NotificationType.BookingApproved
                 && n.EntityId == h.BookingId);
    }

    private static (string P256dh, string Auth) SubscriptionKeys()
    {
        var (publicKey, _) = WebPushClient.GenerateVapidKeys();
        return (publicKey, Base64Url.Encode(new byte[16]));
    }
}
