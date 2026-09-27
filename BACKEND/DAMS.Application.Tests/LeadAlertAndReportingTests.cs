using System.Reflection;
using DAMS.Application.Common;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Application.DTOs.NotificationDtos;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace DAMS.Application.Tests;

public sealed class LeadAlertAndReportingTests
{
    // ── Escalation ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnUncontactedLeadEscalatesToTheManager_ButOnlyOnce()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateLeadAsync();
        await h.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin);
        await AgeAssignmentAsync(h, leadId, hours: 10);

        var first = await h.Alerts.RunScanAsync();
        Assert.Equal(1, first.FirstContactOverdue);
        Assert.True(first.EscalationsRaised > 0);

        Assert.True(await h.Db.Notifications.AnyAsync(
            n => n.RecipientUserId == h.SalesUserId && n.Type == NotificationType.FirstContactOverdue));
        Assert.True(await h.Db.Notifications.AnyAsync(
            n => n.RecipientUserId == h.ManagerUserId && n.IsEscalation));

        var countAfterFirst = await h.Db.Notifications.CountAsync();

        // Repeat scans must not re-notify anybody.
        var second = await h.Alerts.RunScanAsync();
        Assert.Equal(0, second.NotificationsCreated);
        Assert.Equal(countAfterFirst, await h.Db.Notifications.CountAsync());
    }

    [Fact]
    public async Task ContactedLeadsAreNotChasedForFirstContact()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        await AgeAssignmentAsync(h, leadId, hours: 10);

        var result = await h.Alerts.RunScanAsync();

        Assert.Equal(0, result.FirstContactOverdue);
    }

    [Fact]
    public async Task AnOverdueFollowUpAlertsTheOwnerThenBecomesMissedAndEscalates()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var followUp = await h.FollowUps.CreateAsync(leadId, new CreateLeadFollowUpDto
        {
            Title = "Call back",
            DueAt = DateTime.UtcNow.AddHours(1)
        }, h.Sales);

        // Slightly overdue: the owner is nudged, nothing is written off yet.
        await SetFollowUpDueAsync(h, followUp.Id, DateTime.UtcNow.AddHours(-3));
        var overdueScan = await h.Alerts.RunScanAsync();
        Assert.Equal(1, overdueScan.FollowUpsOverdue);
        Assert.True(await h.Db.Notifications.AnyAsync(
            n => n.RecipientUserId == h.SalesUserId && n.Type == NotificationType.FollowUpOverdue));
        Assert.Equal(LeadFollowUpStatus.Pending,
            (await h.Db.LeadFollowUps.AsNoTracking().FirstAsync(f => f.Id == followUp.Id)).Status);

        // Long overdue: recorded as missed, and the manager is told.
        await SetFollowUpDueAsync(h, followUp.Id, DateTime.UtcNow.AddHours(-30));
        var missedScan = await h.Alerts.RunScanAsync();
        Assert.Equal(1, missedScan.FollowUpsMarkedMissed);
        Assert.Equal(LeadFollowUpStatus.Missed,
            (await h.Db.LeadFollowUps.AsNoTracking().FirstAsync(f => f.Id == followUp.Id)).Status);
        Assert.Contains(await h.TimelineAsync(leadId), a => a.Type == LeadActivityType.FollowUpMissed);
        Assert.True(await h.Db.Notifications.AnyAsync(
            n => n.EntityType == NotificationEntityType.Lead && n.EntityId == leadId && n.IsEscalation && n.RecipientUserId == h.ManagerUserId));

        // And it is only written off once.
        var repeat = await h.Alerts.RunScanAsync();
        Assert.Equal(0, repeat.FollowUpsMarkedMissed);
    }

    [Fact]
    public async Task ARescheduledFollowUpIsRemindedAgainForItsNewTime_ButOncePerSchedule()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var followUp = await h.FollowUps.CreateAsync(leadId, new CreateLeadFollowUpDto
        {
            Title = "Call back",
            DueAt = DateTime.UtcNow.AddHours(1)
        }, h.Sales);

        // Before the reschedule: one reminder, however often the scan runs.
        Assert.Equal(1, (await h.Alerts.RunScanAsync()).NotificationsCreated);
        Assert.Equal(0, (await h.Alerts.RunScanAsync()).NotificationsCreated);
        Assert.Equal(1, await FollowUpAlertsAsync(h, NotificationType.FollowUpDue, h.SalesUserId));

        var newDueAt = DateTime.UtcNow.AddHours(2);
        await h.FollowUps.RescheduleAsync(followUp.Id, new RescheduleLeadFollowUpDto
        {
            DueAt = newDueAt,
            Reason = "Customer asked for later"
        }, h.Sales);

        // After the reschedule: the new time earns its own reminder, still only once.
        Assert.Equal(1, (await h.Alerts.RunScanAsync()).NotificationsCreated);
        Assert.Equal(0, (await h.Alerts.RunScanAsync()).NotificationsCreated);
        Assert.Equal(2, await FollowUpAlertsAsync(h, NotificationType.FollowUpDue, h.SalesUserId));
        Assert.True(await h.Db.Notifications.AnyAsync(n => n.Type == NotificationType.FollowUpDue
            && n.Message.Contains(LeadDisplay.When(newDueAt))));

        // Once completed it is never reminded again.
        await h.FollowUps.CompleteAsync(followUp.Id, new CompleteLeadFollowUpDto { Outcome = "Spoke to them" }, h.Sales);
        Assert.Equal(0, (await h.Alerts.RunScanAsync()).NotificationsCreated);
    }

    [Fact]
    public async Task AMissedFollowUpThatIsRescheduledAndMissedAgainEscalatesAgain()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var followUp = await h.FollowUps.CreateAsync(leadId, new CreateLeadFollowUpDto
        {
            Title = "Call back",
            DueAt = DateTime.UtcNow.AddHours(1)
        }, h.Sales);

        await SetFollowUpDueAsync(h, followUp.Id, DateTime.UtcNow.AddHours(-30));
        Assert.Equal(1, (await h.Alerts.RunScanAsync()).FollowUpsMarkedMissed);
        Assert.Equal(1, await FollowUpAlertsAsync(h, NotificationType.ManagerAttentionRequired, h.ManagerUserId));

        await h.FollowUps.RescheduleAsync(followUp.Id, new RescheduleLeadFollowUpDto
        {
            DueAt = DateTime.UtcNow.AddHours(1),
            Reason = "Customer was travelling"
        }, h.Sales);

        // The new time is missed as well: written off and escalated again, once.
        h.Clock.Set(h.Clock.UtcNow.AddHours(30));
        Assert.Equal(1, (await h.Alerts.RunScanAsync()).FollowUpsMarkedMissed);
        Assert.Equal(0, (await h.Alerts.RunScanAsync()).FollowUpsMarkedMissed);
        Assert.Equal(2, await FollowUpAlertsAsync(h, NotificationType.ManagerAttentionRequired, h.ManagerUserId));
    }

    [Fact]
    public async Task AnOverdueFollowUpThatIsRescheduledIsFlaggedOverdueAgainForItsNewTime()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var start = h.Clock.UtcNow;
        var leadId = await h.CreateWorkedLeadAsync();
        var followUp = await h.FollowUps.CreateAsync(leadId, new CreateLeadFollowUpDto
        {
            Title = "Call back",
            DueAt = start.AddHours(1)
        }, h.Sales);

        h.Clock.Set(start.AddHours(3));
        await h.Alerts.RunScanAsync();
        Assert.Equal(1, await FollowUpAlertsAsync(h, NotificationType.FollowUpOverdue, h.SalesUserId));

        await h.FollowUps.RescheduleAsync(followUp.Id, new RescheduleLeadFollowUpDto
        {
            DueAt = start.AddHours(5),
            Reason = "Customer asked for later"
        }, h.Sales);

        // Two hours past the new time: overdue again, and only once however often it is scanned.
        h.Clock.Set(start.AddHours(7));
        await h.Alerts.RunScanAsync();
        await h.Alerts.RunScanAsync();
        Assert.Equal(2, await FollowUpAlertsAsync(h, NotificationType.FollowUpOverdue, h.SalesUserId));
    }

    [Fact]
    public async Task AFollowUpMovedAwayAndBackToItsOriginalTimeIsRemindedForTheReturn()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var original = DateTime.UtcNow.AddHours(1);
        var followUp = await h.FollowUps.CreateAsync(leadId, new CreateLeadFollowUpDto
        {
            Title = "Call back",
            DueAt = original
        }, h.Sales);
        await h.Alerts.RunScanAsync();

        foreach (var dueAt in new[] { original.AddHours(1), original })
        {
            await h.FollowUps.RescheduleAsync(followUp.Id, new RescheduleLeadFollowUpDto
            {
                DueAt = dueAt,
                Reason = "Customer changed their mind"
            }, h.Sales);
            await h.Alerts.RunScanAsync();
        }

        Assert.Equal(3, await FollowUpAlertsAsync(h, NotificationType.FollowUpDue, h.SalesUserId));
    }

    [Fact]
    public async Task ACancelledFollowUpIsNeverReminded()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var start = h.Clock.UtcNow;
        var leadId = await h.CreateWorkedLeadAsync();
        var followUp = await h.FollowUps.CreateAsync(leadId, new CreateLeadFollowUpDto
        {
            Title = "Call back",
            DueAt = start.AddHours(1)
        }, h.Sales);
        await h.FollowUps.CancelAsync(followUp.Id, "Customer bought elsewhere", h.Sales);

        await h.Alerts.RunScanAsync();
        h.Clock.Set(start.AddHours(3));
        await h.Alerts.RunScanAsync();
        h.Clock.Set(start.AddHours(30));
        var late = await h.Alerts.RunScanAsync();

        Assert.Equal(0, late.FollowUpsMarkedMissed);
        Assert.Equal(0, await FollowUpAlertsAsync(h, NotificationType.FollowUpDue, h.SalesUserId));
        Assert.Equal(0, await FollowUpAlertsAsync(h, NotificationType.FollowUpOverdue, h.SalesUserId));
        Assert.Equal(0, await FollowUpAlertsAsync(h, NotificationType.ManagerAttentionRequired, h.ManagerUserId));
        Assert.Equal(LeadFollowUpStatus.Cancelled,
            (await h.Db.LeadFollowUps.AsNoTracking().FirstAsync(f => f.Id == followUp.Id)).Status);
    }

    [Fact]
    public async Task AFollowUpWithItsOwnReminderTimeIsRemindedThenRatherThanByTheDefaultWindow()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var start = h.Clock.UtcNow;
        var leadId = await h.CreateWorkedLeadAsync();

        // Due well outside the default window, but asked to be reminded earlier.
        await h.FollowUps.CreateAsync(leadId, new CreateLeadFollowUpDto
        {
            Title = "Early reminder",
            DueAt = start.AddHours(10),
            RemindAt = start.AddHours(6)
        }, h.Sales);
        // Due inside the default window, but asked to be reminded only shortly before.
        await h.FollowUps.CreateAsync(leadId, new CreateLeadFollowUpDto
        {
            Title = "Late reminder",
            DueAt = start.AddHours(3),
            RemindAt = start.AddHours(2)
        }, h.Sales);

        await h.Alerts.RunScanAsync();
        Assert.Equal(0, await FollowUpAlertsAsync(h, NotificationType.FollowUpDue, h.SalesUserId));

        h.Clock.Set(start.AddHours(2).AddMinutes(1));
        await h.Alerts.RunScanAsync();
        Assert.True(await h.Db.Notifications.AnyAsync(n => n.Type == NotificationType.FollowUpDue
            && n.Message.StartsWith("Late reminder")));
        Assert.Equal(1, await FollowUpAlertsAsync(h, NotificationType.FollowUpDue, h.SalesUserId));

        h.Clock.Set(start.AddHours(6).AddMinutes(1));
        await h.Alerts.RunScanAsync();
        await h.Alerts.RunScanAsync();
        Assert.True(await h.Db.Notifications.AnyAsync(n => n.Type == NotificationType.FollowUpDue
            && n.Message.StartsWith("Early reminder")));
        Assert.Equal(2, await FollowUpAlertsAsync(h, NotificationType.FollowUpDue, h.SalesUserId));
    }

    private static Task<int> FollowUpAlertsAsync(LeadTestHarness h, NotificationType type, int userId) =>
        h.Db.Notifications.CountAsync(n => n.Type == type && n.RecipientUserId == userId
            && (type != NotificationType.ManagerAttentionRequired || n.Title.StartsWith("Follow-up missed")));

    [Fact]
    public async Task ASilentLeadIsFlaggedDailyRatherThanEveryScan()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        await SetLastActivityAsync(h, leadId, DateTime.UtcNow.AddDays(-30));

        var first = await h.Alerts.RunScanAsync();
        Assert.Equal(1, first.InactiveLeads);
        Assert.True(await h.Db.Notifications.AnyAsync(
            n => n.EntityType == NotificationEntityType.Lead && n.EntityId == leadId && n.Type == NotificationType.LeadInactive));

        var notificationCount = await h.Db.Notifications.CountAsync();
        await SetLastActivityAsync(h, leadId, DateTime.UtcNow.AddDays(-30));
        await h.Alerts.RunScanAsync();

        Assert.Equal(notificationCount, await h.Db.Notifications.CountAsync());
    }

    // ── Scan progress ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AlreadyAlertedLeadsDoNotStopASmallFirstContactScanReachingTheRest()
    {
        await using var h = await LeadTestHarness.CreateAsync(new LeadAlertOptions { MaxRowsPerScan = 2 });
        var leadIds = new List<int>();
        for (var i = 0; i < 5; i++)
        {
            var leadId = await h.CreateLeadAsync(LeadTestHarness.Intake(
                phone: $"0300-100000{i}", email: $"first{i}@example.com"));
            await h.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin);
            // Oldest first, so the earliest leads are the ones that used to fill every batch.
            await AgeAssignmentAsync(h, leadId, hours: 50 - i);
            leadIds.Add(leadId);
        }

        var scans = new List<LeadAlertScanResultDto>();
        for (var i = 0; i < 3; i++)
            scans.Add(await h.Alerts.RunScanAsync());

        Assert.All(scans, s => Assert.InRange(s.FirstContactOverdue, 1, 2));
        Assert.All(leadIds, leadId => Assert.Equal(1, FirstContactAlertsFor(h, leadId)));

        // Everything has been seen: further scans do nothing and nobody is told twice.
        var settled = await h.Alerts.RunScanAsync();
        Assert.Equal(0, settled.FirstContactOverdue);
        Assert.Equal(0, settled.NotificationsCreated);
        Assert.All(leadIds, leadId => Assert.Equal(1, FirstContactAlertsFor(h, leadId)));
    }

    [Fact]
    public async Task AReassignedUncontactedLeadIsEvaluatedAgainForItsNewOwner()
    {
        await using var h = await LeadTestHarness.CreateAsync(new LeadAlertOptions { MaxRowsPerScan = 1 });
        var leadId = await h.CreateLeadAsync();
        await h.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin);
        await AgeAssignmentAsync(h, leadId, hours: 10);
        await h.Alerts.RunScanAsync();

        await h.Leads.AssignAsync(leadId,
            new AssignLeadDto { EmployeeId = h.OtherSalesEmployeeId, Reason = "handover" }, h.Admin);
        await AgeAssignmentAsync(h, leadId, hours: 5);
        var result = await h.Alerts.RunScanAsync();

        Assert.Equal(1, result.FirstContactOverdue);
        Assert.True(await h.Db.Notifications.AnyAsync(
            n => n.RecipientUserId == h.OtherSalesUserId && n.Type == NotificationType.FirstContactOverdue));
    }

    [Fact]
    public async Task ASmallInactivityScanCoversEveryQuietLeadEachDay_AndStillAlertsOncePerDay()
    {
        await using var h = await LeadTestHarness.CreateAsync(new LeadAlertOptions { MaxRowsPerScan = 2 });
        var leadIds = new List<int>();
        for (var i = 0; i < 5; i++)
        {
            var leadId = await h.CreateWorkedLeadAsync(phone: $"0300-200000{i}");
            await SetLastActivityAsync(h, leadId, DateTime.UtcNow.AddDays(-30 + i));
            leadIds.Add(leadId);
        }

        for (var i = 0; i < 3; i++)
            Assert.InRange((await h.Alerts.RunScanAsync()).InactiveLeads, 1, 2);
        Assert.All(leadIds, leadId => Assert.Equal(1, InactivityAlertsFor(h, leadId)));

        var settled = await h.Alerts.RunScanAsync();
        Assert.Equal(0, settled.InactiveLeads);
        Assert.Equal(0, settled.NotificationsCreated);

        // A new day starts a new round, and the whole backlog is reached again.
        h.Clock.Set(h.Clock.UtcNow.AddDays(1));
        for (var i = 0; i < 3; i++)
            await h.Alerts.RunScanAsync();
        Assert.All(leadIds, leadId => Assert.Equal(2, InactivityAlertsFor(h, leadId)));
    }

    private static int FirstContactAlertsFor(LeadTestHarness h, int leadId) =>
        h.Db.Notifications.Count(n => n.EntityType == NotificationEntityType.Lead && n.EntityId == leadId
                                      && n.Type == NotificationType.FirstContactOverdue);

    private static int InactivityAlertsFor(LeadTestHarness h, int leadId) =>
        h.Db.Notifications.Count(n => n.EntityType == NotificationEntityType.Lead && n.EntityId == leadId
                                      && n.Type == NotificationType.LeadInactive);

    [Fact]
    public async Task AClosedLeadStopsGeneratingAlerts()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        await SetLastActivityAsync(h, leadId, DateTime.UtcNow.AddDays(-30));

        var reasonId = await LeadIntakeAndDuplicateTests.ReasonIdAsync(h, "not_interested");
        await h.Leads.CloseAsync(leadId, dormant: false, new CloseLeadDto { ClosureReasonId = reasonId }, h.Sales);
        await SetLastActivityAsync(h, leadId, DateTime.UtcNow.AddDays(-30));

        var result = await h.Alerts.RunScanAsync();

        Assert.Equal(0, result.InactiveLeads);
        Assert.Equal(0, result.FirstContactOverdue);
    }

    [Fact]
    public async Task ASiteVisitIsAnnouncedOnTheDayAndWrittenOffWhenItPasses()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var slot = LeadTestHarness.FakeClock.TomorrowAt(10);
        var visit = await h.SiteVisits.ScheduleAsync(leadId, new ScheduleSiteVisitDto
        {
            ScheduledAt = slot,
            MeetingLocation = "Site office"
        }, h.Sales);

        // Two hours before the visit, on the same day.
        h.Clock.Set(slot.AddHours(-2));
        var today = await h.Alerts.RunScanAsync();
        Assert.Equal(1, today.SiteVisitsToday);
        Assert.True(await h.Db.Notifications.AnyAsync(
            n => n.RecipientUserId == h.SalesUserId && n.Type == NotificationType.SiteVisitReminder));

        // Ten hours after the slot, with no outcome recorded.
        h.Clock.Set(slot.AddHours(10));
        var missed = await h.Alerts.RunScanAsync();

        Assert.Equal(1, missed.SiteVisitsMarkedMissed);
        Assert.Equal(LeadSiteVisitStatus.Missed,
            (await h.Db.LeadSiteVisits.AsNoTracking().FirstAsync(v => v.Id == visit.Id)).Status);
        Assert.Contains(await h.TimelineAsync(leadId), a => a.Type == LeadActivityType.SiteVisitMissed);
    }

    [Fact]
    public async Task ASiteVisitMovedLaterTheSameDayIsAnnouncedAgainForTheNewTime()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var slot = LeadTestHarness.FakeClock.TomorrowAt(10);
        var visit = await h.SiteVisits.ScheduleAsync(leadId, new ScheduleSiteVisitDto
        {
            ScheduledAt = slot,
            MeetingLocation = "Site office"
        }, h.Sales);

        h.Clock.Set(slot.AddHours(-2));
        await h.Alerts.RunScanAsync();

        await h.SiteVisits.RescheduleAsync(visit.Id, new RescheduleSiteVisitDto
        {
            ScheduledAt = slot.AddHours(5),
            Reason = "Customer delayed"
        }, h.Sales);
        await h.Alerts.RunScanAsync();
        await h.Alerts.RunScanAsync();

        Assert.Equal(2, await h.Db.Notifications.CountAsync(
            n => n.RecipientUserId == h.SalesUserId && n.Type == NotificationType.SiteVisitReminder));
        Assert.True(await h.Db.Notifications.AnyAsync(n => n.Type == NotificationType.SiteVisitReminder
            && n.Message.Contains(LeadDisplay.When(slot.AddHours(5)))));
    }

    [Fact]
    public async Task SiteVisitAdvanceReminderUsesConfiguredLeadDays()
    {
        Assert.Equal("Site visit reminder: {{leadName}}",
            NotificationCatalog.GetRequired(NotificationType.SiteVisitReminder).DefaultSubject);
        await using var h = await LeadTestHarness.CreateAsync();
        h.Db.NotificationRules.Add(new NotificationRule
        {
            Type = NotificationType.SiteVisitReminder, ReminderLeadDays = 2, RemindOnDueDate = false
        });
        await h.Db.SaveChangesAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var slot = DateTime.UtcNow.Date.AddDays(5).AddHours(10);
        await h.SiteVisits.ScheduleAsync(leadId, new ScheduleSiteVisitDto
        {
            ScheduledAt = slot, MeetingLocation = "Site office"
        }, h.Sales);

        h.Clock.Set(slot.AddDays(-3));
        await h.Alerts.RunScanAsync();
        Assert.Equal(0, await SiteVisitRemindersAsync(h));

        h.Clock.Set(slot.AddDays(-2));
        await h.Alerts.RunScanAsync();
        await h.Alerts.RunScanAsync();
        Assert.Equal(1, await SiteVisitRemindersAsync(h));
    }

    [Fact]
    public async Task SiteVisitLeadDaysUsePakistanCalendarDate()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var slot = DateTime.UtcNow.Date.AddDays(5).AddHours(20).AddMinutes(30);
        await h.SiteVisits.ScheduleAsync(leadId, new ScheduleSiteVisitDto
        {
            ScheduledAt = slot, MeetingLocation = "Site office"
        }, h.Sales);

        // These UTC instants fall on consecutive Pakistan business dates.
        h.Clock.Set(slot.AddDays(-1));
        await h.Alerts.RunScanAsync();

        Assert.Equal(1, await SiteVisitRemindersAsync(h));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task SiteVisitSameDayReminderFollowsRule(bool remindOnDueDate, int expected)
    {
        await using var h = await LeadTestHarness.CreateAsync();
        h.Db.NotificationRules.Add(new NotificationRule
        {
            Type = NotificationType.SiteVisitReminder, ReminderLeadDays = 0,
            RemindOnDueDate = remindOnDueDate
        });
        await h.Db.SaveChangesAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var slot = DateTime.UtcNow.Date.AddDays(5).AddHours(10);
        await h.SiteVisits.ScheduleAsync(leadId, new ScheduleSiteVisitDto
        {
            ScheduledAt = slot, MeetingLocation = "Site office"
        }, h.Sales);

        h.Clock.Set(slot.AddDays(-1));
        await h.Alerts.RunScanAsync();
        Assert.Equal(0, await SiteVisitRemindersAsync(h));

        h.Clock.Set(slot.AddHours(-2));
        await h.Alerts.RunScanAsync();
        Assert.Equal(expected, await SiteVisitRemindersAsync(h));
    }

    [Fact]
    public async Task SiteVisitOwnReminderTimeOverridesLeadDaysAndStillAllowsSameDayReminder()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        h.Db.NotificationRules.Add(new NotificationRule
        {
            Type = NotificationType.SiteVisitReminder, ReminderLeadDays = 3, RemindOnDueDate = true
        });
        await h.Db.SaveChangesAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var slot = DateTime.UtcNow.Date.AddDays(5).AddHours(10);
        var reminder = slot.AddDays(-1);
        await h.SiteVisits.ScheduleAsync(leadId, new ScheduleSiteVisitDto
        {
            ScheduledAt = slot, RemindAt = reminder, MeetingLocation = "Site office"
        }, h.Sales);

        h.Clock.Set(slot.AddDays(-3));
        await h.Alerts.RunScanAsync();
        Assert.Equal(0, await SiteVisitRemindersAsync(h));

        h.Clock.Set(reminder);
        await h.Alerts.RunScanAsync();
        Assert.Equal(1, await SiteVisitRemindersAsync(h));

        h.Clock.Set(slot.AddHours(-2));
        await h.Alerts.RunScanAsync();
        Assert.Equal(2, await SiteVisitRemindersAsync(h));
    }

    [Fact]
    public async Task DisabledSiteVisitReminderRuleStillAllowsMissedVisitEscalation()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        h.Db.NotificationRules.Add(new NotificationRule
        {
            Type = NotificationType.SiteVisitReminder, IsEnabled = false,
            ReminderLeadDays = 3, RemindOnDueDate = true
        });
        await h.Db.SaveChangesAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var slot = DateTime.UtcNow.Date.AddDays(5).AddHours(10);
        var visit = await h.SiteVisits.ScheduleAsync(leadId, new ScheduleSiteVisitDto
        {
            ScheduledAt = slot, RemindAt = slot.AddDays(-2), MeetingLocation = "Site office"
        }, h.Sales);

        h.Clock.Set(slot.AddDays(-2));
        await h.Alerts.RunScanAsync();
        h.Clock.Set(slot.AddHours(-2));
        await h.Alerts.RunScanAsync();
        Assert.Equal(0, await SiteVisitRemindersAsync(h));

        h.Clock.Set(slot.AddHours(10));
        Assert.Equal(1, (await h.Alerts.RunScanAsync()).SiteVisitsMarkedMissed);
        Assert.Equal(LeadSiteVisitStatus.Missed,
            (await h.Db.LeadSiteVisits.AsNoTracking().SingleAsync(v => v.Id == visit.Id)).Status);
        Assert.True(await h.Db.Notifications.AnyAsync(n => n.Type == NotificationType.SiteVisitMissed
            && n.RecipientUserId == h.ManagerUserId));
    }

    [Fact]
    public async Task ReschedulingAfterAnAdvanceReminderAllowsOneNewAdvanceReminder()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        h.Db.NotificationRules.Add(new NotificationRule
        {
            Type = NotificationType.SiteVisitReminder, ReminderLeadDays = 2, RemindOnDueDate = false
        });
        await h.Db.SaveChangesAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var slot = DateTime.UtcNow.Date.AddDays(5).AddHours(10);
        var visit = await h.SiteVisits.ScheduleAsync(leadId, new ScheduleSiteVisitDto
        {
            ScheduledAt = slot, MeetingLocation = "Site office"
        }, h.Sales);

        h.Clock.Set(slot.AddDays(-2));
        await h.Alerts.RunScanAsync();
        Assert.Equal(1, await SiteVisitRemindersAsync(h));

        var nextSlot = slot.AddDays(2);
        await h.SiteVisits.RescheduleAsync(visit.Id, new RescheduleSiteVisitDto
        {
            ScheduledAt = nextSlot, Reason = "Customer delayed"
        }, h.Sales);
        h.Clock.Set(nextSlot.AddDays(-2));
        await h.Alerts.RunScanAsync();
        await h.Alerts.RunScanAsync();
        Assert.Equal(2, await SiteVisitRemindersAsync(h));
    }

    [Fact]
    public async Task RepeatedLimitedScansReachLaterDueVisits()
    {
        await using var h = await LeadTestHarness.CreateAsync(new LeadAlertOptions { MaxRowsPerScan = 1 });
        var slot = DateTime.UtcNow.Date.AddDays(5).AddHours(10);
        var firstLead = await h.CreateWorkedLeadAsync();
        var secondLead = await h.CreateWorkedLeadAsync(phone: "0300-7654321");
        await h.SiteVisits.ScheduleAsync(firstLead, new ScheduleSiteVisitDto
        {
            ScheduledAt = slot, MeetingLocation = "First office"
        }, h.Sales);
        await h.SiteVisits.ScheduleAsync(secondLead, new ScheduleSiteVisitDto
        {
            ScheduledAt = slot.AddHours(1), MeetingLocation = "Second office"
        }, h.Sales);

        h.Clock.Set(slot.AddDays(-1));
        await h.Alerts.RunScanAsync();
        await h.Alerts.RunScanAsync();

        Assert.Equal(2, await SiteVisitRemindersAsync(h));
    }

    [Fact]
    public async Task RescheduledVisitRejectsReminderAfterItsNewTime()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var slot = DateTime.UtcNow.Date.AddDays(5).AddHours(10);
        var visit = await h.SiteVisits.ScheduleAsync(leadId, new ScheduleSiteVisitDto
        {
            ScheduledAt = slot, MeetingLocation = "Site office"
        }, h.Sales);

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.SiteVisits.RescheduleAsync(
            visit.Id, new RescheduleSiteVisitDto
            {
                ScheduledAt = slot.AddHours(1), RemindAt = slot.AddHours(2), Reason = "Customer delayed"
            }, h.Sales));
    }

    private static Task<int> SiteVisitRemindersAsync(LeadTestHarness h) =>
        h.Db.Notifications.CountAsync(n => n.Type == NotificationType.SiteVisitReminder);

    [Fact]
    public async Task AMissedSiteVisitThatIsRescheduledAndMissedAgainEscalatesAgain()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var slot = LeadTestHarness.FakeClock.TomorrowAt(10);
        var visit = await h.SiteVisits.ScheduleAsync(leadId, new ScheduleSiteVisitDto
        {
            ScheduledAt = slot,
            MeetingLocation = "Site office"
        }, h.Sales);

        h.Clock.Set(slot.AddHours(10));
        Assert.Equal(1, (await h.Alerts.RunScanAsync()).SiteVisitsMarkedMissed);

        var nextSlot = slot.AddDays(1);
        await h.SiteVisits.RescheduleAsync(visit.Id, new RescheduleSiteVisitDto
        {
            ScheduledAt = nextSlot,
            Reason = "Customer was ill"
        }, h.Sales);

        h.Clock.Set(nextSlot.AddHours(10));
        Assert.Equal(1, (await h.Alerts.RunScanAsync()).SiteVisitsMarkedMissed);
        Assert.Equal(0, (await h.Alerts.RunScanAsync()).SiteVisitsMarkedMissed);
        Assert.Equal(2, await h.Db.Notifications.CountAsync(
            n => n.RecipientUserId == h.ManagerUserId && n.Type == NotificationType.SiteVisitMissed));
    }

    /// <summary>
    /// Lead alerts are read through the central notification inbox after the migration, and
    /// the isolation guarantee is unchanged: one user cannot touch another's row.
    /// </summary>
    [Fact]
    public async Task NotificationsCanBeReadAndCleared()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateLeadAsync();
        await h.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin);

        var sales = h.Notify(h.Sales);

        var mine = await h.Inbox.GetAsync(sales, new NotificationFilterDto { UnreadOnly = true, PageSize = 50 });
        Assert.NotEmpty(mine.Items);
        Assert.Equal(mine.Items.Count, await h.Inbox.GetUnreadCountAsync(sales));

        await h.Inbox.MarkReadAsync(mine.Items[0].Id, sales);
        Assert.Equal(mine.Items.Count - 1, await h.Inbox.GetUnreadCountAsync(sales));

        await h.Inbox.MarkAllReadAsync(sales, category: null);
        Assert.Equal(0, await h.Inbox.GetUnreadCountAsync(sales));

        // Somebody else's notification cannot be touched, and is reported as missing rather
        // than forbidden so ids cannot be probed.
        var adminNotification = await h.Db.Notifications.AsNoTracking()
            .FirstAsync(n => n.RecipientUserId == h.AdminUserId);
        await Assert.ThrowsAsync<LeadNotFoundException>(() =>
            h.Inbox.MarkReadAsync(adminNotification.Id, sales));
    }

    // ── Reporting ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task EmployeeDashboardShowsTheirOwnWorkload()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var mine = await h.CreateWorkedLeadAsync();
        var theirs = await h.CreateWorkedLeadAsync("03219998888");
        await h.Leads.AssignAsync(theirs,
            new AssignLeadDto { EmployeeId = h.OtherSalesEmployeeId, Reason = "handover" }, h.Admin);

        var slot = LeadTestHarness.FakeClock.TomorrowAt(10);
        await h.FollowUps.CreateAsync(mine, new CreateLeadFollowUpDto
        {
            Title = "Call back", DueAt = slot
        }, h.Sales);
        await h.SiteVisits.ScheduleAsync(mine, new ScheduleSiteVisitDto
        {
            ScheduledAt = slot.AddDays(1), MeetingLocation = "Site office"
        }, h.Sales);

        // Morning of the day the follow-up is due.
        h.Clock.Set(slot.AddHours(-2));
        var dashboard = await h.Reporting.GetEmployeeDashboardAsync(h.Sales);

        Assert.Equal(1, dashboard.ActiveLeads);
        Assert.Equal(1, dashboard.FollowUpsDueToday);
        Assert.Equal(0, dashboard.OverdueFollowUps);
        Assert.Equal(1, dashboard.UpcomingSiteVisits);
        Assert.Single(dashboard.NextSiteVisits);
        Assert.Contains(dashboard.ByStage, s => s.Stage == LeadStage.SiteVisitScheduled && s.Count == 1);
    }

    [Fact]
    public async Task KAN39_SalespersonDashboardCountsTheFourSimpleStages_AndTheListFiltersByThem()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var inProgress = await h.CreateWorkedLeadAsync("0300-1110001");

        var fresh = await h.CreateLeadAsync(LeadTestHarness.Intake(firstName: "Nadia", phone: "03337776666", email: "n@x.com"));
        await h.Leads.AssignAsync(fresh, new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin);

        var won = await h.CreateWorkedLeadAsync("0300-1110002");
        await h.Leads.ConvertAsync(won, new ConvertLeadDto { UnitId = h.UnitId }, h.Admin);

        var lost = await h.CreateWorkedLeadAsync("0300-1110003");
        await h.Leads.CloseAsync(lost, dormant: false, new CloseLeadDto
        {
            ClosureReasonId = await LeadIntakeAndDuplicateTests.ReasonIdAsync(h, "not_interested")
        }, h.Sales);

        var dormant = await h.CreateWorkedLeadAsync("0300-1110004");
        await h.Leads.CloseAsync(dormant, dormant: true, new CloseLeadDto
        {
            ClosureReasonId = await LeadIntakeAndDuplicateTests.ReasonIdAsync(h, "delayed_decision")
        }, h.Sales);

        var theirs = await h.CreateWorkedLeadAsync("03219998888");
        await h.Leads.AssignAsync(theirs,
            new AssignLeadDto { EmployeeId = h.OtherSalesEmployeeId, Reason = "handover" }, h.Admin);

        var dashboard = await h.Reporting.GetEmployeeDashboardAsync(h.Sales);

        Assert.Equal(5, dashboard.TotalAssigned);
        Assert.Equal(1, dashboard.NewLeads);
        Assert.Equal(2, dashboard.InProgressLeads);
        Assert.Equal(1, dashboard.Conversions);
        Assert.Equal(1, dashboard.LostLeads);
        Assert.Equal(1, dashboard.DormantLeads);
        Assert.Equal(dashboard.TotalAssigned,
            dashboard.InProgressLeads + dashboard.Conversions + dashboard.LostLeads + dashboard.DormantLeads);

        async Task<int[]> IdsIn(LeadStageGroup group) =>
            (await h.Leads.GetLeadsAsync(new LeadFilterDto { StageGroup = group }, h.Sales))
                .Items.Select(l => l.Id).OrderBy(id => id).ToArray();

        Assert.Equal(new[] { fresh }, await IdsIn(LeadStageGroup.New));
        Assert.Equal(new[] { fresh, inProgress }.OrderBy(id => id).ToArray(), await IdsIn(LeadStageGroup.InProgress));
        Assert.Equal(new[] { won }, await IdsIn(LeadStageGroup.Won));
        Assert.Equal(new[] { lost }, await IdsIn(LeadStageGroup.Lost));
        Assert.Equal(new[] { dormant }, await IdsIn(LeadStageGroup.Dormant));
    }

    [Fact]
    public void KAN41_StatusGroupsCoverEveryStageOnceExceptNewAlsoSittingInProgress()
    {
        foreach (var stage in Enum.GetValues<LeadStage>())
        {
            var groups = Enum.GetValues<LeadStageGroup>()
                .Where(g => LeadStageRules.StagesIn(g).Contains(stage))
                .ToList();
            if (LeadStageRules.NewStages.Contains(stage))
            {
                Assert.Contains(LeadStageGroup.New, groups);
                Assert.Contains(LeadStageGroup.InProgress, groups);
                Assert.Equal(2, groups.Count);
                Assert.Equal(LeadStageGroup.New, LeadStageRules.GroupOf(stage));
            }
            else
            {
                Assert.True(groups.Count == 1, $"{stage} sits in {groups.Count} simple stages.");
                Assert.Equal(groups[0], LeadStageRules.GroupOf(stage));
            }
        }
    }

    [Fact]
    public async Task KAN39_LeadResponsesCarryTheSimpleStage_SoTheScreensNeverRegroupIt()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var fresh = await h.CreateLeadAsync(LeadTestHarness.Intake(firstName: "Sana", phone: "03001112222", email: "s@x.com"));
        var worked = await h.CreateWorkedLeadAsync("03002223333");

        var list = await h.Leads.GetLeadsAsync(new LeadFilterDto(), h.Admin);
        Assert.Equal(LeadStageGroup.New, list.Items.Single(l => l.Id == fresh).StageGroup);
        Assert.Equal(LeadStageGroup.InProgress, list.Items.Single(l => l.Id == worked).StageGroup);

        var json = System.Text.Json.JsonSerializer.Serialize(list.Items.Single(l => l.Id == worked));
        Assert.Contains("\"StageGroup\"", json);
    }

    [Fact]
    public async Task EmployeesCannotOpenTheOrganisationDashboard()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        await Assert.ThrowsAsync<LeadAuthorizationException>(() => h.Reporting.GetAdminDashboardAsync(h.Sales, null, null));
    }

    [Fact]
    public async Task ManagerSeesTheSameOrganisationDashboardAsAdmin()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        await h.CreateWorkedLeadAsync();
        await h.CreateLeadAsync(LeadTestHarness.Intake(firstName: "Nadia", phone: "03337776666", email: "n@x.com"));
        var outside = await h.CreateWorkedLeadAsync("03219998888");
        await h.Leads.AssignAsync(outside,
            new AssignLeadDto { EmployeeId = h.OtherSalesEmployeeId, Reason = "handover" }, h.Admin);

        var admin = await h.Reporting.GetAdminDashboardAsync(h.Admin, null, null);
        var manager = await h.Reporting.GetAdminDashboardAsync(h.Manager, null, null);

        Assert.Equal(3, manager.TotalLeads);
        Assert.Equal(admin.TotalLeads, manager.TotalLeads);
        Assert.Equal(admin.UnassignedLeads, manager.UnassignedLeads);
        Assert.Contains(manager.ByEmployee, e => e.EmployeeId == h.OtherSalesEmployeeId);
    }

    [Fact]
    public async Task OrganisationDashboardAttributesOutcomesToSourceAndCampaign()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        var facebook = LeadTestHarness.Intake(firstName: "Ali", phone: "03001110000", email: "ali@x.com", sourceCode: "facebook");
        facebook.CampaignName = "Summer Launch";
        var won = await h.CreateLeadAsync(facebook);
        await h.Leads.AssignAsync(won, new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin);
        await h.Communications.RecordAsync(won, new RecordLeadCommunicationDto
        {
            Channel = LeadCommunicationChannel.Phone,
            Direction = LeadCommunicationDirection.Outbound,
            Summary = "Interested."
        }, h.Sales);
        await h.Leads.ConvertAsync(won, new ConvertLeadDto { UnitId = h.UnitId }, h.Admin);

        var lost = await h.CreateLeadAsync(LeadTestHarness.Intake(
            firstName: "Sara", phone: "03002220000", email: "sara@x.com", sourceCode: "facebook"));
        var reasonId = await LeadIntakeAndDuplicateTests.ReasonIdAsync(h, "budget_issue");
        await h.Leads.CloseAsync(lost, dormant: false, new CloseLeadDto { ClosureReasonId = reasonId }, h.Admin);

        await h.CreateLeadAsync(LeadTestHarness.Intake(
            firstName: "Zeeshan", phone: "03003330000", email: "z@x.com", sourceCode: "walk_in"));

        var dashboard = await h.Reporting.GetAdminDashboardAsync(h.Admin, null, null);

        Assert.Equal(3, dashboard.TotalLeads);
        Assert.Equal(1, dashboard.WonLeads);
        Assert.Equal(1, dashboard.LostLeads);
        Assert.Equal(1, dashboard.OpenLeads);
        Assert.Equal(33.33d, dashboard.ConversionRatePercent);
        Assert.NotNull(dashboard.AverageFirstResponseHours);
        Assert.NotNull(dashboard.AverageConversionDays);

        var facebookRow = dashboard.BySource.Single(s => s.SourceCode == "facebook");
        Assert.Equal(2, facebookRow.TotalLeads);
        Assert.Equal(1, facebookRow.WonLeads);
        Assert.Equal(1, facebookRow.LostLeads);
        Assert.Equal(50d, facebookRow.SourceToBookingPercent);

        var campaign = dashboard.ByCampaign.Single();
        Assert.Equal("Summer Launch", campaign.CampaignName);
        Assert.Equal(1, campaign.WonLeads);

        Assert.Contains(dashboard.LossReasons, r => r.ReasonName == "Budget issue" && r.Count == 1);
        Assert.Contains(dashboard.ByEmployee, e => e.EmployeeId == h.SalesEmployeeId && e.WonLeads == 1);
    }

    [Fact]
    public async Task OrganisationDashboardRespectsTheDateRange()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var oldLead = await h.CreateLeadAsync();
        await SetCreatedAtAsync(h, oldLead, DateTime.UtcNow.AddDays(-60));
        await h.CreateLeadAsync(LeadTestHarness.Intake(firstName: "Recent", phone: "03219998888", email: "r@x.com"));

        var recentOnly = await h.Reporting.GetAdminDashboardAsync(h.Admin, DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);

        Assert.Equal(1, recentOnly.TotalLeads);
        Assert.Equal(2, (await h.Reporting.GetAdminDashboardAsync(h.Admin, null, null)).TotalLeads);
    }

    // ── KAN-46: Dormant leads come back on their "Bring back on" date ──────────────

    [Fact]
    public async Task KAN46_ADormantLeadComesBackFromNineOnItsDate_WithACallBackTimelineAndNotification_Once()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var bringBackOn = PakistanTime.Today.AddDays(1);
        await MarkDormantAsync(h, leadId, bringBackOn);
        var closedAt = (await h.LoadLeadAsync(leadId)).ClosedAt!.Value;
        var dormantEntryId = (await h.TimelineAsync(leadId)).Single(a => a.Type == LeadActivityType.LeadDormant).Id;

        // The day before, however late, and its own day before 09:00 PKT: nothing happens.
        h.Clock.Set(PakistanAt(bringBackOn.AddDays(-1), 18));
        Assert.Equal(0, (await h.Alerts.RunScanAsync()).DormantLeadsBroughtBack);
        h.Clock.Set(PakistanAt(bringBackOn, 8, 59));
        Assert.Equal(0, (await h.Alerts.RunScanAsync()).DormantLeadsBroughtBack);
        Assert.Equal(LeadStage.Dormant, (await h.LoadLeadAsync(leadId)).Stage);

        h.Clock.Set(PakistanAt(bringBackOn, 9, 5));
        var scan = await h.Alerts.RunScanAsync();
        Assert.Equal(1, scan.DormantLeadsBroughtBack);
        Assert.Equal(1, scan.NotificationsCreated);

        var lead = await h.LoadLeadAsync(leadId);
        Assert.Equal(LeadStage.Contacted, lead.Stage);
        Assert.Equal(h.SalesEmployeeId, lead.AssignedEmployeeId);
        Assert.Null(lead.ReactivateOn);
        Assert.Null(lead.ClosedAt);
        Assert.Null(lead.ClosureReasonId);

        var followUp = Assert.Single(await h.Db.LeadFollowUps.AsNoTracking()
            .Where(f => f.LeadId == leadId && f.Status == LeadFollowUpStatus.Pending).ToListAsync());
        Assert.Equal(LeadFollowUpType.Call, followUp.Type);
        Assert.Equal("Call back — brought back from Dormant", followUp.Title);
        Assert.Equal(h.SalesEmployeeId, followUp.AssignedEmployeeId);
        Assert.Equal(PakistanAt(bringBackOn, 10), followUp.DueAt);
        Assert.Null(followUp.CreatedByUserId);
        Assert.Equal(followUp.DueAt, lead.NextActionAt);
        Assert.Equal(followUp.Title, lead.NextActionSummary);

        // One timeline entry for the return, written by the system.
        var since = (await h.TimelineAsync(leadId)).Where(a => a.Id > dormantEntryId).ToList();
        var entry = Assert.Single(since);
        Assert.Equal(LeadActivityType.LeadReopened, entry.Type);
        Assert.True(entry.IsSystemGenerated);
        Assert.Equal($"Brought back from Dormant as planned (Dormant since {LeadDisplay.Day(closedAt)}, reason: Delayed decision).",
            entry.Summary);

        var notice = Assert.Single(await BringBackNoticesAsync(h, leadId));
        Assert.Equal(h.SalesUserId, notice.RecipientUserId);
        Assert.Equal("Bring back today: Bilal Khan — was Dormant (Delayed decision)", notice.Title);

        // Re-running the scan brings nothing back twice.
        Assert.Equal(0, (await h.Alerts.RunScanAsync()).DormantLeadsBroughtBack);
        Assert.Single(await h.Db.LeadFollowUps.AsNoTracking().Where(f => f.LeadId == leadId).ToListAsync());
        Assert.Single(await h.TimelineAsync(leadId), a => a.Type == LeadActivityType.LeadReopened);
        Assert.Single(await BringBackNoticesAsync(h, leadId));
    }

    [Theory]
    [InlineData(9, 30)]
    [InlineData(11, 30)]
    public async Task KAN46_ALeadWhoseDayWasMissedComesBackNextScan_WithTheCallBackAnHourOut(int hour, int minute)
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var bringBackOn = PakistanTime.Today.AddDays(1);
        await MarkDormantAsync(h, leadId, bringBackOn);

        // The scanner was off on the day. 10:00 on the day it was due has long passed, even
        // when the next scan runs before 10:00 the following morning.
        var now = PakistanAt(bringBackOn.AddDays(1), hour, minute);
        h.Clock.Set(now);
        Assert.Equal(1, (await h.Alerts.RunScanAsync()).DormantLeadsBroughtBack);

        var followUp = await h.Db.LeadFollowUps.AsNoTracking().SingleAsync(f => f.LeadId == leadId);
        Assert.Equal(now.AddHours(1), followUp.DueAt);
    }

    [Fact]
    public async Task KAN46_ALeadReopenedByHandFirstIsNotBroughtBackAgain()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var bringBackOn = PakistanTime.Today.AddDays(1);
        await MarkDormantAsync(h, leadId, bringBackOn);
        await h.Leads.ReopenAsync(leadId, new ReopenLeadDto { Stage = LeadStage.Contacted, Reason = "Customer called early" }, h.Sales);

        h.Clock.Set(PakistanAt(bringBackOn, 9, 30));
        Assert.Equal(0, (await h.Alerts.RunScanAsync()).DormantLeadsBroughtBack);

        Assert.Single(await h.TimelineAsync(leadId), a => a.Type == LeadActivityType.LeadReopened);
        Assert.Empty(await BringBackNoticesAsync(h, leadId));
        Assert.Empty(await h.Db.LeadFollowUps.AsNoTracking().Where(f => f.LeadId == leadId).ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KAN46_WithoutAWorkingOwnerAdminsAndManagersAreToldAndNoCallBackIsCreated(bool noOwner)
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var bringBackOn = PakistanTime.Today.AddDays(1);
        await MarkDormantAsync(h, leadId, bringBackOn);

        if (noOwner)
        {
            var dormant = await h.Db.Leads.FirstAsync(l => l.Id == leadId);
            dormant.AssignedEmployeeId = null;
        }
        else
        {
            var owner = await h.Db.Employees.FirstAsync(e => e.Id == h.SalesEmployeeId);
            owner.Status = EmployeeStatus.Inactive;
        }
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        h.Clock.Set(PakistanAt(bringBackOn, 9, 30));
        Assert.Equal(1, (await h.Alerts.RunScanAsync()).DormantLeadsBroughtBack);

        var lead = await h.LoadLeadAsync(leadId);
        Assert.Equal(LeadStage.Contacted, lead.Stage);
        Assert.Equal(noOwner ? null : h.SalesEmployeeId, lead.AssignedEmployeeId);
        Assert.Empty(await h.Db.LeadFollowUps.AsNoTracking().Where(f => f.LeadId == leadId).ToListAsync());

        var notices = await BringBackNoticesAsync(h, leadId);
        Assert.Equal(new[] { h.AdminUserId, h.ManagerUserId }.OrderBy(id => id),
            notices.Select(n => n.RecipientUserId!.Value).OrderBy(id => id));
        Assert.All(notices, n => Assert.Equal(NotificationType.ManagerAttentionRequired, n.Type));
    }

    [Fact]
    public async Task KAN46_ALeadWhosePersonAlreadyHasAnotherOpenLeadStaysDormant_AndTheOwnerIsTold()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var bringBackOn = PakistanTime.Today.AddDays(1);
        await MarkDormantAsync(h, leadId, bringBackOn);
        var reference = (await h.LoadLeadAsync(leadId)).LeadReference;

        // The same person enquired again while this lead was Dormant.
        var newer = await h.CreateLeadAsync(LeadTestHarness.Intake(phone: "0300-1234567", email: "someone.else@example.com"));
        var newerReference = (await h.LoadLeadAsync(newer)).LeadReference;
        Assert.NotEqual(reference, newerReference);

        h.Clock.Set(PakistanAt(bringBackOn, 9, 30));
        var scan = await h.Alerts.RunScanAsync();
        Assert.Equal(0, scan.DormantLeadsBroughtBack);
        Assert.True(scan.NotificationsCreated >= 1);

        var lead = await h.LoadLeadAsync(leadId);
        Assert.Equal(LeadStage.Dormant, lead.Stage);
        Assert.Null(lead.ReactivateOn);
        Assert.Contains(await h.TimelineAsync(leadId),
            a => a.Type == LeadActivityType.SystemAlert && a.Summary.Contains(newerReference));
        var notice = Assert.Single(await BringBackNoticesAsync(h, leadId));
        Assert.Equal(h.SalesUserId, notice.RecipientUserId);
        Assert.Contains(newerReference, notice.Message);

        // Handled once: the next scan leaves it alone.
        await h.Alerts.RunScanAsync();
        Assert.Single(await BringBackNoticesAsync(h, leadId));
    }

    [Fact]
    public async Task KAN46_ALeadNobodyContactedComesBackAsContacted_WithTheCallBackThatChasesIt()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateLeadAsync();
        await h.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin);
        Assert.Null((await h.LoadLeadAsync(leadId)).LastContactAt);
        var bringBackOn = PakistanTime.Today.AddDays(1);
        await MarkDormantAsync(h, leadId, bringBackOn);

        h.Clock.Set(PakistanAt(bringBackOn, 9, 30));
        Assert.Equal(1, (await h.Alerts.RunScanAsync()).DormantLeadsBroughtBack);

        var lead = await h.LoadLeadAsync(leadId);
        Assert.Equal(LeadStage.Contacted, lead.Stage);
        Assert.Equal(LeadStageGroup.InProgress, LeadStageRules.GroupOf(lead.Stage));
        var followUp = await h.Db.LeadFollowUps.AsNoTracking().SingleAsync(f => f.LeadId == leadId);
        Assert.Equal(LeadFollowUpStatus.Pending, followUp.Status);
        Assert.Equal(h.SalesEmployeeId, followUp.AssignedEmployeeId);
    }

    [Fact]
    public async Task KAN46_ALeadThatFailsToComeBackDoesNotHoldBackTheLeadsDueAfterIt()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var failing = await h.CreateWorkedLeadAsync("0300-1111111");
        var healthy = await h.CreateWorkedLeadAsync("0300-2222222");
        var bringBackOn = PakistanTime.Today.AddDays(1);
        await MarkDormantAsync(h, failing, bringBackOn);
        await MarkDormantAsync(h, healthy, bringBackOn);
        Assert.True(failing < healthy, "The failing lead must be the first one the scan reaches.");

        // The first lead stages a change and then fails, as a lost lock or a database error would.
        var leads = FailingBringBack.For(h.Leads, h.Db, failing);
        var alerts = new LeadAlertService(h.Db, h.Notifications, leads, Options.Create(new LeadAlertOptions()), h.Clock);

        h.Clock.Set(PakistanAt(bringBackOn, 9, 30));
        var scan = await alerts.RunScanAsync();

        Assert.Equal(1, scan.DormantLeadsFailed);
        Assert.Equal(1, scan.DormantLeadsBroughtBack);
        Assert.Equal(LeadStage.Contacted, (await h.LoadLeadAsync(healthy)).Stage);

        // Nothing the failed attempt staged was written with the next lead's save, and its date
        // is still there, so the next scan tries it again.
        var stuck = await h.LoadLeadAsync(failing);
        Assert.Equal(LeadStage.Dormant, stuck.Stage);
        Assert.NotNull(stuck.ReactivateOn);

        Assert.Equal(1, (await h.Alerts.RunScanAsync()).DormantLeadsBroughtBack);
        Assert.Equal(LeadStage.Contacted, (await h.LoadLeadAsync(failing)).Stage);
    }

    /// <summary>The real lead service, except that bringing one chosen lead back fails.</summary>
    public class FailingBringBack : DispatchProxy
    {
        private ILeadService _inner = null!;
        private AppDbContext _db = null!;
        private int _failFor;

        internal static ILeadService For(ILeadService inner, AppDbContext db, int failFor)
        {
            var proxy = Create<ILeadService, FailingBringBack>();
            var self = (FailingBringBack)(object)proxy;
            (self._inner, self._db, self._failFor) = (inner, db, failFor);
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(ILeadService.BringBackDormantAsync) && (int)args![0]! == _failFor)
                return FailAsync();

            try
            {
                return method.Invoke(_inner, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }

        private async Task<DormantBringBackResultDto> FailAsync()
        {
            var lead = await _db.Leads.FirstAsync(l => l.Id == _failFor);
            lead.Stage = LeadStage.Contacted;
            throw new InvalidOperationException("Simulated failure while bringing the lead back.");
        }
    }

    private static async Task MarkDormantAsync(LeadTestHarness h, int leadId, DateTime bringBackOn)
    {
        // Sent as the Lost / dormant popup sends it: the picked date at midnight UTC.
        await h.Leads.CloseAsync(leadId, dormant: true, new CloseLeadDto
        {
            ClosureReasonId = await LeadIntakeAndDuplicateTests.ReasonIdAsync(h, "delayed_decision"),
            ReactivateOn = DateTime.SpecifyKind(bringBackOn, DateTimeKind.Utc)
        }, h.Sales);
        h.Db.ChangeTracker.Clear();
    }

    private static DateTime PakistanAt(DateTime date, int hour, int minute = 0) =>
        PakistanTime.StartOfBusinessDateUtc(date).AddHours(hour).AddMinutes(minute);

    private static Task<List<Notification>> BringBackNoticesAsync(LeadTestHarness h, int leadId) =>
        h.Db.Notifications.AsNoTracking()
            .Where(n => n.EntityType == NotificationEntityType.Lead && n.EntityId == leadId
                        && n.Title.StartsWith("Bring back today"))
            .ToListAsync();

    // ── KAN-48: the leads list's cards, search and dates ─────────────────────────

    [Fact]
    public async Task KAN48_TheSummaryCountsEachStatusOfTheCallersLeads_ExactlyAsTheListFiltersThem()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        await h.CreateWorkedLeadAsync("0300-1110001");

        var fresh = await h.CreateLeadAsync(LeadTestHarness.Intake(firstName: "Nadia", phone: "03337776666", email: "n@x.com"));
        await h.Leads.AssignAsync(fresh, new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin);

        var won = await h.CreateWorkedLeadAsync("0300-1110002");
        await h.Leads.ConvertAsync(won, new ConvertLeadDto { UnitId = h.UnitId }, h.Admin);

        var lost = await h.CreateWorkedLeadAsync("0300-1110003");
        await h.Leads.CloseAsync(lost, dormant: false, new CloseLeadDto
        {
            ClosureReasonId = await LeadIntakeAndDuplicateTests.ReasonIdAsync(h, "not_interested")
        }, h.Sales);

        var dormant = await h.CreateWorkedLeadAsync("0300-1110004");
        await h.Leads.CloseAsync(dormant, dormant: true, new CloseLeadDto
        {
            ClosureReasonId = await LeadIntakeAndDuplicateTests.ReasonIdAsync(h, "delayed_decision")
        }, h.Sales);

        var theirs = await h.CreateWorkedLeadAsync("03219998888");
        await h.Leads.AssignAsync(theirs,
            new AssignLeadDto { EmployeeId = h.OtherSalesEmployeeId, Reason = "handover" }, h.Admin);

        await h.CreateLeadAsync(LeadTestHarness.Intake(firstName: "Omar", phone: "03331112222", email: "o@x.com"));

        // In progress includes the new leads nobody has contacted yet; Lost leaves Dormant out.
        Assert.Equal((7, 4, 1, 1, 1), Counts(await h.Leads.GetSummaryAsync(new LeadFilterDto(), h.Admin)));
        Assert.Equal((7, 4, 1, 1, 1), Counts(await h.Leads.GetSummaryAsync(new LeadFilterDto(), h.Manager)));
        Assert.Equal((5, 2, 1, 1, 1), Counts(await h.Leads.GetSummaryAsync(new LeadFilterDto(), h.Sales)));

        // Each card is the total the list reaches when that status is picked.
        foreach (var ctx in new[] { h.Admin, h.Sales })
        {
            var summary = await h.Leads.GetSummaryAsync(new LeadFilterDto(), ctx);
            async Task<int> Listed(LeadStageGroup? group) =>
                (await h.Leads.GetLeadsAsync(new LeadFilterDto { StageGroup = group }, ctx)).TotalCount;

            Assert.Equal(await Listed(null), summary.Total);
            Assert.Equal(await Listed(LeadStageGroup.InProgress), summary.InProgress);
            Assert.Equal(await Listed(LeadStageGroup.Won), summary.Won);
            Assert.Equal(await Listed(LeadStageGroup.Lost), summary.Lost);
            Assert.Equal(await Listed(LeadStageGroup.Dormant), summary.Dormant);
        }

        // The list's other filters apply; its status filters do not, as every card shows its own.
        Assert.Equal((1, 1, 0, 0, 0), Counts(await h.Leads.GetSummaryAsync(new LeadFilterDto { Unassigned = true }, h.Admin)));
        Assert.Equal((1, 1, 0, 0, 0), Counts(await h.Leads.GetSummaryAsync(
            new LeadFilterDto { AssignedEmployeeId = h.OtherSalesEmployeeId }, h.Admin)));
        Assert.Equal((1, 1, 0, 0, 0), Counts(await h.Leads.GetSummaryAsync(new LeadFilterDto { SearchTerm = "Nadia" }, h.Admin)));
        Assert.Equal((7, 4, 1, 1, 1), Counts(await h.Leads.GetSummaryAsync(
            new LeadFilterDto { Stage = LeadStage.Won, StageGroup = LeadStageGroup.Lost }, h.Admin)));

        await Assert.ThrowsAsync<LeadAuthorizationException>(() => h.Leads.GetSummaryAsync(new LeadFilterDto(), h.Client));
    }

    [Fact]
    public async Task KAN48_SearchFindsAFullNameAndACity_AsTheSearchBoxPromises()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var hamza = LeadTestHarness.Intake(firstName: "Hamza", phone: "03001230001", email: "hamza@x.com");
        hamza.LastName = "Iqbal";
        hamza.City = "Islamabad";
        var hamzaId = await h.CreateLeadAsync(hamza);
        var other = LeadTestHarness.Intake(firstName: "Hamza", phone: "03001230002", email: "ali@x.com");
        other.LastName = "Ali";
        other.City = "Lahore";
        var otherId = await h.CreateLeadAsync(other);

        async Task<int[]> Found(string search) =>
            (await h.Leads.GetLeadsAsync(new LeadFilterDto { SearchTerm = search }, h.Admin))
                .Items.Select(l => l.Id).OrderBy(id => id).ToArray();

        Assert.Equal(new[] { hamzaId }, await Found("Hamza Iqbal"));
        Assert.Equal(new[] { hamzaId }, await Found(" hamza iqbal "));
        Assert.Equal(new[] { hamzaId }, await Found("Islam"));
        Assert.Equal(new[] { otherId }, await Found("lahore"));
        Assert.Equal(new[] { hamzaId, otherId }, await Found("Hamza"));
        Assert.Equal(1, (await h.Leads.GetSummaryAsync(new LeadFilterDto { SearchTerm = "Hamza Iqbal" }, h.Admin)).Total);
    }

    [Fact]
    public async Task KAN48_FromAndToArePakistanDays_ForTheListAndTheSummary()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var day = new DateTime(2026, 9, 27);

        async Task<int> CreatedAtAsync(string phone, DateTime utc)
        {
            var leadId = await h.CreateLeadAsync(LeadTestHarness.Intake(phone: phone, email: $"{phone}@x.com"));
            await SetCreatedAtAsync(h, leadId, utc);
            return leadId;
        }

        var dayBefore = await CreatedAtAsync("03001110001", PakistanAt(day.AddDays(-1), 23, 30));
        var earlyMorning = await CreatedAtAsync("03001110002", PakistanAt(day, 2));        // 21:00 UTC the day before
        var lateEvening = await CreatedAtAsync("03001110003", PakistanAt(day, 23, 59));
        var dayAfter = await CreatedAtAsync("03001110004", PakistanAt(day.AddDays(1), 0, 30)); // 19:30 UTC the same day

        async Task<int[]> Created(DateTime from, DateTime to) =>
            (await h.Leads.GetLeadsAsync(new LeadFilterDto { CreatedFrom = from, CreatedTo = to }, h.Admin))
                .Items.Select(l => l.Id).OrderBy(id => id).ToArray();

        // As a date box sends the day ("2026-09-27"), and as toISOString() sends it from a date
        // picked as UTC midnight or as Pakistan midnight (19:00 UTC the day before).
        var utcMidnight = DateTime.SpecifyKind(day, DateTimeKind.Utc);
        var pakistanMidnight = PakistanTime.StartOfBusinessDateUtc(day);
        Assert.Equal(new[] { earlyMorning, lateEvening }, await Created(day, day));
        Assert.Equal(new[] { earlyMorning, lateEvening }, await Created(utcMidnight, utcMidnight));
        Assert.Equal(new[] { earlyMorning, lateEvening }, await Created(pakistanMidnight, pakistanMidnight));
        Assert.Equal(new[] { dayBefore }, await Created(day.AddDays(-1), day.AddDays(-1)));
        Assert.Equal(new[] { dayAfter }, await Created(day.AddDays(1), day.AddDays(1)));
        Assert.Equal(2, (await h.Leads.GetSummaryAsync(new LeadFilterDto { CreatedFrom = day, CreatedTo = day }, h.Admin)).Total);
    }

    [Fact]
    public async Task KAN48_TheListSendsSlimRows_WithOnlyWhatTheListShows()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        await h.CreateWorkedLeadAsync();

        var row = Assert.Single((await h.Leads.GetLeadsAsync(new LeadFilterDto(), h.Admin)).Items);
        Assert.IsType<LeadListItemDto>(row);

        // Named as the API writes them (camelCase).
        var json = System.Text.Json.JsonSerializer.SerializeToElement(row,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Equal(
            new[]
            {
                "id", "leadReference", "firstName", "lastName", "fullName", "phone", "city", "propertyType",
                "paymentPreference", "purchaseIntent", "sourceName", "stage", "stageGroup", "assignedEmployeeId",
                "assignedEmployeeName", "lastActivityAt", "lastActivitySummary", "nextActionAt", "nextActionSummary",
                "createdAt"
            }.Order(),
            json.EnumerateObject().Select(p => p.Name).Order());
    }

    private static (int Total, int InProgress, int Won, int Lost, int Dormant) Counts(LeadSummaryDto summary) =>
        (summary.Total, summary.InProgress, summary.Won, summary.Lost, summary.Dormant);

    // ── Helpers that move the clock on stored rows ──────────────────────────────

    private static async Task AgeAssignmentAsync(LeadTestHarness h, int leadId, int hours)
    {
        var lead = await h.Db.Leads.FirstAsync(l => l.Id == leadId);
        lead.AssignedAt = DateTime.UtcNow.AddHours(-hours);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
    }

    private static async Task SetLastActivityAsync(LeadTestHarness h, int leadId, DateTime when)
    {
        var lead = await h.Db.Leads.FirstAsync(l => l.Id == leadId);
        lead.LastActivityAt = when;
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
    }

    private static async Task SetCreatedAtAsync(LeadTestHarness h, int leadId, DateTime when)
    {
        var lead = await h.Db.Leads.FirstAsync(l => l.Id == leadId);
        lead.CreatedAt = when;
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
    }

    private static async Task SetFollowUpDueAsync(LeadTestHarness h, int followUpId, DateTime when)
    {
        var followUp = await h.Db.LeadFollowUps.FirstAsync(f => f.Id == followUpId);
        followUp.DueAt = when;
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
    }

}
