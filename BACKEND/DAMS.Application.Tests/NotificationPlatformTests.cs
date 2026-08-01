using DAMS.Application.Common;
using DAMS.Application.DTOs.NotificationDtos;
using DAMS.Application.Interfaces;
using DAMS.Application.Services.Notifications;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// Channel selection, preferences, templates, push subscriptions and the delivery worker's
/// behaviour under restart and contention.
/// </summary>
public sealed class NotificationPlatformTests
{
    // ── Channels, rules and preferences ─────────────────────────────────────────

    [Fact]
    public async Task AChannelThatIsNotConfiguredIsSimplyNotAttempted()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        // Nothing enabled: a notification is still recorded, but only in the inbox.
        await h.Dispatcher.DispatchAsync(Announcement(h.CustomerUserId, "no-channels"));

        var deliveries = await h.DeliveriesAsync();
        Assert.Single(deliveries);
        Assert.Equal(NotificationChannel.InApp, deliveries[0].Channel);

        await h.Processor.ProcessDueDeliveriesAsync(20);
        Assert.Empty(h.Email.Sent);
        Assert.Empty(h.PushSender.Sent);
    }

    [Fact]
    public async Task AUsersPreferenceIsEnforcedByTheBackendAndRecordedAsSkipped()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();
        await h.AddPushSubscriptionAsync(h.CustomerUserId, "device-1");

        await h.Preferences.UpdateAsync(h.CustomerCtx, new UpdateNotificationPreferencesDto
        {
            Items =
            {
                new UpdateNotificationPreferenceItemDto
                {
                    Category = NotificationCategory.Announcements,
                    EmailEnabled = false,
                    PushEnabled = false
                }
            }
        });

        await h.Dispatcher.DispatchAsync(Announcement(h.CustomerUserId, "muted"));
        await h.Processor.ProcessDueDeliveriesAsync(20);

        Assert.Empty(h.Email.Sent);
        Assert.Empty(h.PushSender.Sent);

        // Skipped, not silently missing: an admin can see exactly why nothing was sent.
        var deliveries = await h.DeliveriesAsync();
        Assert.Equal(NotificationDeliveryStatus.Skipped,
            deliveries.Single(d => d.Channel == NotificationChannel.Email).Status);
        Assert.Contains("turned off email", deliveries.Single(d => d.Channel == NotificationChannel.Email).FailureReason);
    }

    [Fact]
    public async Task AnEssentialNotificationIgnoresAPreferenceThatTriesToMuteIt()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();

        // The interface never offers these, and a crafted request is ignored rather than
        // silently switching off a receipt.
        var result = await h.Preferences.UpdateAsync(h.CustomerCtx, new UpdateNotificationPreferencesDto
        {
            Items =
            {
                new UpdateNotificationPreferenceItemDto
                {
                    Category = NotificationCategory.PaymentsAndReceipts,
                    EmailEnabled = false,
                    PushEnabled = false
                }
            }
        });

        Assert.True(result.Single(r => r.Category == NotificationCategory.PaymentsAndReceipts).EmailEnabled);
        Assert.Empty(await h.Db.NotificationPreferences.AsNoTracking().ToListAsync());

        await h.RecordPaymentAsync();
        await h.Processor.ProcessDueDeliveriesAsync(20);

        Assert.Single(h.Email.Sent);
    }

    [Fact]
    public async Task AnEssentialNotificationCannotBeSwitchedOffByARule()
    {
        await using var h = await NotificationTestHarness.CreateAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Configuration.SaveRuleAsync(NotificationType.PaymentReceipt, new SaveNotificationRuleDto
            {
                IsEnabled = false,
                InAppEnabled = true,
                EmailEnabled = true,
                PushEnabled = true
            }, h.AdminCtx));

        // Weakening it needs an explicit acknowledgement.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Configuration.SaveRuleAsync(NotificationType.PaymentReceipt, new SaveNotificationRuleDto
            {
                IsEnabled = true,
                InAppEnabled = true,
                EmailEnabled = false,
                PushEnabled = true
            }, h.AdminCtx));

        var saved = await h.Configuration.SaveRuleAsync(NotificationType.PaymentReceipt, new SaveNotificationRuleDto
        {
            IsEnabled = true,
            InAppEnabled = true,
            EmailEnabled = false,
            PushEnabled = true,
            ConfirmEssentialChange = true
        }, h.AdminCtx);

        Assert.False(saved.EmailEnabled);
        Assert.True(saved.IsEnabled);
    }

    [Fact]
    public async Task DisablingARuleStopsThatNotificationBeingRaisedAtAll()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();

        await h.Configuration.SaveRuleAsync(NotificationType.AdminAnnouncement, new SaveNotificationRuleDto
        {
            IsEnabled = false
        }, h.AdminCtx);

        var created = await h.Dispatcher.DispatchAsync(Announcement(h.CustomerUserId, "disabled-type"));

        Assert.False(created);
        Assert.Empty(await h.NotificationsAsync());
    }

    // ── Templates ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnEditedTemplateIsWhatActuallyGoesOut()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();

        await h.Configuration.SaveTemplateAsync(NotificationType.PaymentReceipt, NotificationChannel.Email,
            new SaveNotificationTemplateDto
            {
                Subject = "Receipt {{receiptNumber}} from {{companyName}}",
                Heading = "Thank you, {{customerName}}",
                Body = "<p>We received <strong>{{amount}}</strong> on {{paymentDate}}.</p>",
                ActionText = "View receipt"
            }, h.AdminCtx);

        await h.RecordPaymentAsync(750_000m);
        await h.Processor.ProcessDueDeliveriesAsync(20);

        var email = Assert.Single(h.Email.Sent);
        Assert.StartsWith("Receipt RCP-", email.Subject);
        Assert.Contains("DAMS Estates", email.Subject);
        Assert.Contains("Thank you, Client Person", email.HtmlBody);
        Assert.Contains("750,000.00", email.HtmlBody);
        Assert.Contains("750,000.00", email.TextBody);

        // The receipt PDF rides along, built from the booking service's own figures.
        var attachment = Assert.Single(email.Attachments);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(attachment.Content[..4]));
    }

    [Fact]
    public async Task UnsafeTemplateContentIsRejectedOnSave()
    {
        await using var h = await NotificationTestHarness.CreateAsync();

        foreach (var body in new[]
                 {
                     "<script>fetch('https://evil')</script>",
                     "<img src=x onerror=alert(1)>",
                     "<a href=\"javascript:alert(1)\">click</a>",
                     "<iframe src=\"https://evil\"></iframe>"
                 })
        {
            await Assert.ThrowsAsync<NotificationTemplateException>(() =>
                h.Configuration.SaveTemplateAsync(NotificationType.AdminAnnouncement, NotificationChannel.Email,
                    new SaveNotificationTemplateDto { Subject = "Hello", Body = body }, h.AdminCtx));
        }
    }

    [Fact]
    public async Task ATemplateCannotUseAVariableThatIsNotApprovedForIt()
    {
        await using var h = await NotificationTestHarness.CreateAsync();

        // "cnic" is not offered for any template, and a lead variable is not offered on a
        // payment receipt.
        await Assert.ThrowsAsync<NotificationTemplateException>(() =>
            h.Configuration.SaveTemplateAsync(NotificationType.PaymentReceipt, NotificationChannel.Email,
                new SaveNotificationTemplateDto { Subject = "Receipt", Body = "Your CNIC is {{cnic}}" }, h.AdminCtx));

        await Assert.ThrowsAsync<NotificationTemplateException>(() =>
            h.Configuration.SaveTemplateAsync(NotificationType.PaymentReceipt, NotificationChannel.Email,
                new SaveNotificationTemplateDto { Subject = "Receipt", Body = "About {{leadName}}" }, h.AdminCtx));
    }

    [Fact]
    public async Task AVariableWithNoValueLeavesTheMessageReadableRatherThanBroken()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();

        await h.Configuration.SaveTemplateAsync(NotificationType.AdminAnnouncement, NotificationChannel.Email,
            new SaveNotificationTemplateDto
            {
                Subject = "Notice for {{recipientName}}",
                // supportPhone is approved but not configured on this installation.
                Body = "<p>{{message}} Call us on {{supportPhone}} .</p>"
            }, h.AdminCtx);

        await h.Dispatcher.DispatchAsync(Announcement(h.CustomerUserId, "missing-vars"));
        await h.Processor.ProcessDueDeliveriesAsync(20);

        var email = Assert.Single(h.Email.Sent);
        Assert.DoesNotContain("{{", email.HtmlBody);
        Assert.DoesNotContain("{{", email.Subject);
        Assert.DoesNotContain("supportPhone", email.HtmlBody);
    }

    [Fact]
    public async Task AValueInsideANotificationCannotInjectMarkupIntoAnEmail()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();

        // A customer-supplied name that looks like markup.
        var customer = await h.Db.Customers.FirstAsync(c => c.Id == h.CustomerId);
        customer.FullName = "<script>alert('x')</script>";
        await h.Db.SaveChangesAsync();

        await h.RecordPaymentAsync();
        await h.Processor.ProcessDueDeliveriesAsync(20);

        var email = Assert.Single(h.Email.Sent);
        Assert.DoesNotContain("<script>", email.HtmlBody);
        Assert.Contains("&lt;script&gt;", email.HtmlBody);
    }

    [Fact]
    public async Task PushCopyStaysShortAndKeepsPrivateDetailOffTheLockScreen()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();
        await h.AddPushSubscriptionAsync(h.CustomerUserId, "device-1");

        await h.RecordPaymentAsync(234_567m);
        await h.Processor.ProcessDueDeliveriesAsync(20);

        var (_, payload) = Assert.Single(h.PushSender.Sent);

        Assert.Contains("Payment received", payload);
        Assert.Contains("Open DAMS to view your receipt", payload);
        // Neither the amount nor the customer's name belongs on a banner.
        Assert.DoesNotContain("234,567", payload);
        Assert.DoesNotContain("Client Person", payload);
    }

    // ── Push subscriptions ──────────────────────────────────────────────────────

    [Fact]
    public async Task OneUserCanHaveManyDevicesAndAllOfThemGetTheNotification()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();
        var (p256dh, auth) = SubscriptionKeys();

        await h.Push.RegisterAsync(h.CustomerCtx, new RegisterPushSubscriptionDto
        {
            Endpoint = "https://push.test/laptop", P256dh = p256dh, Auth = auth, DeviceLabel = "Chrome on Windows"
        });
        await h.Push.RegisterAsync(h.CustomerCtx, new RegisterPushSubscriptionDto
        {
            Endpoint = "https://push.test/phone", P256dh = p256dh, Auth = auth, DeviceLabel = "Chrome on Android"
        });

        await h.Dispatcher.DispatchAsync(Announcement(h.CustomerUserId, "two-devices"));
        await h.Processor.ProcessDueDeliveriesAsync(20);

        Assert.Equal(2, h.PushSender.Sent.Count);
        Assert.Equal(2, (await h.Push.GetMyDevicesAsync(h.CustomerCtx)).Count);
    }

    [Fact]
    public async Task ASharedBrowserMovesToWhoeverSignsInNextRatherThanKeepingTheOldOwner()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();
        var (p256dh, auth) = SubscriptionKeys();

        var subscription = new RegisterPushSubscriptionDto
        {
            Endpoint = "https://push.test/shared-desk", P256dh = p256dh, Auth = auth, DeviceLabel = "Front desk PC"
        };

        await h.Push.RegisterAsync(h.CustomerCtx, subscription);
        // The next person signs in on the same machine and their browser re-registers.
        await h.Push.RegisterAsync(h.SecondCustomerCtx, subscription);

        var rows = await h.Db.PushSubscriptions.AsNoTracking().ToListAsync();
        Assert.Single(rows);
        Assert.Equal(h.SecondCustomerUserId, rows[0].UserId);

        await h.Dispatcher.DispatchAsync(Announcement(h.CustomerUserId, "for-previous-user"));
        await h.Processor.ProcessDueDeliveriesAsync(20);

        // Nothing goes to the shared machine for the previous user.
        Assert.Empty(h.PushSender.Sent);
    }

    [Fact]
    public async Task SigningOutDetachesEveryDeviceForThatLogin()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();
        await h.AddPushSubscriptionAsync(h.CustomerUserId, "d1");
        await h.AddPushSubscriptionAsync(h.CustomerUserId, "d2");
        await h.AddPushSubscriptionAsync(h.SecondCustomerUserId, "d3");

        var removed = await h.Push.UnregisterAllAsync(h.CustomerUserId);

        Assert.Equal(2, removed);
        Assert.Single(await h.Db.PushSubscriptions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ASubscriptionTheBrowserHasThrownAwayIsDeactivatedRatherThanRetriedForever()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();

        var subscription = await h.AddPushSubscriptionAsync(h.CustomerUserId, "expired");
        h.PushSender.GoneEndpoints.Add(subscription.Endpoint);

        await h.Dispatcher.DispatchAsync(Announcement(h.CustomerUserId, "gone-subscription"));
        await h.Processor.ProcessDueDeliveriesAsync(20);

        var stored = await h.Db.PushSubscriptions.AsNoTracking().FirstAsync();
        Assert.False(stored.IsActive);
        Assert.Contains("discarded", stored.DeactivationReason);

        var delivery = Assert.Single(await h.DeliveriesForAsync(NotificationChannel.WebPush));
        Assert.Equal(NotificationDeliveryStatus.Unavailable, delivery.Status);
        Assert.True(delivery.IsPermanentFailure);
    }

    [Fact]
    public async Task ASubscriptionCanOnlyBeRemovedByTheAccountItBelongsTo()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        var subscription = await h.AddPushSubscriptionAsync(h.CustomerUserId, "private-device");

        // Knowing the endpoint is not enough.
        await h.Push.UnregisterAsync(h.SecondCustomerCtx, subscription.Endpoint);
        Assert.Single(await h.Db.PushSubscriptions.AsNoTracking().ToListAsync());

        await h.Push.UnregisterAsync(h.CustomerCtx, subscription.Endpoint);
        Assert.Empty(await h.Db.PushSubscriptions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task AnInvalidSubscriptionIsRefusedOutright()
    {
        await using var h = await NotificationTestHarness.CreateAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Push.RegisterAsync(h.CustomerCtx, new RegisterPushSubscriptionDto
            {
                Endpoint = "http://push.test/insecure", P256dh = "k", Auth = "a"
            }));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Push.RegisterAsync(h.CustomerCtx, new RegisterPushSubscriptionDto
            {
                Endpoint = "https://push.test/ok", P256dh = "not a key!", Auth = "a"
            }));
    }

    [Fact]
    public async Task PushConfigNeverExposesThePrivateKey()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();

        var config = await h.Push.GetConfigAsync(h.CustomerCtx);
        var privateKey = await h.Settings.GetAsync(NotificationSettingKeys.PushVapidPrivateKey);

        Assert.True(config.Enabled);
        Assert.False(string.IsNullOrWhiteSpace(config.PublicKey));
        Assert.NotEqual(privateKey, config.PublicKey);
    }

    // ── Worker behaviour ────────────────────────────────────────────────────────

    [Fact]
    public async Task ARestartMidDeliveryResumesOnceTheLeaseExpiresAndDoesNotDoubleSend()
    {
        await using var h = await NotificationTestHarness.CreateAsync(new NotificationOptions { LeaseMinutes = 5 });
        await h.EnableChannelsAsync();

        await h.Dispatcher.DispatchAsync(Announcement(h.CustomerUserId, "restart"));

        // Simulate a worker that claimed the row and then died before finishing.
        var email = await h.Db.NotificationDeliveries.FirstAsync(d => d.Channel == NotificationChannel.Email);
        email.Status = NotificationDeliveryStatus.Processing;
        email.LockedBy = "dead-worker";
        email.LockedUntil = h.Clock.UtcNow.AddMinutes(5);
        await h.Db.SaveChangesAsync();

        // Nothing else may touch it while the lease is live.
        await h.Processor.ProcessDueDeliveriesAsync(20);
        Assert.Empty(h.Email.Sent);

        // Once the lease expires the row is claimable again with no manual intervention —
        // that is what makes restart recovery automatic rather than a stranded row.
        h.Clock.Set(h.Clock.UtcNow.AddMinutes(10));
        await h.Processor.ProcessDueDeliveriesAsync(20);

        Assert.Single(h.Email.Sent);

        var recovered = await h.Db.NotificationDeliveries.AsNoTracking()
            .FirstAsync(d => d.Channel == NotificationChannel.Email);
        Assert.Equal(NotificationDeliveryStatus.Sent, recovered.Status);
        Assert.Null(recovered.LockedUntil);
    }

    [Fact]
    public async Task AScheduledJobAbandonedMidSendResumesWithoutSendingTwice()
    {
        await using var h = await NotificationTestHarness.CreateAsync(new NotificationOptions { LeaseMinutes = 5 });
        await h.EnableChannelsAsync();

        var job = await h.Admin.ComposeAsync(new ComposeNotificationDto
        {
            Title = "All customers",
            Message = "Body",
            ConfirmLargeAudience = true,
            Audience = new NotificationAudienceDto { Type = NotificationAudienceType.AllCustomers }
        }, h.AdminCtx);

        // The worker claimed it, created one recipient, then the process died.
        var stored = await h.Db.NotificationJobs.FirstAsync(j => j.Id == job.Id);
        stored.Status = NotificationJobStatus.Processing;
        stored.LockedBy = "dead-worker";
        stored.LockedUntil = h.Clock.UtcNow.AddMinutes(5);
        await h.Db.SaveChangesAsync();

        await h.Dispatcher.DispatchAsync(new NotificationRequest
        {
            Type = NotificationType.AdminAnnouncement,
            RecipientUserId = h.CustomerUserId,
            DedupKey = $"job:{job.Id}:u:{h.CustomerUserId}",
            Title = "All customers",
            Message = "Body",
            JobId = job.Id
        });

        // Still leased: nothing happens.
        Assert.Equal(0, await h.Processor.ProcessScheduledJobsAsync(10));

        h.Clock.Set(h.Clock.UtcNow.AddMinutes(10));
        Assert.Equal(1, await h.Processor.ProcessScheduledJobsAsync(10));

        // Two account-backed customers, one notification each — the one already created is
        // not repeated. Login-less contacts require the email channel.
        Assert.Equal(2, (await h.NotificationsAsync()).Count);
        Assert.Equal(NotificationJobStatus.Sent,
            (await h.Db.NotificationJobs.AsNoTracking().FirstAsync(j => j.Id == job.Id)).Status);
    }

    [Fact]
    public async Task TwoWorkersSweepingAtOnceDoNotSendTheSameMessageTwice()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();
        await h.Dispatcher.DispatchAsync(Announcement(h.CustomerUserId, "contended"));

        // A second processor over the same store, exactly as a second instance would be.
        var second = new NotificationDeliveryProcessor(
            h.Db,
            new INotificationChannelSender[]
            {
                new InAppChannelSender(h.Realtime),
                new EmailChannelSender(h.Db, h.Settings, h.Renderer, h.Email,
                    new NotificationReceiptAttachmentBuilder(h.Bookings, h.Settings,
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<NotificationReceiptAttachmentBuilder>.Instance))
            },
            h.Dispatcher, h.Recipients, h.Options, h.Clock, h.Eligibility,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<NotificationDeliveryProcessor>.Instance);

        await h.Processor.ProcessDueDeliveriesAsync(20);
        await second.ProcessDueDeliveriesAsync(20);

        Assert.Single(h.Email.Sent);
    }

    [Fact]
    public async Task AnExpiredNotificationIsNotSentLate()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();

        await h.Dispatcher.DispatchAsync(new NotificationRequest
        {
            Type = NotificationType.AdminAnnouncement,
            RecipientUserId = h.CustomerUserId,
            DedupKey = "short-lived",
            Title = "Office closes early today",
            Message = "Today only.",
            ExpiresAt = h.Clock.UtcNow.AddHours(2)
        });

        h.Clock.Set(h.Clock.UtcNow.AddHours(5));
        await h.Processor.ProcessDueDeliveriesAsync(20);

        Assert.Empty(h.Email.Sent);
        Assert.All(await h.DeliveriesAsync(), d => Assert.Equal(NotificationDeliveryStatus.Expired, d.Status));

        // And it has already dropped out of the inbox.
        var inbox = await h.Inbox.GetAsync(h.CustomerCtx, new NotificationFilterDto());
        Assert.Empty(inbox.Items);
    }

    [Fact]
    public async Task ADelayedRuleHoldsTheNotificationBackUntilItsTime()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();

        await h.Configuration.SaveRuleAsync(NotificationType.AdminAnnouncement, new SaveNotificationRuleDto
        {
            IsEnabled = true,
            InAppEnabled = true,
            EmailEnabled = true,
            DelayMinutes = 30
        }, h.AdminCtx);

        await h.Dispatcher.DispatchAsync(Announcement(h.CustomerUserId, "delayed"));

        await h.Processor.ProcessDueDeliveriesAsync(20);
        Assert.Empty(h.Email.Sent);

        h.Clock.Set(h.Clock.UtcNow.AddMinutes(31));
        await h.Processor.ProcessDueDeliveriesAsync(20);
        Assert.Single(h.Email.Sent);
    }

    [Fact]
    public async Task ReadStateAndCountsBehaveAsTheInterfaceExpects()
    {
        await using var h = await NotificationTestHarness.CreateAsync();

        for (var i = 0; i < 3; i++)
            await h.Dispatcher.DispatchAsync(Announcement(h.CustomerUserId, $"count-{i}"));

        Assert.Equal(3, await h.Inbox.GetUnreadCountAsync(h.CustomerCtx));

        var summary = await h.Inbox.GetSummaryAsync(h.CustomerCtx, 10);
        Assert.Equal(3, summary.UnreadCount);
        Assert.Equal(3, summary.UnreadByCategory[NotificationCategory.Announcements.ToString()]);

        var first = summary.Recent[0];
        await h.Inbox.MarkReadAsync(first.Id, h.CustomerCtx);
        Assert.Equal(2, await h.Inbox.GetUnreadCountAsync(h.CustomerCtx));

        // Marking an already-read notification again is a no-op, not a negative count.
        await h.Inbox.MarkReadAsync(first.Id, h.CustomerCtx);
        Assert.Equal(2, await h.Inbox.GetUnreadCountAsync(h.CustomerCtx));

        Assert.Equal(2, await h.Inbox.MarkAllReadAsync(h.CustomerCtx, category: null));
        Assert.Equal(0, await h.Inbox.GetUnreadCountAsync(h.CustomerCtx));
    }

    [Fact]
    public async Task DismissingANotificationRemovesItFromTheDefaultViewButKeepsTheHistory()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.Dispatcher.DispatchAsync(Announcement(h.CustomerUserId, "dismissable"));

        var notification = await h.Db.Notifications.AsNoTracking().FirstAsync();
        await h.Inbox.ArchiveAsync(notification.Id, h.CustomerCtx);

        Assert.Empty((await h.Inbox.GetAsync(h.CustomerCtx, new NotificationFilterDto())).Items);
        Assert.Single((await h.Inbox.GetAsync(h.CustomerCtx, new NotificationFilterDto { IncludeArchived = true })).Items);
    }

    private static NotificationRequest Announcement(int userId, string key) => new()
    {
        Type = NotificationType.AdminAnnouncement,
        RecipientUserId = userId,
        DedupKey = key,
        Title = "Scheduled maintenance",
        Message = "DAMS will be unavailable on Sunday morning."
    };

    private static (string P256dh, string Auth) SubscriptionKeys()
    {
        var (publicKey, _) = WebPushClient.GenerateVapidKeys();
        return (publicKey, Base64Url.Encode(new byte[16]));
    }
}
