using DAMS.Application.Common;
using DAMS.Application.DTOs.NotificationDtos;
using DAMS.Application.Interfaces;
using DAMS.Application.Services.Notifications;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

public sealed class NotificationRoleEligibilityTests
{
    [Fact]
    public async Task CapabilitiesExposeOnlyCategoriesForTheCurrentDatabaseRole()
    {
        await using var h = await NotificationTestHarness.CreateAsync();

        var customer = await h.Preferences.GetCapabilitiesAsync(h.CustomerCtx);
        var employee = await h.Preferences.GetCapabilitiesAsync(h.SalesCtx);
        var manager = await h.Preferences.GetCapabilitiesAsync(h.ManagerCtx);
        var admin = await h.Preferences.GetCapabilitiesAsync(h.AdminCtx);

        Assert.Contains(customer.Categories, item => item.Category == NotificationCategory.PaymentsAndReceipts);
        Assert.DoesNotContain(customer.Categories, item => item.Category == NotificationCategory.LeadAssignments);
        Assert.Contains(employee.Categories, item => item.Category == NotificationCategory.LeadAssignments);
        Assert.DoesNotContain(employee.Categories, item => item.Category == NotificationCategory.ManagerEscalations);
        Assert.Contains(manager.Categories, item => item.Category == NotificationCategory.ManagerEscalations);
        Assert.DoesNotContain(admin.Categories, item => item.Category == NotificationCategory.PaymentsAndReceipts);

        var unknown = await h.Preferences.GetCapabilitiesAsync(new NotificationUserContext
        {
            UserId = h.CustomerUserId,
            Role = "Unknown",
            DisplayName = "Unknown",
            Email = "unknown@dams.test"
        });
        Assert.Empty(unknown.Categories);
    }

    [Fact]
    public async Task UnauthorizedCategoryFiltersAndPreferenceWritesAreRejected()
    {
        await using var h = await NotificationTestHarness.CreateAsync();

        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            h.Inbox.GetAsync(h.CustomerCtx, new NotificationFilterDto
            {
                Category = NotificationCategory.LeadAssignments
            }));

        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            h.Preferences.UpdateAsync(h.CustomerCtx, new UpdateNotificationPreferencesDto
            {
                Items =
                {
                    new UpdateNotificationPreferenceItemDto
                    {
                        Category = NotificationCategory.LeadAssignments,
                        EmailEnabled = true,
                        PushEnabled = true
                    }
                }
            }));
    }

    [Fact]
    public async Task StaffOnlyFanoutDropsCustomersAndEmployeesWithoutSupervisorEligibility()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        var targets = new[]
        {
            new NotificationRecipientTarget(h.CustomerUserId, null, null),
            new NotificationRecipientTarget(h.SalesUserId, null, null),
            new NotificationRecipientTarget(h.ManagerUserId, null, null),
            new NotificationRecipientTarget(h.AdminUserId, null, null)
        };

        var eligible = await h.Eligibility.FilterEligibleTargetsAsync(
            NotificationType.ManagerAttentionRequired, targets);

        Assert.Equal(new[] { h.AdminUserId, h.ManagerUserId },
            eligible.Select(item => item.UserId!.Value).OrderBy(id => id));
    }

    [Fact]
    public async Task ATaskCannotBeQueuedForAnEmployeeWhoIsNotAssignedToIt()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        var assignedEmployee = await h.Db.Employees.SingleAsync(employee => employee.UserId == h.SalesUserId);
        var task = new EmployeeTask { EmployeeId = assignedEmployee.Id, Title = "Prepare file" };
        h.Db.EmployeeTasks.Add(task);
        await h.Db.SaveChangesAsync();

        var created = await h.Dispatcher.DispatchAsync(new NotificationRequest
        {
            Type = NotificationType.EmployeeTaskAssigned,
            RecipientUserId = h.OtherSalesUserId,
            EntityType = NotificationEntityType.EmployeeTask,
            EntityId = task.Id,
            DedupKey = "wrong-task-owner",
            Title = "Task assigned",
            Message = "Prepare file"
        });

        Assert.False(created);
        Assert.Empty(await h.NotificationsAsync());
    }

    [Fact]
    public async Task AProjectUpdateCannotBeQueuedForUnrelatedStaff()
    {
        await using var h = await NotificationTestHarness.CreateAsync();

        var created = await h.Dispatcher.DispatchAsync(new NotificationRequest
        {
            Type = NotificationType.ProjectUpdated,
            RecipientUserId = h.SalesUserId,
            EntityType = NotificationEntityType.Project,
            EntityId = h.ProjectId,
            DedupKey = "unrelated-staff-project",
            Title = "Project updated",
            Message = "Progress changed"
        });

        Assert.False(created);
        Assert.Empty(await h.NotificationsAsync());
    }

    [Fact]
    public async Task ManagingALeadDoesNotFabricateAMentionForTheManager()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateLeadAsync();
        await h.Leads.AssignAsync(leadId,
            new DAMS.Application.DTOs.LeadDtos.AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin);

        var created = await h.Dispatcher.DispatchAsync(new NotificationRequest
        {
            Type = NotificationType.UserMentioned,
            RecipientUserId = h.ManagerUserId,
            EntityType = NotificationEntityType.Lead,
            EntityId = leadId,
            DedupKey = "manager-not-actually-mentioned",
            Title = "You were mentioned",
            Message = "Details"
        });

        Assert.False(created);
        Assert.DoesNotContain(await h.Db.Notifications.AsNoTracking().ToListAsync(),
            notification => notification.DedupKey == "manager-not-actually-mentioned");
    }

    [Fact]
    public async Task AnAdminIsNotEligibleForEmployeeWorkTheyDoNotHold()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        var lead = await NotificationSecurityTests.SeedLeadAsync(h);

        // The lead is Sana's. An admin supervises it but is not its assignee, was not
        // mentioned on it and holds none of its follow-up work.
        Assert.False(await h.Eligibility.CanReceiveAsync(
            h.AdminUserId, NotificationType.LeadAssigned, NotificationEntityType.Lead, lead));
        Assert.False(await h.Eligibility.CanReceiveAsync(
            h.AdminUserId, NotificationType.UserMentioned, NotificationEntityType.Lead, lead));
        Assert.False(await h.Eligibility.CanReceiveAsync(
            h.AdminUserId, NotificationType.FollowUpAssigned, NotificationEntityType.Lead, lead));
        Assert.False(await h.Eligibility.CanReceiveAsync(
            h.AdminUserId, NotificationType.SiteVisitReminder, NotificationEntityType.Lead, lead));

        // Supervisory events about the same lead are still theirs to receive.
        Assert.True(await h.Eligibility.CanReceiveAsync(
            h.AdminUserId, NotificationType.ManagerAttentionRequired, NotificationEntityType.Lead, lead));
        Assert.True(await h.Eligibility.CanReceiveAsync(
            h.AdminUserId, NotificationType.LeadStageChanged, NotificationEntityType.Lead, lead));
    }

    [Fact]
    public async Task AnAdminIsNotEligibleForATaskAssignedToSomebodyElse()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        var employee = await h.Db.Employees.AsNoTracking().FirstAsync(e => e.UserId == h.SalesUserId);

        var task = new EmployeeTask
        {
            EmployeeId = employee.Id,
            ProjectId = h.ProjectId,
            Title = "Prepare handover pack",
            Status = EmployeeTaskStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        h.Db.EmployeeTasks.Add(task);
        await h.Db.SaveChangesAsync();

        Assert.False(await h.Eligibility.CanReceiveAsync(
            h.AdminUserId, NotificationType.EmployeeTaskAssigned, NotificationEntityType.EmployeeTask, task.Id));
        Assert.True(await h.Eligibility.CanReceiveAsync(
            h.SalesUserId, NotificationType.EmployeeTaskAssigned, NotificationEntityType.EmployeeTask, task.Id));
    }

    [Fact]
    public async Task ACurrentRoleChangeImmediatelyHidesOldRoleNotifications()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        var paymentId = await h.RecordPaymentAsync();
        Assert.Contains((await h.Inbox.GetAsync(h.CustomerCtx, new NotificationFilterDto())).Items,
            item => item.EntityId == paymentId);

        var user = await h.Db.Users.SingleAsync(item => item.UserId == h.CustomerUserId);
        user.RoleId = 4;
        await h.Db.SaveChangesAsync();

        var changedContext = new NotificationUserContext
        {
            UserId = h.CustomerUserId,
            Role = LeadRoles.Employee,
            DisplayName = h.CustomerCtx.DisplayName,
            Email = h.CustomerCtx.Email
        };
        Assert.Empty((await h.Inbox.GetAsync(changedContext, new NotificationFilterDto())).Items);
    }

    [Fact]
    public async Task DeliveryRevalidatesEligibilityAfterARecipientRoleChanges()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.EnableChannelsAsync(email: true, push: false);
        await h.RecordPaymentAsync();

        var user = await h.Db.Users.SingleAsync(item => item.UserId == h.CustomerUserId);
        user.RoleId = 4;
        await h.Db.SaveChangesAsync();

        await h.Processor.ProcessDueDeliveriesAsync(20);

        var deliveries = await h.DeliveriesAsync();
        Assert.NotEmpty(deliveries);
        Assert.All(deliveries, delivery => Assert.Equal(NotificationDeliveryStatus.Skipped, delivery.Status));
        Assert.Empty(h.Email.Sent);
    }

    [Theory]
    [InlineData("smtp.example.com", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("https://smtp.example.com", false)]
    [InlineData("smtp.example.com:587", false)]
    [InlineData("smtp.example.com/path", false)]
    public void SmtpHostsAreValidatedAsHostsRatherThanUrls(string host, bool expected) =>
        Assert.Equal(expected, SmtpEmailSender.IsValidHost(host));
}
