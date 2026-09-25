using DAMS.Application.Common;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

public sealed class LeadEngagementTests
{
    // ── Communication ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RecordingAConversation_SetsFirstContact_AndMovesTheStage()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateLeadAsync();
        await h.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin);

        var communication = await h.Communications.RecordAsync(leadId, new RecordLeadCommunicationDto
        {
            Channel = LeadCommunicationChannel.Whatsapp,
            Direction = LeadCommunicationDirection.Outbound,
            Summary = "Shared the price plan.",
            CustomerResponse = "Will review tonight.",
            NextAction = "Call tomorrow",
            NextActionAt = DateTime.UtcNow.AddDays(1)
        }, h.Sales);

        Assert.Equal(LeadCommunicationChannel.Whatsapp, communication.Channel);

        var lead = await h.LoadLeadAsync(leadId);
        Assert.NotNull(lead.FirstContactAt);
        Assert.NotNull(lead.LastContactAt);
        Assert.Equal(LeadStage.Contacted, lead.Stage);
        Assert.Equal("Call tomorrow", lead.NextActionSummary);

        var activity = (await h.TimelineAsync(leadId)).Single(a => a.Type == LeadActivityType.WhatsappActivity);
        Assert.Equal(LeadCommunicationChannel.Whatsapp, activity.Channel);
        Assert.Equal(communication.Id, activity.CommunicationId);
    }

    [Fact]
    public async Task AFailedContactAttempt_IsRecordedButIsNotFirstContact()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateLeadAsync();
        await h.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin);

        await h.Communications.RecordAsync(leadId, new RecordLeadCommunicationDto
        {
            Channel = LeadCommunicationChannel.Phone,
            Direction = LeadCommunicationDirection.Outbound,
            Connected = false,
            Summary = "No answer."
        }, h.Sales);

        var lead = await h.LoadLeadAsync(leadId);
        Assert.Null(lead.FirstContactAt);
        Assert.Equal(LeadStage.FirstContactPending, lead.Stage);
        Assert.Contains(await h.TimelineAsync(leadId), a => a.Type == LeadActivityType.ContactAttempt);
    }

    [Fact]
    public async Task ReplayedProviderMessages_DoNotDuplicateHistory()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateLeadAsync();

        var dto = new RecordLeadCommunicationDto
        {
            Channel = LeadCommunicationChannel.Whatsapp,
            Direction = LeadCommunicationDirection.Inbound,
            Summary = "Is the corner unit available?",
            ExternalProvider = "whatsapp",
            ExternalMessageId = "wamid.123"
        };

        var first = await h.Communications.RecordAsync(leadId, dto, h.Admin);
        var replay = await h.Communications.RecordAsync(leadId, dto, h.Admin);

        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(1, await h.Db.LeadCommunications.CountAsync(c => c.LeadId == leadId));
    }

    [Fact]
    public async Task FutureDatedCommunicationIsRejected()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateLeadAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Communications.RecordAsync(leadId,
            new RecordLeadCommunicationDto
            {
                Channel = LeadCommunicationChannel.Phone,
                Direction = LeadCommunicationDirection.Outbound,
                Summary = "Tomorrow's call",
                OccurredAt = DateTime.UtcNow.AddDays(1)
            }, h.Admin));
    }

    [Fact]
    public async Task OnlyStaffCanBeMentioned()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateLeadAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Communications.AddCommentAsync(leadId, new CreateLeadCommentDto
            {
                Body = "Passing to the customer",
                MentionedUserIds = { h.ClientUserId }
            }, h.Admin));

        Assert.Contains("colleagues", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ManagerReviewRequest_AlertsSupervisors()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateLeadAsync();
        await h.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin);

        await h.Communications.AddCommentAsync(leadId, new CreateLeadCommentDto
        {
            Body = "Customer wants 12% off — need approval.",
            IsManagerReviewRequest = true
        }, h.Sales);

        Assert.True(await h.Db.Notifications.AnyAsync(
            n => n.EntityType == NotificationEntityType.Lead && n.EntityId == leadId && n.Type == NotificationType.ManagerAttentionRequired));

        var comments = await h.Communications.GetCommentsAsync(leadId, h.Manager);
        Assert.Single(comments);
        Assert.True(comments[0].IsManagerReviewRequest);
    }

    // ── Follow-ups ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SchedulingAFollowUp_SetsTheLeadNextAction()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var due = DateTime.UtcNow.AddDays(2);

        var followUp = await h.FollowUps.CreateAsync(leadId, new CreateLeadFollowUpDto
        {
            Type = LeadFollowUpType.Call,
            Title = "Confirm payment plan",
            DueAt = due,
            Priority = TaskPriority.High
        }, h.Sales);

        Assert.Equal(LeadFollowUpStatus.Pending, followUp.Status);
        Assert.Equal(h.SalesEmployeeId, followUp.AssignedEmployeeId);

        var lead = await h.LoadLeadAsync(leadId);
        Assert.Equal(due, lead.NextActionAt);
        Assert.Equal("Confirm payment plan", lead.NextActionSummary);
        Assert.Contains(await h.TimelineAsync(leadId), a => a.Type == LeadActivityType.FollowUpScheduled);
    }

    [Fact]
    public async Task PastDatedFollowUpIsRejected()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.FollowUps.CreateAsync(leadId,
            new CreateLeadFollowUpDto { Title = "Late", DueAt = DateTime.UtcNow.AddDays(-1) }, h.Sales));
    }

    [Fact]
    public async Task CompletingAFollowUp_RecordsTheOutcomeAndCanChainTheNextOne()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var followUp = await h.FollowUps.CreateAsync(leadId, new CreateLeadFollowUpDto
        {
            Title = "Confirm payment plan",
            DueAt = DateTime.UtcNow.AddDays(1)
        }, h.Sales);

        var completed = await h.FollowUps.CompleteAsync(followUp.Id, new CompleteLeadFollowUpDto
        {
            Outcome = "Happy with the 3-year plan.",
            NextFollowUpAt = DateTime.UtcNow.AddDays(5),
            NextFollowUpTitle = "Collect documents"
        }, h.Sales);

        Assert.Equal(LeadFollowUpStatus.Completed, completed.Status);
        Assert.Equal("Happy with the 3-year plan.", completed.Outcome);

        var open = await h.Db.LeadFollowUps.Where(f => f.LeadId == leadId && f.Status == LeadFollowUpStatus.Pending).ToListAsync();
        Assert.Single(open);
        Assert.Equal("Collect documents", open[0].Title);

        // The lead's next action follows the remaining open follow-up.
        var lead = await h.LoadLeadAsync(leadId);
        Assert.Equal("Collect documents", lead.NextActionSummary);

        var timeline = await h.TimelineAsync(leadId);
        Assert.Contains(timeline, a => a.Type == LeadActivityType.FollowUpCompleted);
    }

    [Fact]
    public async Task EmployeeCannotHandFollowUpsToUnrelatedColleagues()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();

        await Assert.ThrowsAsync<LeadAuthorizationException>(() => h.FollowUps.CreateAsync(leadId,
            new CreateLeadFollowUpDto
            {
                Title = "You do it",
                DueAt = DateTime.UtcNow.AddDays(1),
                AssignedEmployeeId = h.OtherSalesEmployeeId
            }, h.Sales));

        await Assert.ThrowsAsync<LeadAuthorizationException>(() => h.FollowUps.CreateAsync(leadId,
            new CreateLeadFollowUpDto
            {
                Title = "Manager handoff",
                DueAt = DateTime.UtcNow.AddDays(1),
                AssignedEmployeeId = h.OtherSalesEmployeeId
            }, h.Manager));

        // Admin can make an explicit cross-team work assignment, and the assignee can act on
        // that work item without receiving full lead access.
        var task = await h.FollowUps.CreateAsync(leadId, new CreateLeadFollowUpDto
        {
            Title = "Admin-approved cover",
            DueAt = DateTime.UtcNow.AddDays(1),
            AssignedEmployeeId = h.OtherSalesEmployeeId
        }, h.Admin);

        Assert.Equal(h.OtherSalesEmployeeId, task.AssignedEmployeeId);
        await h.FollowUps.CompleteAsync(task.Id, new CompleteLeadFollowUpDto { Outcome = "Covered." }, h.OtherSales);
        Assert.Null(await h.Leads.GetByIdAsync(leadId, h.OtherSales));
    }

    [Fact]
    public async Task ATaskOnALeadDoesNotGrantTheAssigneeFullLeadAccess()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();

        Assert.Null(await h.Leads.GetByIdAsync(leadId, h.OtherSales));

        await h.FollowUps.CreateAsync(leadId, new CreateLeadFollowUpDto
        {
            Title = "Cover the viewing",
            DueAt = DateTime.UtcNow.AddDays(1),
            AssignedEmployeeId = h.OtherSalesEmployeeId
        }, h.Admin);

        Assert.Null(await h.Leads.GetByIdAsync(leadId, h.OtherSales));
    }

    // ── Site visits ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SchedulingASiteVisit_MovesTheStageAndCapturesTheDetails()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var when = DateTime.UtcNow.AddDays(3);

        var visit = await h.SiteVisits.ScheduleAsync(leadId, new ScheduleSiteVisitDto
        {
            ScheduledAt = when,
            MeetingLocation = "Floria Heights site office",
            ProjectId = h.ProjectId,
            UnitId = h.UnitId,
            CustomerAttendees = "Bilal + spouse",
            InternalAttendees = "Sana"
        }, h.Sales);

        Assert.Equal(LeadSiteVisitStatus.Scheduled, visit.Status);
        Assert.Equal(h.ProjectId, visit.ProjectId);
        Assert.Equal(h.SalesEmployeeId, visit.AssignedEmployeeId);

        var lead = await h.LoadLeadAsync(leadId);
        Assert.Equal(LeadStage.SiteVisitScheduled, lead.Stage);
        Assert.Equal(when, lead.NextActionAt);
        Assert.Contains(await h.TimelineAsync(leadId), a => a.Type == LeadActivityType.SiteVisitScheduled);
    }

    [Fact]
    public async Task ReschedulingKeepsTheOriginalTimeAndCountsTheMove()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var original = DateTime.UtcNow.AddDays(3);
        var visit = await h.SiteVisits.ScheduleAsync(leadId, new ScheduleSiteVisitDto
        {
            ScheduledAt = original,
            MeetingLocation = "Site office"
        }, h.Sales);

        var moved = await h.SiteVisits.RescheduleAsync(visit.Id, new RescheduleSiteVisitDto
        {
            ScheduledAt = DateTime.UtcNow.AddDays(5),
            Reason = "Customer travelling."
        }, h.Sales);

        Assert.Equal(LeadSiteVisitStatus.Rescheduled, moved.Status);
        Assert.Equal(original, moved.OriginalScheduledAt);
        Assert.Equal(1, moved.RescheduleCount);
        Assert.Contains(await h.TimelineAsync(leadId), a => a.Type == LeadActivityType.SiteVisitRescheduled);
    }

    [Fact]
    public async Task CompletingAVisitRecordsTheOutcome_AndCountsAsContact()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var visit = await h.SiteVisits.ScheduleAsync(leadId, new ScheduleSiteVisitDto
        {
            ScheduledAt = DateTime.UtcNow.AddDays(1),
            MeetingLocation = "Site office"
        }, h.Sales);

        var completed = await h.SiteVisits.CompleteAsync(visit.Id, new CompleteSiteVisitDto
        {
            Outcome = LeadSiteVisitOutcome.ReadyToBook,
            NextAction = "Prepare the application form",
            CustomerFeedback = "Loved the view."
        }, h.Sales);

        Assert.Equal(LeadSiteVisitStatus.Completed, completed.Status);
        Assert.Equal(LeadSiteVisitOutcome.ReadyToBook, completed.Outcome);
        Assert.Equal("Prepare the application form", completed.NextAction);

        var lead = await h.LoadLeadAsync(leadId);
        Assert.Equal(LeadStage.SiteVisitCompleted, lead.Stage);
        Assert.NotNull(lead.LastContactAt);

        // Only now may the lead be moved on from a completed visit.
        await h.Leads.ChangeStageAsync(leadId, new ChangeLeadStageDto { Stage = LeadStage.Negotiation }, h.Sales);
    }

    // ── Next action ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CompletingTheOnlyVisit_ClearsTheNextActionImmediately()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var visit = await h.SiteVisits.ScheduleAsync(leadId, new ScheduleSiteVisitDto
        {
            ScheduledAt = DateTime.UtcNow.AddDays(1),
            MeetingLocation = "Site office"
        }, h.Sales);

        await h.SiteVisits.CompleteAsync(visit.Id, new CompleteSiteVisitDto
        {
            Outcome = LeadSiteVisitOutcome.Interested,
            NextAction = "Follow up"
        }, h.Sales);

        var lead = await h.LoadLeadAsync(leadId);
        Assert.Null(lead.NextActionAt);
        Assert.Null(lead.NextActionSummary);
    }

    [Fact]
    public async Task CompletingAVisit_HandsTheNextActionToTheEarliestRemainingWork()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var followUpDue = DateTime.UtcNow.AddDays(4);
        var visit = await h.SiteVisits.ScheduleAsync(leadId, new ScheduleSiteVisitDto
        {
            ScheduledAt = DateTime.UtcNow.AddDays(1),
            MeetingLocation = "Site office"
        }, h.Sales);
        await h.FollowUps.CreateAsync(leadId, new CreateLeadFollowUpDto
        {
            Title = "Send brochure",
            DueAt = followUpDue
        }, h.Sales);

        await h.SiteVisits.CompleteAsync(visit.Id, new CompleteSiteVisitDto
        {
            Outcome = LeadSiteVisitOutcome.Interested,
            NextAction = "Follow up"
        }, h.Sales);

        var lead = await h.LoadLeadAsync(leadId);
        Assert.Equal(followUpDue, lead.NextActionAt);
        Assert.Equal("Send brochure", lead.NextActionSummary);
    }

    [Fact]
    public async Task ReschedulingAVisitLater_DoesNotHideAnEarlierOutstandingFollowUp()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var followUpDue = DateTime.UtcNow.AddDays(3);
        var visit = await h.SiteVisits.ScheduleAsync(leadId, new ScheduleSiteVisitDto
        {
            ScheduledAt = DateTime.UtcNow.AddDays(1),
            MeetingLocation = "Site office"
        }, h.Sales);
        await h.FollowUps.CreateAsync(leadId, new CreateLeadFollowUpDto
        {
            Title = "Send brochure",
            DueAt = followUpDue
        }, h.Sales);

        await h.SiteVisits.RescheduleAsync(visit.Id, new RescheduleSiteVisitDto
        {
            ScheduledAt = DateTime.UtcNow.AddDays(6),
            Reason = "Customer travelling."
        }, h.Sales);

        var lead = await h.LoadLeadAsync(leadId);
        Assert.Equal(followUpDue, lead.NextActionAt);
        Assert.Equal("Send brochure", lead.NextActionSummary);
    }

    [Fact]
    public async Task ALaterCommunication_SupersedesTheEarlierOnesNextAction()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        await h.Communications.RecordAsync(leadId, new RecordLeadCommunicationDto
        {
            Channel = LeadCommunicationChannel.Phone,
            Direction = LeadCommunicationDirection.Outbound,
            Summary = "Discussed pricing.",
            NextAction = "Call tomorrow",
            NextActionAt = DateTime.UtcNow.AddDays(1)
        }, h.Sales);

        var rescheduledTo = DateTime.UtcNow.AddDays(5);
        await h.Communications.RecordAsync(leadId, new RecordLeadCommunicationDto
        {
            Channel = LeadCommunicationChannel.Whatsapp,
            Direction = LeadCommunicationDirection.Inbound,
            Summary = "Customer asked to talk next week.",
            NextAction = "Call next week",
            NextActionAt = rescheduledTo
        }, h.Sales);

        var lead = await h.LoadLeadAsync(leadId);
        Assert.Equal(rescheduledTo, lead.NextActionAt);
        Assert.Equal("Call next week", lead.NextActionSummary);

        // A later exchange with no plan means the earlier plan was carried out.
        await h.Communications.RecordAsync(leadId, new RecordLeadCommunicationDto
        {
            Channel = LeadCommunicationChannel.Phone,
            Direction = LeadCommunicationDirection.Outbound,
            Summary = "Called back as promised."
        }, h.Sales);

        lead = await h.LoadLeadAsync(leadId);
        Assert.Null(lead.NextActionAt);
        Assert.Null(lead.NextActionSummary);
    }

    [Fact]
    public async Task MixedWork_NextActionTracksTheEarliestOutstandingItem()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var callAt = DateTime.UtcNow.AddDays(2);
        var visitAt = DateTime.UtcNow.AddDays(4);
        var followUpDue = DateTime.UtcNow.AddDays(1);

        await h.Communications.RecordAsync(leadId, new RecordLeadCommunicationDto
        {
            Channel = LeadCommunicationChannel.Phone,
            Direction = LeadCommunicationDirection.Outbound,
            Summary = "Intro call.",
            NextAction = "Share floor plans",
            NextActionAt = callAt
        }, h.Sales);
        var visit = await h.SiteVisits.ScheduleAsync(leadId, new ScheduleSiteVisitDto
        {
            ScheduledAt = visitAt,
            MeetingLocation = "Site office"
        }, h.Sales);
        var followUp = await h.FollowUps.CreateAsync(leadId, new CreateLeadFollowUpDto
        {
            Title = "Confirm budget",
            DueAt = followUpDue
        }, h.Sales);

        Assert.Equal(followUpDue, (await h.LoadLeadAsync(leadId)).NextActionAt);

        await h.FollowUps.CompleteAsync(followUp.Id, new CompleteLeadFollowUpDto { Outcome = "Budget confirmed." }, h.Sales);
        Assert.Equal(callAt, (await h.LoadLeadAsync(leadId)).NextActionAt);

        await h.Communications.RecordAsync(leadId, new RecordLeadCommunicationDto
        {
            Channel = LeadCommunicationChannel.Email,
            Direction = LeadCommunicationDirection.Outbound,
            Summary = "Sent the floor plans."
        }, h.Sales);
        Assert.Equal(visitAt, (await h.LoadLeadAsync(leadId)).NextActionAt);

        await h.SiteVisits.CompleteAsync(visit.Id, new CompleteSiteVisitDto
        {
            Outcome = LeadSiteVisitOutcome.Interested,
            NextAction = "Follow up"
        }, h.Sales);
        Assert.Null((await h.LoadLeadAsync(leadId)).NextActionAt);
    }

    [Fact]
    public async Task ACompletedVisitCannotBeCompletedOrCancelledAgain()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var visit = await h.SiteVisits.ScheduleAsync(leadId, new ScheduleSiteVisitDto
        {
            ScheduledAt = DateTime.UtcNow.AddDays(1),
            MeetingLocation = "Site office"
        }, h.Sales);

        await h.SiteVisits.CompleteAsync(visit.Id, new CompleteSiteVisitDto
        {
            Outcome = LeadSiteVisitOutcome.Interested,
            NextAction = "Follow up"
        }, h.Sales);

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.SiteVisits.CompleteAsync(visit.Id,
            new CompleteSiteVisitDto { Outcome = LeadSiteVisitOutcome.Interested, NextAction = "Again" }, h.Sales));

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.SiteVisits.CancelAsync(visit.Id,
            new CloseSiteVisitDto { Reason = "Changed my mind" }, h.Sales));
    }

    [Fact]
    public async Task AMissedVisitEscalatesToSupervisors()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var visit = await h.SiteVisits.ScheduleAsync(leadId, new ScheduleSiteVisitDto
        {
            ScheduledAt = DateTime.UtcNow.AddDays(1),
            MeetingLocation = "Site office"
        }, h.Sales);

        await h.SiteVisits.MarkMissedAsync(visit.Id, new CloseSiteVisitDto { Reason = "Customer did not turn up." }, h.Sales);

        Assert.True(await h.Db.Notifications.AnyAsync(
            n => n.EntityType == NotificationEntityType.Lead && n.EntityId == leadId && n.Type == NotificationType.SiteVisitMissed && n.IsEscalation));
        Assert.Contains(await h.TimelineAsync(leadId), a => a.Type == LeadActivityType.SiteVisitMissed);
    }

    [Fact]
    public async Task UpcomingVisitsAreScopedToWhatTheCallerMaySee()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var mine = await h.CreateWorkedLeadAsync();
        var theirs = await h.CreateWorkedLeadAsync("03219998888");
        await h.Leads.AssignAsync(theirs,
            new AssignLeadDto { EmployeeId = h.OtherSalesEmployeeId, Reason = "handover" }, h.Admin);

        await h.SiteVisits.ScheduleAsync(mine, new ScheduleSiteVisitDto
        {
            ScheduledAt = DateTime.UtcNow.AddDays(1), MeetingLocation = "Site office"
        }, h.Sales);
        await h.SiteVisits.ScheduleAsync(theirs, new ScheduleSiteVisitDto
        {
            ScheduledAt = DateTime.UtcNow.AddDays(2), MeetingLocation = "Site office"
        }, h.OtherSales);

        var visible = await h.SiteVisits.GetUpcomingAsync(h.Sales, 30);
        Assert.Single(visible);
        Assert.Equal(mine, visible[0].LeadId);

        Assert.Equal(2, (await h.SiteVisits.GetUpcomingAsync(h.Admin, 30)).Count);
    }

    // ── Documents ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task UploadingADocument_StoresItPrivatelyAndRecordsWhoAddedIt()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();

        var document = await h.Documents.UploadAsync(leadId, LeadTestHarness.Pdf(),
            LeadDocumentCategory.Quotation, "Initial quote", null, h.Sales);

        Assert.Equal("quotation.pdf", document.FileName);
        Assert.Equal(h.SalesUserId, document.UploadedByUserId);
        Assert.Single(h.DocumentStorage.Files);
        Assert.Contains(await h.TimelineAsync(leadId), a => a.Type == LeadActivityType.DocumentUploaded);

        var download = await h.Documents.DownloadAsync(document.Id, h.Sales);
        await using (download.Content) Assert.True(download.Content.Length > 0);
    }

    [Fact]
    public async Task DocumentsInheritTheLeadAccessRules()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var document = await h.Documents.UploadAsync(leadId, LeadTestHarness.Pdf(),
            LeadDocumentCategory.IdentityDocument, null, null, h.Sales);

        await Assert.ThrowsAsync<LeadNotFoundException>(() => h.Documents.DownloadAsync(document.Id, h.OtherSales));
        await Assert.ThrowsAsync<LeadNotFoundException>(() => h.Documents.GetForLeadAsync(leadId, h.OtherSales));
        // The manager who owns the team may read it.
        var managerCopy = await h.Documents.DownloadAsync(document.Id, h.Manager);
        await using (managerCopy.Content) Assert.True(managerCopy.Content.Length > 0);
    }

    [Fact]
    public async Task OnlyTheUploaderOrASupervisorCanRemoveADocument()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var document = await h.Documents.UploadAsync(leadId, LeadTestHarness.Pdf(),
            LeadDocumentCategory.Quotation, null, null, h.Manager);

        await Assert.ThrowsAsync<LeadAuthorizationException>(() => h.Documents.DeleteAsync(document.Id, h.Sales));

        await h.Documents.DeleteAsync(document.Id, h.Manager);
        Assert.Empty(h.DocumentStorage.Files);
        Assert.Contains(await h.TimelineAsync(leadId), a => a.Type == LeadActivityType.DocumentRemoved);
    }

    [Fact]
    public async Task AFailedUploadLeavesNoRowAndNoFile()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        h.DocumentStorage.FailSaves = true;

        await Assert.ThrowsAsync<IOException>(() => h.Documents.UploadAsync(leadId, LeadTestHarness.Pdf(),
            LeadDocumentCategory.Quotation, null, null, h.Sales));

        Assert.Empty(h.DocumentStorage.Files);
        Assert.False(await h.Db.LeadDocuments.AnyAsync(d => d.LeadId == leadId));
    }

    [Fact]
    public async Task DisguisedFilesAreRejectedBeforeStorage()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();

        var bytes = System.Text.Encoding.UTF8.GetBytes("MZ this is an executable");
        var upload = new LeadDocumentUpload
        {
            Content = new MemoryStream(bytes),
            FileName = "payload.pdf",
            Length = bytes.Length
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Documents.UploadAsync(
            leadId, upload, LeadDocumentCategory.Other, null, null, h.Sales));

        Assert.Empty(h.DocumentStorage.Files);
    }
}
