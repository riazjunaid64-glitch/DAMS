using DAMS.Application.Common;
using DAMS.Application.DTOs.LeadDtos;
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

        Assert.True(await h.Db.LeadNotifications.AnyAsync(
            n => n.RecipientUserId == h.SalesUserId && n.Type == LeadNotificationType.FirstContactOverdue));
        Assert.True(await h.Db.LeadNotifications.AnyAsync(
            n => n.RecipientUserId == h.ManagerUserId && n.IsEscalation));

        var countAfterFirst = await h.Db.LeadNotifications.CountAsync();

        // Repeat scans must not re-notify anybody.
        var second = await h.Alerts.RunScanAsync();
        Assert.Equal(0, second.NotificationsCreated);
        Assert.Equal(countAfterFirst, await h.Db.LeadNotifications.CountAsync());
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
        Assert.True(await h.Db.LeadNotifications.AnyAsync(
            n => n.RecipientUserId == h.SalesUserId && n.Type == LeadNotificationType.FollowUpOverdue));
        Assert.Equal(LeadFollowUpStatus.Pending,
            (await h.Db.LeadFollowUps.AsNoTracking().FirstAsync(f => f.Id == followUp.Id)).Status);

        // Long overdue: recorded as missed, and the manager is told.
        await SetFollowUpDueAsync(h, followUp.Id, DateTime.UtcNow.AddHours(-30));
        var missedScan = await h.Alerts.RunScanAsync();
        Assert.Equal(1, missedScan.FollowUpsMarkedMissed);
        Assert.Equal(LeadFollowUpStatus.Missed,
            (await h.Db.LeadFollowUps.AsNoTracking().FirstAsync(f => f.Id == followUp.Id)).Status);
        Assert.Contains(await h.TimelineAsync(leadId), a => a.Type == LeadActivityType.FollowUpMissed);
        Assert.True(await h.Db.LeadNotifications.AnyAsync(
            n => n.LeadId == leadId && n.IsEscalation && n.RecipientUserId == h.ManagerUserId));

        // And it is only written off once.
        var repeat = await h.Alerts.RunScanAsync();
        Assert.Equal(0, repeat.FollowUpsMarkedMissed);
    }

    [Fact]
    public async Task ASilentLeadIsFlaggedDailyRatherThanEveryScan()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        await SetLastActivityAsync(h, leadId, DateTime.UtcNow.AddDays(-30));

        var first = await h.Alerts.RunScanAsync();
        Assert.Equal(1, first.InactiveLeads);
        Assert.True(await h.Db.LeadNotifications.AnyAsync(
            n => n.LeadId == leadId && n.Type == LeadNotificationType.LeadInactive));

        var notificationCount = await h.Db.LeadNotifications.CountAsync();
        await SetLastActivityAsync(h, leadId, DateTime.UtcNow.AddDays(-30));
        await h.Alerts.RunScanAsync();

        Assert.Equal(notificationCount, await h.Db.LeadNotifications.CountAsync());
    }

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
        Assert.True(await h.Db.LeadNotifications.AnyAsync(
            n => n.RecipientUserId == h.SalesUserId && n.Type == LeadNotificationType.SiteVisitToday));

        // Ten hours after the slot, with no outcome recorded.
        h.Clock.Set(slot.AddHours(10));
        var missed = await h.Alerts.RunScanAsync();

        Assert.Equal(1, missed.SiteVisitsMarkedMissed);
        Assert.Equal(LeadSiteVisitStatus.Missed,
            (await h.Db.LeadSiteVisits.AsNoTracking().FirstAsync(v => v.Id == visit.Id)).Status);
        Assert.Contains(await h.TimelineAsync(leadId), a => a.Type == LeadActivityType.SiteVisitMissed);
    }

    [Fact]
    public async Task NotificationsCanBeReadAndCleared()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateLeadAsync();
        await h.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin);

        var mine = await h.Notifications.GetMyNotificationsAsync(h.Sales, unreadOnly: true, take: 50);
        Assert.NotEmpty(mine);
        Assert.Equal(mine.Count, await h.Notifications.GetUnreadCountAsync(h.Sales));

        await h.Notifications.MarkReadAsync(mine[0].Id, h.Sales);
        Assert.Equal(mine.Count - 1, await h.Notifications.GetUnreadCountAsync(h.Sales));

        await h.Notifications.MarkAllReadAsync(h.Sales);
        Assert.Equal(0, await h.Notifications.GetUnreadCountAsync(h.Sales));

        // Somebody else's notification cannot be touched.
        var adminNotification = await h.Db.LeadNotifications.AsNoTracking()
            .FirstAsync(n => n.RecipientUserId == h.AdminUserId);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Notifications.MarkReadAsync(adminNotification.Id, h.Sales));
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
