using DAMS.Application.Common;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Application.DTOs.NotificationDtos;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
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
            && n.Message.Contains($"{newDueAt:yyyy-MM-dd HH:mm}")));

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
            && n.Message.StartsWith($"{slot.AddHours(5):HH:mm}")));
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
    public async Task ManagerDashboardCoversTheTeamAndTheUnassignedQueue()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var teamLead = await h.CreateWorkedLeadAsync();
        await h.CreateLeadAsync(LeadTestHarness.Intake(firstName: "Nadia", phone: "03337776666", email: "n@x.com"));
        var outside = await h.CreateWorkedLeadAsync("03219998888");
        await h.Leads.AssignAsync(outside,
            new AssignLeadDto { EmployeeId = h.OtherSalesEmployeeId, Reason = "handover" }, h.Admin);

        await h.Leads.ConvertAsync(teamLead, new ConvertLeadDto { UnitId = h.UnitId }, h.Manager);

        var dashboard = await h.Reporting.GetManagerDashboardAsync(h.Manager);

        // The team's own lead plus the unassigned one; the other team's lead is excluded.
        Assert.Equal(2, dashboard.TeamLeads);
        Assert.Equal(1, dashboard.UnassignedLeads);
        Assert.Equal(1, dashboard.WonLeads);
        Assert.Equal(50d, dashboard.ConversionRatePercent);
        Assert.Contains(dashboard.ByEmployee, e => e.EmployeeId == h.SalesEmployeeId && e.WonLeads == 1);
        Assert.DoesNotContain(dashboard.ByEmployee, e => e.EmployeeId == h.OtherSalesEmployeeId);
    }

    [Fact]
    public async Task EmployeesCannotOpenTheTeamOrOrganisationDashboards()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        await Assert.ThrowsAsync<LeadAuthorizationException>(() => h.Reporting.GetManagerDashboardAsync(h.Sales));
        await Assert.ThrowsAsync<LeadAuthorizationException>(() => h.Reporting.GetAdminDashboardAsync(h.Sales, null, null));
        await Assert.ThrowsAsync<LeadAuthorizationException>(() => h.Reporting.GetAdminDashboardAsync(h.Manager, null, null));
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
        Assert.Contains(dashboard.ByTeam, t => t.TeamId == h.TeamId && t.WonLeads == 1);
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
