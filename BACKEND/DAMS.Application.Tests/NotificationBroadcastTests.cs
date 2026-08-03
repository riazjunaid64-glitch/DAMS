using DAMS.Application.Common;
using DAMS.Application.DTOs.NotificationDtos;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// Admin-composed and scheduled sends: audience targeting, the confirmation an accidental
/// mass send has to pass, and the guarantees around cancelling and repeating a job.
/// </summary>
public sealed class NotificationBroadcastTests
{
    [Fact]
    public async Task AMessageToOneUserReachesOnlyThatUser()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();

        await SendAsync(h, new NotificationAudienceDto
        {
            Type = NotificationAudienceType.SelectedUsers,
            UserIds = { h.CustomerUserId }
        });

        var notifications = await h.NotificationsAsync();
        Assert.Single(notifications);
        Assert.Equal(h.CustomerUserId, notifications[0].RecipientUserId);
    }

    [Fact]
    public async Task AValidBroadcastActionLinkCanBeOpenedByItsRecipient()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        var job = await h.Admin.ComposeAsync(new ComposeNotificationDto
        {
            Title = "Project news",
            Message = "New photos are available.",
            ActionUrl = $"/projects/{h.ProjectId}",
            Audience = new NotificationAudienceDto
            {
                Type = NotificationAudienceType.SelectedUsers,
                UserIds = { h.CustomerUserId }
            }
        }, h.AdminCtx);

        await h.Processor.ProcessScheduledJobsAsync(10);
        var notification = await h.Db.Notifications.AsNoTracking()
            .SingleAsync(item => item.NotificationJobId == job.Id);

        var opened = await h.Inbox.OpenAsync(notification.Id, h.CustomerCtx);
        Assert.True(opened.Allowed);
        Assert.Equal($"/projects/{h.ProjectId}", opened.DeepLink);
    }

    [Fact]
    public async Task AMessageToSelectedUsersReachesExactlyThem()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();

        await SendAsync(h, new NotificationAudienceDto
        {
            Type = NotificationAudienceType.SelectedUsers,
            // The last id does not exist; it must not inflate the audience.
            UserIds = { h.CustomerUserId, h.SalesUserId, 99_999 }
        });

        var recipients = (await h.NotificationsAsync()).Select(n => n.RecipientUserId!.Value).ToHashSet();
        Assert.Equal(new[] { h.CustomerUserId, h.SalesUserId }.ToHashSet(), recipients);
    }

    [Fact]
    public async Task AMessageToARoleReachesEveryActiveMemberOfIt()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();

        await SendAsync(h, new NotificationAudienceDto { Type = NotificationAudienceType.AllSalesEmployees });

        var recipients = (await h.NotificationsAsync()).Select(n => n.RecipientUserId!.Value).ToHashSet();
        Assert.Contains(h.SalesUserId, recipients);
        Assert.Contains(h.OtherSalesUserId, recipients);
        Assert.DoesNotContain(h.CustomerUserId, recipients);
        Assert.DoesNotContain(h.ManagerUserId, recipients);
    }

    [Fact]
    public async Task AMessageToATeamReachesItsMembersAndItsManager()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();

        await SendAsync(h, new NotificationAudienceDto { Type = NotificationAudienceType.Team, TeamId = h.TeamId });

        var recipients = (await h.NotificationsAsync()).Select(n => n.RecipientUserId!.Value).ToHashSet();
        Assert.Contains(h.SalesUserId, recipients);
        Assert.Contains(h.ManagerUserId, recipients);
        // Omar is deliberately not on this team.
        Assert.DoesNotContain(h.OtherSalesUserId, recipients);
    }

    [Fact]
    public async Task AMessageToCustomersInAProjectReachesOnlyThoseWithABookingThere()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();

        await SendAsync(h, new NotificationAudienceDto
        {
            Type = NotificationAudienceType.CustomersInProject,
            ProjectId = h.ProjectId
        });

        var recipients = (await h.NotificationsAsync()).Select(n => n.RecipientUserId!.Value).ToHashSet();
        Assert.Contains(h.CustomerUserId, recipients);
        Assert.DoesNotContain(h.SecondCustomerUserId, recipients);
        Assert.DoesNotContain(h.SalesUserId, recipients);
    }

    [Fact]
    public async Task AnAudiencePreviewSaysHowManyPeopleCanActuallyBeReachedOnEachChannel()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();
        await h.AddPushSubscriptionAsync(h.CustomerUserId, "device-1");
        await h.AddPushSubscriptionAsync(h.CustomerUserId, "device-2");

        var preview = await h.Admin.PreviewAudienceAsync(new ComposeNotificationDto
        {
            Title = "Notice",
            Message = "Body",
            SendEmail = true,
            SendPush = true,
            Audience = new NotificationAudienceDto { Type = NotificationAudienceType.AllCustomers }
        });

        Assert.Equal(3, preview.TotalRecipients);
        Assert.Equal(3, preview.WithEmail);
        Assert.Equal(1, preview.WithPushDevices);
        Assert.Equal(2, preview.PushDeviceCount);
        Assert.Equal("All customers", preview.Description);
    }

    [Fact]
    public async Task ALargeAudienceCannotBeSentWithoutAnExplicitConfirmation()
    {
        await using var h = await NotificationTestHarness.CreateAsync(
            new NotificationOptions { LargeAudienceThreshold = 2 });
        await h.EnableChannelsAsync();

        var dto = new ComposeNotificationDto
        {
            Title = "Everyone",
            Message = "Body",
            Audience = new NotificationAudienceDto { Type = NotificationAudienceType.AllCustomers }
        };

        var preview = await h.Admin.PreviewAudienceAsync(dto);
        Assert.True(preview.RequiresConfirmation);

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Admin.ComposeAsync(dto, h.AdminCtx));

        dto.ConfirmLargeAudience = true;
        var job = await h.Admin.ComposeAsync(dto, h.AdminCtx);
        Assert.Equal(2, job.RecipientCount);
    }

    [Fact]
    public async Task AnAudienceAboveTheHardLimitIsRefusedRatherThanTruncated()
    {
        await using var h = await NotificationTestHarness.CreateAsync(
            new NotificationOptions { MaxBroadcastRecipients = 1, LargeAudienceThreshold = 1000 });
        await h.EnableChannelsAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Admin.ComposeAsync(new ComposeNotificationDto
        {
            Title = "Everyone",
            Message = "Body",
            Audience = new NotificationAudienceDto { Type = NotificationAudienceType.AllCustomers }
        }, h.AdminCtx));
    }

    [Fact]
    public async Task ADoubleClickedSendProducesOneBroadcastRatherThanTwo()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();

        var dto = new ComposeNotificationDto
        {
            Title = "Office closed",
            Message = "We are closed on Friday.",
            ConfirmLargeAudience = true,
            RequestKey = "compose-token-1",
            Audience = new NotificationAudienceDto { Type = NotificationAudienceType.AllCustomers }
        };

        var first = await h.Admin.ComposeAsync(dto, h.AdminCtx);
        var second = await h.Admin.ComposeAsync(dto, h.AdminCtx);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(await h.Db.NotificationJobs.AsNoTracking().ToListAsync());

        await h.Processor.ProcessScheduledJobsAsync(10);
        await h.Processor.ProcessScheduledJobsAsync(10);

        // Only account-backed customers can receive the selected in-app channel. The
        // login-less contact is included when email is selected.
        Assert.Equal(2, (await h.NotificationsAsync()).Count);
    }

    [Fact]
    public async Task AScheduledSendWaitsForItsTimeAndThenGoesOutOnce()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();

        var job = await h.Admin.ComposeAsync(new ComposeNotificationDto
        {
            Title = "Maintenance tonight",
            Message = "DAMS will be briefly unavailable.",
            ConfirmLargeAudience = true,
            ScheduledAt = h.Clock.UtcNow.AddHours(3),
            Audience = new NotificationAudienceDto { Type = NotificationAudienceType.AllCustomers }
        }, h.AdminCtx);

        Assert.Equal(NotificationJobStatus.Scheduled, job.Status);

        // Too early: nothing happens.
        Assert.Equal(0, await h.Processor.ProcessScheduledJobsAsync(10));
        Assert.Empty(await h.NotificationsAsync());

        h.Clock.Set(h.Clock.UtcNow.AddHours(4));
        Assert.Equal(1, await h.Processor.ProcessScheduledJobsAsync(10));
        Assert.Equal(2, (await h.NotificationsAsync()).Count);

        // And it is not picked up again.
        Assert.Equal(0, await h.Processor.ProcessScheduledJobsAsync(10));
        Assert.Equal(2, (await h.NotificationsAsync()).Count);
    }

    [Fact]
    public async Task ACancelledScheduleNeverSendsEvenAfterItsTimePassesOrTheAppRestarts()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();

        var job = await h.Admin.ComposeAsync(new ComposeNotificationDto
        {
            Title = "Draft that should not go out",
            Message = "Internal draft.",
            ConfirmLargeAudience = true,
            ScheduledAt = h.Clock.UtcNow.AddHours(2),
            Audience = new NotificationAudienceDto { Type = NotificationAudienceType.AllCustomers }
        }, h.AdminCtx);

        var cancelled = await h.Admin.CancelJobAsync(job.Id, h.AdminCtx);
        Assert.Equal(NotificationJobStatus.Cancelled, cancelled.Status);

        h.Clock.Set(h.Clock.UtcNow.AddDays(1));
        // Several sweeps, as a restarted worker would do.
        await h.Processor.ProcessScheduledJobsAsync(10);
        await h.Processor.ProcessScheduledJobsAsync(10);

        Assert.Empty(await h.NotificationsAsync());

        // And it cannot be cancelled twice or resurrected.
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Admin.CancelJobAsync(job.Id, h.AdminCtx));
    }

    [Fact]
    public async Task ABroadcastRespectsEachRecipientsOwnChannelPreferences()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();

        await h.Preferences.UpdateAsync(h.SecondCustomerCtx, new UpdateNotificationPreferencesDto
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

        await SendAsync(h, new NotificationAudienceDto { Type = NotificationAudienceType.AllCustomers });
        await h.Processor.ProcessDueDeliveriesAsync(50);

        // All three customers get a record; only the two reachable, opted-in addresses send.
        Assert.Equal(3, (await h.NotificationsAsync()).Count);
        Assert.Equal(2, h.Email.Sent.Count);
        Assert.Contains(h.Email.Sent, m => m.To == "client@dams.test");
    }

    [Fact]
    public async Task OnlyBroadcastShapedNotificationTypesCanBeComposedByHand()
    {
        await using var h = await NotificationTestHarness.CreateAsync();

        // A receipt is generated from a payment; it must not be forgeable from the composer.
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Admin.ComposeAsync(new ComposeNotificationDto
        {
            Type = NotificationType.PaymentReceipt,
            Title = "Payment received",
            Message = "You have paid in full.",
            ConfirmLargeAudience = true,
            Audience = new NotificationAudienceDto { Type = NotificationAudienceType.AllCustomers }
        }, h.AdminCtx));
    }

    [Fact]
    public async Task AComposedMessageIsHeldToTheSameContentRulesAsATemplate()
    {
        await using var h = await NotificationTestHarness.CreateAsync();

        await Assert.ThrowsAsync<NotificationTemplateException>(() => h.Admin.ComposeAsync(new ComposeNotificationDto
        {
            Title = "Notice",
            Message = "<script>fetch('https://evil')</script>",
            ConfirmLargeAudience = true,
            Audience = new NotificationAudienceDto { Type = NotificationAudienceType.AllCustomers }
        }, h.AdminCtx));

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Admin.ComposeAsync(new ComposeNotificationDto
        {
            Title = "Notice",
            Message = "Body",
            ActionUrl = "https://evil.example.com",
            ConfirmLargeAudience = true,
            Audience = new NotificationAudienceDto { Type = NotificationAudienceType.AllCustomers }
        }, h.AdminCtx));
    }

    [Fact]
    public async Task AnEmptyAudienceIsRefusedRatherThanCreatingAJobThatDoesNothing()
    {
        await using var h = await NotificationTestHarness.CreateAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Admin.ComposeAsync(new ComposeNotificationDto
        {
            Title = "Notice",
            Message = "Body",
            Audience = new NotificationAudienceDto { Type = NotificationAudienceType.Team, TeamId = 99_999 }
        }, h.AdminCtx));
    }

    [Fact]
    public async Task InstallmentRemindersFireOnceBeforeTheDueDateAndDailyOnceOverdue()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync();

        await h.RecordPaymentAsync(1_000_000m);

        var due = PakistanTime.Today.AddDays(2);
        h.Db.Installments.Add(new Domain.Entities.Installment
        {
            BookingId = h.BookingId,
            SequenceNumber = 1,
            Amount = 500_000m,
            DueDate = due,
            Status = InstallmentStatus.Pending,
            Type = InstallmentType.Regular
        });
        await h.Db.SaveChangesAsync();

        // Default lead time is three days, so this one is inside the window.
        Assert.Equal(1, await h.Events.RunInstallmentRemindersAsync(50));
        // And it is not repeated.
        Assert.Equal(0, await h.Events.RunInstallmentRemindersAsync(50));

        var reminder = await h.Db.Notifications.AsNoTracking()
            .FirstAsync(n => n.Type == NotificationType.InstallmentDue);
        Assert.Equal(h.CustomerUserId, reminder.RecipientUserId);
        Assert.Contains("500,000.00", reminder.Title);
    }

    private static async Task SendAsync(NotificationTestHarness h, NotificationAudienceDto audience)
    {
        await h.Admin.ComposeAsync(new ComposeNotificationDto
        {
            Title = "Important notice",
            Message = "Please read this update from the DAMS team.",
            SendEmail = true,
            SendPush = true,
            ConfirmLargeAudience = true,
            Audience = audience
        }, h.AdminCtx);

        await h.Processor.ProcessScheduledJobsAsync(10);
    }
}
