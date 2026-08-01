using DAMS.Application.Common;
using DAMS.Application.DTOs.NotificationDtos;
using DAMS.Application.Services.Notifications;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// Recipient isolation, role boundaries and the rules that decide where a notification is
/// allowed to take somebody.
/// </summary>
public sealed class NotificationSecurityTests
{
    [Fact]
    public async Task AnInboxOnlyEverContainsItsOwnOwnersNotifications()
    {
        await using var h = await NotificationTestHarness.CreateAsync();

        await h.Dispatcher.DispatchAsync(Announce(h.CustomerUserId, "customer-1", "For the customer"));
        await h.Dispatcher.DispatchAsync(Announce(h.SalesUserId, "sales-1", "For the sales employee"));
        await h.Dispatcher.DispatchAsync(Announce(h.AdminUserId, "admin-1", "For the admin"));

        var customerInbox = await h.Inbox.GetAsync(h.CustomerCtx, new NotificationFilterDto());
        var salesInbox = await h.Inbox.GetAsync(h.SalesCtx, new NotificationFilterDto());

        Assert.Single(customerInbox.Items);
        Assert.Equal("For the customer", customerInbox.Items[0].Title);
        Assert.Single(salesInbox.Items);
        Assert.Equal("For the sales employee", salesInbox.Items[0].Title);
    }

    [Fact]
    public async Task OneUserCannotReadOrChangeAnotherUsersNotificationByGuessingItsId()
    {
        await using var h = await NotificationTestHarness.CreateAsync();

        await h.Dispatcher.DispatchAsync(Announce(h.AdminUserId, "admin-secret", "Internal escalation"));
        var target = await h.Db.Notifications.AsNoTracking().FirstAsync();

        // Reported as missing, not forbidden: a different answer would confirm the row exists.
        await Assert.ThrowsAsync<LeadNotFoundException>(() => h.Inbox.MarkReadAsync(target.Id, h.CustomerCtx));
        await Assert.ThrowsAsync<LeadNotFoundException>(() => h.Inbox.OpenAsync(target.Id, h.CustomerCtx));
        await Assert.ThrowsAsync<LeadNotFoundException>(() => h.Inbox.ArchiveAsync(target.Id, h.CustomerCtx));

        var stillUnread = await h.Db.Notifications.AsNoTracking().FirstAsync(n => n.Id == target.Id);
        Assert.False(stillUnread.IsRead);
    }

    [Fact]
    public async Task MarkingEverythingReadOnlyTouchesTheCallersOwnRows()
    {
        await using var h = await NotificationTestHarness.CreateAsync();

        await h.Dispatcher.DispatchAsync(Announce(h.CustomerUserId, "c1", "Customer message"));
        await h.Dispatcher.DispatchAsync(Announce(h.SalesUserId, "s1", "Sales message"));

        await h.Inbox.MarkAllReadAsync(h.CustomerCtx, category: null);

        Assert.Equal(0, await h.Inbox.GetUnreadCountAsync(h.CustomerCtx));
        Assert.Equal(1, await h.Inbox.GetUnreadCountAsync(h.SalesCtx));
    }

    [Fact]
    public async Task ACustomerCannotFollowANotificationIntoAnotherCustomersBooking()
    {
        await using var h = await NotificationTestHarness.CreateAsync();

        // A booking notification that names the first customer's booking, delivered — through
        // a mistake or a tampered row — to somebody else.
        var created = await h.Dispatcher.DispatchAsync(new NotificationRequest
        {
            Type = NotificationType.BookingApproved, RecipientUserId = h.SecondCustomerUserId,
            DedupKey = "rejected-cross-tenant-booking", Title = "Booking approved", Message = "Details",
            EntityType = NotificationEntityType.Booking, EntityId = h.BookingId
        });
        Assert.False(created);

        await SeedLegacyAsync(h, NotificationType.BookingApproved, h.SecondCustomerUserId,
            NotificationEntityType.Booking, h.BookingId, $"/my-projects/{h.BookingId}");

        var notification = await h.Db.Notifications.AsNoTracking().FirstAsync();
        var result = await h.Inbox.OpenAsync(notification.Id, h.SecondCustomerCtx);

        Assert.False(result.Allowed);
        Assert.Null(result.DeepLink);
        Assert.Contains("permission", result.Message);
    }

    [Fact]
    public async Task ACustomerCannotFollowANotificationIntoTheLeadWorkspace()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        var lead = await SeedLeadAsync(h);

        Assert.False(await h.Dispatcher.DispatchAsync(new NotificationRequest
        {
            Type = NotificationType.LeadAssigned, RecipientUserId = h.CustomerUserId,
            DedupKey = "lead-to-customer", Title = "Lead assigned", Message = "Internal",
            EntityType = NotificationEntityType.Lead, EntityId = lead
        }));
        await SeedLegacyAsync(h, NotificationType.LeadAssigned, h.CustomerUserId,
            NotificationEntityType.Lead, lead, $"/crm/leads/{lead}");

        var notification = await h.Db.Notifications.AsNoTracking().FirstAsync();
        await Assert.ThrowsAsync<LeadNotFoundException>(() => h.Inbox.OpenAsync(notification.Id, h.CustomerCtx));
        Assert.Empty((await h.Inbox.GetAsync(h.CustomerCtx, new NotificationFilterDto())).Items);
    }

    [Fact]
    public async Task ACustomerCannotOpenAStaffRouteFromAnAnnouncement()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        Assert.True(await h.Dispatcher.DispatchAsync(new NotificationRequest
        {
            Type = NotificationType.AdminAnnouncement,
            RecipientUserId = h.CustomerUserId,
            DedupKey = "customer-staff-route",
            Title = "Internal route",
            Message = "Details",
            EntityType = NotificationEntityType.Announcement,
            DeepLink = "/crm"
        }));

        var notification = await h.Db.Notifications.AsNoTracking().SingleAsync();
        var opened = await h.Inbox.OpenAsync(notification.Id, h.CustomerCtx);

        Assert.False(opened.Allowed);
        Assert.Null(opened.DeepLink);
    }

    [Fact]
    public async Task AnEmployeeCannotOpenTheAdminCrmSettingsRouteFromAnAnnouncement()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        Assert.True(await h.Dispatcher.DispatchAsync(new NotificationRequest
        {
            Type = NotificationType.AdminAnnouncement,
            RecipientUserId = h.SalesUserId,
            DedupKey = "employee-admin-route",
            Title = "Internal route",
            Message = "Details",
            EntityType = NotificationEntityType.Announcement,
            DeepLink = "/crm/settings"
        }));

        var notification = await h.Db.Notifications.AsNoTracking().SingleAsync();
        var opened = await h.Inbox.OpenAsync(notification.Id, h.SalesCtx);

        Assert.False(opened.Allowed);
        Assert.Null(opened.DeepLink);
    }

    [Fact]
    public async Task ACustomerCannotOpenAnUnrelatedProjectFromALegacyNotification()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await SeedLegacyAsync(h, NotificationType.ProjectUpdated, h.SecondCustomerUserId,
            NotificationEntityType.Project, h.ProjectId, $"/projects/{h.ProjectId}");

        var notification = await h.Db.Notifications.AsNoTracking().SingleAsync();
        var opened = await h.Inbox.OpenAsync(notification.Id, h.SecondCustomerCtx);

        Assert.False(opened.Allowed);
        Assert.Null(opened.DeepLink);
    }

    [Fact]
    public async Task AnAccountNotificationCannotReferenceAnotherUsersAccount()
    {
        await using var h = await NotificationTestHarness.CreateAsync();

        var created = await h.Dispatcher.DispatchAsync(new NotificationRequest
        {
            Type = NotificationType.AccountSecurity,
            RecipientUserId = h.CustomerUserId,
            DedupKey = "wrong-account-resource",
            Title = "Account notice",
            Message = "Details",
            EntityType = NotificationEntityType.Account,
            EntityId = h.SecondCustomerUserId,
            DeepLink = "/notifications"
        });

        Assert.False(created);
        Assert.Empty(await h.NotificationsAsync());
    }

    [Fact]
    public async Task AnEmployeeCannotFollowANotificationIntoALeadTheyDoNotOwn()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        var lead = await SeedLeadAsync(h);

        Assert.False(await h.Dispatcher.DispatchAsync(new NotificationRequest
        {
            Type = NotificationType.LeadAssigned,
            RecipientUserId = h.OtherSalesUserId,
            DedupKey = "lead-wrong-owner",
            Title = "Lead assigned",
            Message = "Internal",
            EntityType = NotificationEntityType.Lead,
            EntityId = lead
        }));
        await SeedLegacyAsync(h, NotificationType.LeadAssigned, h.OtherSalesUserId,
            NotificationEntityType.Lead, lead, $"/crm/leads/{lead}");

        var notification = await h.Db.Notifications.AsNoTracking().FirstAsync();

        // Omar is not on the owning team and does not own the lead.
        var denied = await h.Inbox.OpenAsync(notification.Id, h.OtherSalesCtx);
        Assert.False(denied.Allowed);

        // The manager of the owning team may open it.
        await h.Dispatcher.DispatchAsync(new NotificationRequest
        {
            Type = NotificationType.ManagerAttentionRequired,
            RecipientUserId = h.ManagerUserId,
            DedupKey = "lead-manager",
            Title = "Attention required",
            Message = "Internal",
            EntityType = NotificationEntityType.Lead,
            EntityId = lead
        });

        var managerNotification = await h.Db.Notifications.AsNoTracking()
            .FirstAsync(n => n.RecipientUserId == h.ManagerUserId);
        var allowed = await h.Inbox.OpenAsync(managerNotification.Id, h.ManagerCtx);

        Assert.True(allowed.Allowed);
        Assert.Equal($"/crm/leads/{lead}", allowed.DeepLink);
    }

    [Fact]
    public async Task ANotificationWhoseRecordHasGoneGivesASafeAnswerRatherThanABrokenLink()
    {
        await using var h = await NotificationTestHarness.CreateAsync();

        Assert.False(await h.Dispatcher.DispatchAsync(new NotificationRequest
        {
            Type = NotificationType.ProjectUpdated, RecipientUserId = h.CustomerUserId,
            DedupKey = "missing-project", Title = "Project update", Message = "Details",
            EntityType = NotificationEntityType.Project, EntityId = 99_999
        }));
        await SeedLegacyAsync(h, NotificationType.ProjectUpdated, h.CustomerUserId,
            NotificationEntityType.Project, 99_999, "/projects/99999");

        var notification = await h.Db.Notifications.AsNoTracking().FirstAsync();
        var result = await h.Inbox.OpenAsync(notification.Id, h.CustomerCtx);

        Assert.False(result.Allowed);
        Assert.Contains("no longer available", result.Message);

        // It is still marked read, so it stops nagging.
        var stored = await h.Db.Notifications.AsNoTracking().FirstAsync(n => n.Id == notification.Id);
        Assert.True(stored.IsRead);
    }

    [Fact]
    public async Task ADeepLinkThatTriesToLeaveDamsIsRejected()
    {
        await using var h = await NotificationTestHarness.CreateAsync();

        await h.Dispatcher.DispatchAsync(new NotificationRequest
        {
            Type = NotificationType.AdminAnnouncement,
            RecipientUserId = h.CustomerUserId,
            DedupKey = "open-redirect",
            Title = "Announcement",
            Message = "Details",
            DeepLink = "https://evil.example.com/steal"
        });

        var notification = await h.Db.Notifications.AsNoTracking().FirstAsync();

        // It falls back to a safe in-app destination rather than carrying the outside URL.
        Assert.Equal("/notifications", notification.DeepLink);

        foreach (var attempt in new[] { "//evil.example.com", "/\\evil.example.com", "javascript:alert(1)", "/path\\x" })
            Assert.Null(NotificationLink.Sanitize(attempt));

        Assert.Equal("/crm/leads/7", NotificationLink.Sanitize("/crm/leads/7"));
    }

    [Fact]
    public async Task OnlyAnAdminCanChangeSettingsTemplatesRulesOrSendBroadcasts()
    {
        await using var h = await NotificationTestHarness.CreateAsync();

        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            h.Configuration.UpdateSettingsAsync(new UpdateNotificationSettingsDto
            {
                Values = { ["general.companyName"] = "Hostile Rename" }
            }, h.ManagerCtx));

        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            h.Configuration.SaveRuleAsync(NotificationType.LeadAssigned, new SaveNotificationRuleDto(), h.SalesCtx));

        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            h.Configuration.SaveTemplateAsync(NotificationType.LeadAssigned, NotificationChannel.Email,
                new SaveNotificationTemplateDto { Subject = "x", Body = "y" }, h.SalesCtx));

        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            h.Admin.ComposeAsync(new ComposeNotificationDto
            {
                Title = "Everyone",
                Message = "Hello",
                Audience = new NotificationAudienceDto { Type = NotificationAudienceType.AllCustomers }
            }, h.CustomerCtx));
    }

    [Fact]
    public async Task SecretsAreNeverReturnedToTheBrowserAndSurviveAFormRoundTrip()
    {
        await using var h = await NotificationTestHarness.CreateAsync();

        await h.Configuration.UpdateSettingsAsync(new UpdateNotificationSettingsDto
        {
            Values =
            {
                ["email.smtp.password"] = "super-secret-value",
                ["email.smtp.username"] = "notification-test-user",
                ["email.smtp.host"] = "smtp.test"
            }
        }, h.AdminCtx);

        var settings = await h.Configuration.GetSettingsAsync();

        Assert.DoesNotContain("super-secret-value", settings.Values.Values);
        Assert.False(settings.Values.ContainsKey("email.smtp.password"));
        Assert.Contains("email.smtp.password", settings.Secrets.Keys);
        Assert.DoesNotContain("super-secret-value", settings.Secrets["email.smtp.password"]);

        // Posting the mask back leaves the stored secret alone.
        await h.Configuration.UpdateSettingsAsync(new UpdateNotificationSettingsDto
        {
            Values = { ["email.smtp.password"] = NotificationConfigurationService.SecretPlaceholder }
        }, h.AdminCtx);

        var stored = await h.Db.NotificationSettings.AsNoTracking()
            .FirstAsync(s => s.Key == "email.smtp.password");
        Assert.Equal("super-secret-value", stored.Value);
    }

    private static async Task SeedLegacyAsync(
        NotificationTestHarness h,
        NotificationType type,
        int recipientUserId,
        NotificationEntityType entityType,
        int entityId,
        string deepLink)
    {
        var definition = NotificationCatalog.GetRequired(type);
        h.Db.Notifications.Add(new Notification
        {
            Type = type,
            Category = definition.Category,
            Module = definition.Module,
            Priority = definition.Priority,
            RecipientUserId = recipientUserId,
            EntityType = entityType,
            EntityId = entityId,
            Title = "Legacy notification",
            Message = "Legacy or externally corrupted row",
            DeepLink = deepLink,
            DedupKey = $"legacy:{type}:{recipientUserId}:{entityId}:{Guid.NewGuid()}"
        });
        await h.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task AnUnknownSettingKeyIsRefused()
    {
        await using var h = await NotificationTestHarness.CreateAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Configuration.UpdateSettingsAsync(new UpdateNotificationSettingsDto
            {
                Values = { ["push.vapid.privateKey"] = "attacker-supplied" }
            }, h.AdminCtx));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Configuration.UpdateSettingsAsync(new UpdateNotificationSettingsDto
            {
                Values = { ["Jwt:Key"] = "nope" }
            }, h.AdminCtx));
    }

    [Fact]
    public async Task ADeactivatedEmployeeDropsOutOfEveryStaffAudience()
    {
        await using var h = await NotificationTestHarness.CreateAsync();

        var before = await h.Recipients.ResolveAsync(NotificationAudienceType.AllSalesEmployees,
            new Interfaces.NotificationAudienceSelection());
        Assert.Contains(h.SalesUserId, before);

        var employee = await h.Db.Employees.FirstAsync(e => e.UserId == h.SalesUserId);
        employee.Status = EmployeeStatus.Terminated;
        await h.Db.SaveChangesAsync();

        var after = await h.Recipients.ResolveAsync(NotificationAudienceType.AllSalesEmployees,
            new Interfaces.NotificationAudienceSelection());
        Assert.DoesNotContain(h.SalesUserId, after);
    }

    private static NotificationRequest Announce(int userId, string key, string title) => new()
    {
        Type = NotificationType.AdminAnnouncement,
        RecipientUserId = userId,
        DedupKey = key,
        Title = title,
        Message = "Body"
    };

    internal static async Task<int> SeedLeadAsync(NotificationTestHarness h)
    {
        var employee = await h.Db.Employees.AsNoTracking().FirstAsync(e => e.UserId == h.SalesUserId);

        var lead = new Domain.Entities.Lead
        {
            LeadReference = "LD-000001",
            FirstName = "Bilal",
            LastName = "Khan",
            Phone = "03001234567",
            NormalizedPhone = "3001234567",
            LeadSourceId = 1,
            Stage = LeadStage.New,
            AssignedEmployeeId = employee.Id,
            AssignedTeamId = h.TeamId,
            AssignmentState = LeadAssignmentState.Assigned,
            CreatedAt = DateTime.UtcNow
        };

        h.Db.Leads.Add(lead);
        await h.Db.SaveChangesAsync();
        return lead.Id;
    }
}
