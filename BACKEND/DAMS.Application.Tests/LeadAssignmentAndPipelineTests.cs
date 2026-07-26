using DAMS.Application.Common;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

public sealed class LeadAssignmentAndPipelineTests
{
    // ── Assignment ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Assigning_SetsOwner_MovesStage_AndAlertsTheOwner()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateLeadAsync();

        var lead = await h.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin);

        Assert.Equal(h.SalesEmployeeId, lead.AssignedEmployeeId);
        Assert.Equal(h.TeamId, lead.AssignedTeamId);
        Assert.Equal(LeadAssignmentState.Assigned, lead.AssignmentState);
        Assert.Equal(LeadStage.FirstContactPending, lead.Stage);
        Assert.NotNull(lead.AssignedAt);

        Assert.True(await h.Db.Notifications.AnyAsync(
            n => n.EntityType == NotificationEntityType.Lead && n.EntityId == leadId && n.RecipientUserId == h.SalesUserId && n.Type == NotificationType.LeadAssigned));

        var history = await h.Leads.GetAssignmentHistoryAsync(leadId, h.Admin);
        Assert.Single(history);
        Assert.Equal(h.SalesEmployeeId, history[0].AssignedEmployeeId);
        Assert.Equal(h.AdminUserId, history[0].AssignedByUserId);
    }

    [Fact]
    public async Task Reassignment_KeepsPreviousOwner_AndRequiresAReason()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateLeadAsync();
        await h.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin);

        var missingReason = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.OtherSalesEmployeeId }, h.Admin));
        Assert.Contains("reason", missingReason.Message, StringComparison.OrdinalIgnoreCase);

        var lead = await h.Leads.AssignAsync(leadId,
            new AssignLeadDto { EmployeeId = h.OtherSalesEmployeeId, Reason = "Sana is on leave." }, h.Admin);

        Assert.Equal(LeadAssignmentState.Reassigned, lead.AssignmentState);
        Assert.Equal(h.OtherSalesEmployeeId, lead.AssignedEmployeeId);

        var history = await h.Leads.GetAssignmentHistoryAsync(leadId, h.Admin);
        Assert.Equal(2, history.Count);
        var latest = history[0];
        Assert.Equal(h.SalesEmployeeId, latest.PreviousEmployeeId);
        Assert.Equal("Sana is on leave.", latest.Reason);

        var timeline = await h.TimelineAsync(leadId);
        Assert.Contains(timeline, a => a.Type == LeadActivityType.LeadReassigned);
    }

    [Fact]
    public async Task ManagerCannotAssignOutsideTheirTeam()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateLeadAsync();

        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            h.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.OtherSalesEmployeeId }, h.Manager));

        var lead = await h.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Manager);
        Assert.Equal(h.SalesEmployeeId, lead.AssignedEmployeeId);
    }

    [Fact]
    public async Task EmployeeCannotChangeOwnership()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateLeadAsync();

        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            h.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Sales));
    }

    [Fact]
    public async Task InactiveEmployeeCannotReceiveLeads()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateLeadAsync();

        var employee = await h.Db.Employees.FirstAsync(e => e.Id == h.SalesEmployeeId);
        employee.Status = EmployeeStatus.Terminated;
        await h.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin));
    }

    [Fact]
    public async Task UnassigningReturnsTheLeadToTheQueue()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateLeadAsync();
        await h.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin);

        var lead = await h.Leads.AssignAsync(leadId, new AssignLeadDto { Reason = "Wrong territory." }, h.Admin);

        Assert.Null(lead.AssignedEmployeeId);
        Assert.Equal(LeadAssignmentState.Unassigned, lead.AssignmentState);
        var timeline = await h.TimelineAsync(leadId);
        Assert.Contains(timeline, a => a.Type == LeadActivityType.LeadUnassigned);
    }

    // ── Stage rules ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidTransitions_AreAccepted_AndRecorded()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();

        // Recording the call already moved the lead to Contacted.
        Assert.Equal(LeadStage.Contacted, (await h.LoadLeadAsync(leadId)).Stage);

        await h.Leads.ChangeStageAsync(leadId, new ChangeLeadStageDto { Stage = LeadStage.Qualified }, h.Sales);
        await h.Leads.ChangeStageAsync(leadId, new ChangeLeadStageDto { Stage = LeadStage.Negotiation }, h.Sales);
        var lead = await h.Leads.ChangeStageAsync(leadId,
            new ChangeLeadStageDto { Stage = LeadStage.BookingPending }, h.Sales);

        Assert.Equal(LeadStage.BookingPending, lead.Stage);

        var stageChanges = (await h.TimelineAsync(leadId)).Where(a => a.Type == LeadActivityType.StageChanged).ToList();
        Assert.True(stageChanges.Count >= 3);
        Assert.Contains(stageChanges, a => a.PreviousValue == "Qualified" && a.NewValue == "Negotiation");
    }

    [Fact]
    public async Task InvalidTransition_IsRejectedWithAClearMessage()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateLeadAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.ChangeStageAsync(leadId, new ChangeLeadStageDto { Stage = LeadStage.Negotiation }, h.Admin));

        Assert.Contains("cannot move from New to Negotiation", error.Message);
        Assert.Equal(LeadStage.New, (await h.LoadLeadAsync(leadId)).Stage);
    }

    [Fact]
    public async Task StageCannotBeSetToWonDirectly()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.ChangeStageAsync(leadId, new ChangeLeadStageDto { Stage = LeadStage.Won }, h.Admin));

        Assert.Contains("converting", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LostAndDormantMustGoThroughClosureSoAReasonIsCaptured()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();

        foreach (var stage in new[] { LeadStage.Lost, LeadStage.Dormant })
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                h.Leads.ChangeStageAsync(leadId, new ChangeLeadStageDto { Stage = stage }, h.Admin));
            Assert.Contains("reason is required", error.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ContactedRequiresARecordedConversation()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateLeadAsync();
        await h.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.ChangeStageAsync(leadId, new ChangeLeadStageDto { Stage = LeadStage.Contacted }, h.Sales));

        Assert.Contains("Record the call", error.Message);
    }

    [Fact]
    public async Task SiteVisitStagesRequireTheVisitToExist()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();

        var scheduled = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.ChangeStageAsync(leadId, new ChangeLeadStageDto { Stage = LeadStage.SiteVisitScheduled }, h.Sales));
        Assert.Contains("Schedule the site visit first", scheduled.Message);
    }

    [Fact]
    public async Task QualificationChange_IsRecorded()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();

        var lead = await h.Leads.UpdateQualificationAsync(leadId,
            new UpdateLeadQualificationDto { Qualification = LeadQualification.Hot }, h.Sales);

        Assert.Equal(LeadQualification.Hot, lead.Qualification);
        Assert.Contains(await h.TimelineAsync(leadId), a => a.Type == LeadActivityType.QualificationChanged);
    }

    // ── Closure and reopening ───────────────────────────────────────────────────

    [Fact]
    public async Task LostRequiresAValidReason_AndPreservesHistory()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var historyBefore = (await h.TimelineAsync(leadId)).Count;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.CloseAsync(leadId, dormant: false, new CloseLeadDto { ClosureReasonId = 9999 }, h.Sales));

        var reasonId = await LeadIntakeAndDuplicateTests.ReasonIdAsync(h, "purchased_elsewhere");
        var lead = await h.Leads.CloseAsync(leadId, dormant: false,
            new CloseLeadDto { ClosureReasonId = reasonId, Notes = "Bought from a competitor." }, h.Sales);

        Assert.Equal(LeadStage.Lost, lead.Stage);
        Assert.Equal("Purchased elsewhere", lead.ClosureReasonName);
        Assert.NotNull(lead.ClosedAt);

        var timeline = await h.TimelineAsync(leadId);
        Assert.True(timeline.Count > historyBefore);
        Assert.Contains(timeline, a => a.Type == LeadActivityType.LeadLost);
        // Nothing that came before was thrown away.
        Assert.Contains(timeline, a => a.Type == LeadActivityType.LeadCreated);
    }

    [Fact]
    public async Task DormantAcceptsAFutureFollowUpDate_ButLostDoesNot()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var dormantLead = await h.CreateWorkedLeadAsync();
        var lostLead = await h.CreateWorkedLeadAsync("03211112222");
        var reasonId = await LeadIntakeAndDuplicateTests.ReasonIdAsync(h, "delayed_decision");

        var lead = await h.Leads.CloseAsync(dormantLead, dormant: true, new CloseLeadDto
        {
            ClosureReasonId = reasonId,
            ReactivateOn = DateTime.UtcNow.AddMonths(3)
        }, h.Sales);

        Assert.Equal(LeadStage.Dormant, lead.Stage);
        Assert.NotNull(lead.ReactivateOn);

        var notInterestedId = await LeadIntakeAndDuplicateTests.ReasonIdAsync(h, "not_interested");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.CloseAsync(lostLead, dormant: false, new CloseLeadDto
            {
                ClosureReasonId = notInterestedId,
                ReactivateOn = DateTime.UtcNow.AddMonths(1)
            }, h.Sales));

        Assert.Contains("dormant leads only", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClosureReasonMustMatchTheKindOfClosure()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var lostOnlyReason = await LeadIntakeAndDuplicateTests.ReasonIdAsync(h, "purchased_elsewhere");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.CloseAsync(leadId, dormant: true, new CloseLeadDto { ClosureReasonId = lostOnlyReason }, h.Sales));

        Assert.Contains("cannot be used", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClosingCancelsOpenWorkSoAClosedLeadStopsNagging()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();

        await h.FollowUps.CreateAsync(leadId, new CreateLeadFollowUpDto
        {
            Title = "Call back",
            DueAt = DateTime.UtcNow.AddDays(1)
        }, h.Sales);

        await h.SiteVisits.ScheduleAsync(leadId, new ScheduleSiteVisitDto
        {
            ScheduledAt = DateTime.UtcNow.AddDays(2),
            MeetingLocation = "Site office"
        }, h.Sales);

        var reasonId = await LeadIntakeAndDuplicateTests.ReasonIdAsync(h, "unable_to_contact");
        await h.Leads.CloseAsync(leadId, dormant: false, new CloseLeadDto { ClosureReasonId = reasonId }, h.Sales);

        Assert.False(await h.Db.LeadFollowUps.AnyAsync(f => f.LeadId == leadId && f.Status == LeadFollowUpStatus.Pending));
        Assert.False(await h.Db.LeadSiteVisits.AnyAsync(
            v => v.LeadId == leadId && (v.Status == LeadSiteVisitStatus.Scheduled || v.Status == LeadSiteVisitStatus.Rescheduled)));
    }

    [Fact]
    public async Task ReopeningIsRecorded_AndRestrictedToAdminsAndManagers()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var reasonId = await LeadIntakeAndDuplicateTests.ReasonIdAsync(h, "delayed_decision");
        await h.Leads.CloseAsync(leadId, dormant: true, new CloseLeadDto { ClosureReasonId = reasonId }, h.Sales);

        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            h.Leads.ReopenAsync(leadId, new ReopenLeadDto { Reason = "They called back." }, h.Sales));

        var lead = await h.Leads.ReopenAsync(leadId,
            new ReopenLeadDto { Stage = LeadStage.Contacted, Reason = "They called back." }, h.Manager);

        Assert.Equal(LeadStage.Contacted, lead.Stage);
        Assert.Null(lead.ClosureReasonId);
        Assert.Null(lead.ClosedAt);

        var reopened = (await h.TimelineAsync(leadId)).Single(a => a.Type == LeadActivityType.LeadReopened);
        Assert.Contains("Dormant", reopened.PreviousValue);
        Assert.Equal("They called back.", reopened.Notes);
    }

    [Fact]
    public async Task ClosedLeadCannotBeEditedOrProgressedUntilReopened()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var reasonId = await LeadIntakeAndDuplicateTests.ReasonIdAsync(h, "not_interested");
        await h.Leads.CloseAsync(leadId, dormant: false, new CloseLeadDto { ClosureReasonId = reasonId }, h.Sales);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.ChangeStageAsync(leadId, new ChangeLeadStageDto { Stage = LeadStage.Qualified }, h.Sales));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.CloseAsync(leadId, dormant: false, new CloseLeadDto { ClosureReasonId = reasonId }, h.Sales));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Communications.RecordAsync(leadId, new RecordLeadCommunicationDto
            {
                Channel = LeadCommunicationChannel.Phone,
                Direction = LeadCommunicationDirection.Outbound,
                Summary = "Trying again"
            }, h.Sales));
    }

    // ── Access control ──────────────────────────────────────────────────────────

    [Fact]
    public async Task EmployeeSeesOnlyTheirOwnLeads()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var mine = await h.CreateLeadAsync();
        var theirs = await h.CreateLeadAsync(LeadTestHarness.Intake(firstName: "Zara", phone: "03219998888", email: "z@x.com"));

        await h.Leads.AssignAsync(mine, new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin);
        await h.Leads.AssignAsync(theirs, new AssignLeadDto { EmployeeId = h.OtherSalesEmployeeId }, h.Admin);

        var list = await h.Leads.GetLeadsAsync(new LeadFilterDto(), h.Sales);
        Assert.Single(list.Items);
        Assert.Equal(mine, list.Items[0].Id);

        Assert.Null(await h.Leads.GetByIdAsync(theirs, h.Sales));
        await Assert.ThrowsAsync<LeadNotFoundException>(() => h.Leads.GetTimelineAsync(theirs, h.Sales));
    }

    [Fact]
    public async Task MentioningAColleagueNotifiesButDoesNotShareLeadAcrossTeams()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateLeadAsync();
        await h.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin);

        Assert.Null(await h.Leads.GetByIdAsync(leadId, h.OtherSales));

        await Assert.ThrowsAsync<LeadAuthorizationException>(() => h.Communications.AddCommentAsync(leadId, new CreateLeadCommentDto
        {
            Body = "Omar, can you cover the viewing?",
            MentionedUserIds = { h.OtherSalesUserId }
        }, h.Sales));

        await h.Communications.AddCommentAsync(leadId, new CreateLeadCommentDto
        {
            Body = "Manager, please review.",
            MentionedUserIds = { h.ManagerUserId }
        }, h.Sales);

        Assert.Null(await h.Leads.GetByIdAsync(leadId, h.OtherSales));
        Assert.True(await h.Db.Notifications.AnyAsync(
            n => n.RecipientUserId == h.ManagerUserId && n.Type == NotificationType.UserMentioned));
    }

    [Fact]
    public async Task ManagerSeesTheirTeamAndTheUnassignedQueueButNotOtherTeams()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var teamLead = await h.CreateLeadAsync();
        var outsideLead = await h.CreateLeadAsync(LeadTestHarness.Intake(firstName: "Zara", phone: "03219998888", email: "z@x.com"));
        var unassigned = await h.CreateLeadAsync(LeadTestHarness.Intake(firstName: "Nadia", phone: "03337776666", email: "n@x.com"));

        await h.Leads.AssignAsync(teamLead, new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin);
        await h.Leads.AssignAsync(outsideLead, new AssignLeadDto { EmployeeId = h.OtherSalesEmployeeId }, h.Admin);

        var visible = await h.Leads.GetLeadsAsync(new LeadFilterDto { PageSize = 50 }, h.Manager);
        var ids = visible.Items.Select(i => i.Id).ToList();

        Assert.Contains(teamLead, ids);
        Assert.Contains(unassigned, ids);
        Assert.DoesNotContain(outsideLead, ids);
    }

    [Fact]
    public async Task ClientsHaveNoAccessToTheLeadWorkspace()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateLeadAsync();

        await Assert.ThrowsAsync<LeadAuthorizationException>(() => h.Leads.GetLeadsAsync(new LeadFilterDto(), h.Client));
        await Assert.ThrowsAsync<LeadAuthorizationException>(() => h.Leads.GetByIdAsync(leadId, h.Client));
    }

    [Fact]
    public async Task FilteringAndSearchWorkWithinScope()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        await h.CreateLeadAsync(LeadTestHarness.Intake(firstName: "Zara", phone: "03219998888", email: "z@x.com"));

        var byStage = await h.Leads.GetLeadsAsync(new LeadFilterDto { Stage = LeadStage.Contacted }, h.Admin);
        Assert.Single(byStage.Items);
        Assert.Equal(leadId, byStage.Items[0].Id);

        var byPhone = await h.Leads.GetLeadsAsync(new LeadFilterDto { SearchTerm = "0321-999-8888" }, h.Admin);
        Assert.Single(byPhone.Items);
        Assert.Equal("Zara", byPhone.Items[0].FirstName);

        var unassigned = await h.Leads.GetLeadsAsync(new LeadFilterDto { Unassigned = true }, h.Admin);
        Assert.Single(unassigned.Items);
    }
}
