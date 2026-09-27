using DAMS.Application.DTOs.LeadDtos;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

public sealed class LeadEditAndTimelineTests
{
    [Fact]
    public async Task EditingALeadRecordsWhatChanged()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();

        var updated = await h.Leads.UpdateAsync(leadId,
            Update(await h.ConcurrencyTokenAsync(leadId), email: "new@example.com"), h.Sales);

        Assert.Equal("new@example.com", updated.Email);
        Assert.Equal(500_000m, updated.BudgetMin);
        var activity = (await h.TimelineAsync(leadId)).Last(a => a.Type == LeadActivityType.DetailsUpdated);
        Assert.Contains("email", activity.NewValue);
    }

    [Fact]
    public async Task EditingCannotIntroduceADuplicateOpenLead()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var first = await h.CreateWorkedLeadAsync();
        var second = await h.CreateWorkedLeadAsync("03219998888");
        var token = await h.ConcurrencyTokenAsync(second);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.UpdateAsync(second, Update(token, phone: "0300-1234567"), h.Admin));

        Assert.Contains("Another open lead", error.Message);

        // The rejected edit must not have been half-applied.
        h.Db.ChangeTracker.Clear();
        var untouched = await h.LoadLeadAsync(second);
        Assert.Equal("3219998888", untouched.NormalizedPhone);
        Assert.Null(untouched.BudgetMin);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task AnImpossibleBudgetRangeIsRejectedBeforeAnythingChanges()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();

        var dto = Update(await h.ConcurrencyTokenAsync(leadId));
        dto.BudgetMin = 9_000_000m;
        dto.BudgetMax = 1_000_000m;

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Leads.UpdateAsync(leadId, dto, h.Sales));

        h.Db.ChangeTracker.Clear();
        var untouched = await h.LoadLeadAsync(leadId);
        Assert.Null(untouched.BudgetMin);
    }

    [Fact]
    public async Task TheTimelineIsAppendOnlyAndReadsNewestFirst()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var afterCreate = (await h.Leads.GetTimelineAsync(leadId, h.Admin)).Count;

        await h.Communications.RecordAsync(leadId, new RecordLeadCommunicationDto
        {
            Channel = LeadCommunicationChannel.Phone,
            Direction = LeadCommunicationDirection.Outbound,
            Summary = "Checked in again."
        }, h.Sales);

        var timeline = await h.Leads.GetTimelineAsync(leadId, h.Admin);
        Assert.True(timeline.Count > afterCreate);
        // Nothing that existed before was replaced.
        Assert.Contains(timeline, a => a.Type == LeadActivityType.LeadCreated);
        Assert.Contains(timeline, a => a.Type == LeadActivityType.LeadAssigned);
        Assert.True(timeline[0].OccurredAt >= timeline[^1].OccurredAt);

        // Every entry names who did it and when.
        Assert.All(timeline.Where(a => !a.IsSystemGenerated), a =>
        {
            Assert.NotNull(a.PerformedByUserId);
            Assert.NotEqual(default, a.OccurredAt);
        });
    }

    [Fact]
    public async Task TheLeadSummaryTracksItsLatestActivity()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();

        await h.Leads.ChangeStageAsync(leadId, new ChangeLeadStageDto { Stage = LeadStage.Qualified }, h.Sales);

        var lead = await h.Leads.GetByIdAsync(leadId, h.Admin);
        Assert.NotNull(lead!.LastActivityAt);
        Assert.Contains("Qualified", lead.LastActivitySummary);
    }

    [Fact]
    public async Task LongNotesAreClampedRatherThanFailingTheSave()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();

        await h.Leads.ChangeStageAsync(leadId, new ChangeLeadStageDto
        {
            Stage = LeadStage.Qualified,
            Notes = new string('x', 5000)
        }, h.Sales);

        var activity = (await h.TimelineAsync(leadId)).Last(a => a.Type == LeadActivityType.StageChanged);
        Assert.Equal(2000, activity.Notes!.Length);
    }

    [Fact]
    public async Task LeadsAreLinkedToTheirWebsiteRequestInBothDirections()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var request = await h.BookingRequests.CreateBookingRequestAsync(
            new DTOs.BookingRequestDtos.CreateBookingRequestDto
            {
                UnitId = h.UnitId,
                FullName = "Aiman Raza",
                Phone = "03001234567",
                Email = "aiman@example.com",
                CNIC = "35202-9876543-2",
                Address = "12 Model Town, Lahore"
            }, h.ClientUserId);

        var lead = await h.Leads.GetByIdAsync(request.LeadId!.Value, h.Admin);

        Assert.Equal(request.Id, lead!.BookingRequestId);
        Assert.Equal("website", lead.SourceCode);
    }

    [Fact]
    public async Task TheLeadPage_ReturnsTabCountsAndHeader_AndTheListDoesNot()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        await h.Communications.RecordAsync(leadId, new RecordLeadCommunicationDto
        {
            Channel = LeadCommunicationChannel.Email,
            Direction = LeadCommunicationDirection.Outbound,
            Summary = "Sent the brochure."
        }, h.Sales);
        await h.FollowUps.CreateAsync(leadId, new CreateLeadFollowUpDto
        {
            Title = "Send brochure",
            DueAt = DateTime.UtcNow.AddDays(2)
        }, h.Sales);
        var cancelledFollowUp = await h.FollowUps.CreateAsync(leadId, new CreateLeadFollowUpDto
        {
            Title = "Drop this",
            DueAt = DateTime.UtcNow.AddDays(3)
        }, h.Sales);
        await h.FollowUps.CancelAsync(cancelledFollowUp.Id, "No longer needed", h.Sales);
        await h.SiteVisits.ScheduleAsync(leadId, new ScheduleSiteVisitDto
        {
            ScheduledAt = DateTime.UtcNow.AddDays(1),
            MeetingLocation = "Site office"
        }, h.Sales);
        var cancelledVisit = await h.SiteVisits.ScheduleAsync(leadId, new ScheduleSiteVisitDto
        {
            ScheduledAt = DateTime.UtcNow.AddDays(4),
            MeetingLocation = "Other office"
        }, h.Sales);
        await h.SiteVisits.CancelAsync(cancelledVisit.Id, new CloseSiteVisitDto { Reason = "Customer cancelled." }, h.Sales);

        var detail = Assert.IsType<LeadDetailResponseDto>(await h.Leads.GetByIdAsync(leadId, h.Admin));
        var timeline = await h.Leads.GetTimelineAsync(leadId, h.Admin);
        Assert.Equal(timeline.Count, detail.Counts.Timeline);
        Assert.Equal(2, detail.Counts.Communications);
        Assert.Equal(1, detail.Counts.FollowUps);
        Assert.Equal(1, detail.Counts.SiteVisits);

        var pending = (await h.FollowUps.GetForLeadAsync(leadId, h.Sales))
            .Single(f => f.Status == LeadFollowUpStatus.Pending);
        await h.FollowUps.CompleteAsync(pending.Id, new CompleteLeadFollowUpDto { Outcome = "Done." }, h.Sales);
        detail = Assert.IsType<LeadDetailResponseDto>(await h.Leads.GetByIdAsync(leadId, h.Admin));
        Assert.Equal(1, detail.Counts.FollowUps);
        Assert.Equal(LeadCommunicationChannel.Email, detail.LastCommunication!.Channel);
        Assert.Equal("Sent the brochure.", detail.LastCommunication.Summary);
        Assert.True(detail.LastCommunication.Connected);
        Assert.Equal("Sana Sales", detail.LastCommunication.EmployeeName);
        Assert.Null(detail.ClosedByName);
        Assert.Null(detail.ConvertedByName);
        Assert.Null(detail.ConvertedUnitNumber);

        var listed = (await h.Leads.GetLeadsAsync(new LeadFilterDto(), h.Admin)).Items.Single(l => l.Id == leadId);
        Assert.IsNotType<LeadDetailResponseDto>(listed);

        var reasonId = await LeadIntakeAndDuplicateTests.ReasonIdAsync(h, "not_interested");
        await h.Leads.CloseAsync(leadId, dormant: false, new CloseLeadDto { ClosureReasonId = reasonId }, h.Sales);
        detail = Assert.IsType<LeadDetailResponseDto>(await h.Leads.GetByIdAsync(leadId, h.Admin));
        Assert.Equal("Sana Sales", detail.ClosedByName);

        var wonId = await h.CreateWorkedLeadAsync("0300-8889900");
        await h.Leads.ConvertAsync(wonId, new ConvertLeadDto { UnitId = h.UnitId }, h.Admin);
        var won = Assert.IsType<LeadDetailResponseDto>(await h.Leads.GetByIdAsync(wonId, h.Admin));
        Assert.Equal("Ayesha Admin", won.ConvertedByName);
        Assert.Equal("A-101", won.ConvertedUnitNumber);
    }

    [Fact]
    public async Task TimelineOmitsDetailedStageEntries_AndReopenUsesTheSimpleStatus()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        await h.SiteVisits.ScheduleAsync(leadId, new ScheduleSiteVisitDto
        {
            ScheduledAt = DateTime.UtcNow.AddDays(1),
            MeetingLocation = "Site office"
        }, h.Sales);
        await h.Leads.UpdateQualificationAsync(leadId,
            new UpdateLeadQualificationDto { Qualification = LeadQualification.Hot }, h.Sales);
        var reasonId = await LeadIntakeAndDuplicateTests.ReasonIdAsync(h, "not_interested");
        await h.Leads.CloseAsync(leadId, dormant: false, new CloseLeadDto { ClosureReasonId = reasonId }, h.Sales);
        await h.Leads.ReopenAsync(leadId, new ReopenLeadDto { Stage = LeadStage.Contacted, Reason = "Called back." }, h.Sales);

        Assert.Contains(await h.Db.LeadActivities.Where(a => a.LeadId == leadId).ToListAsync(),
            a => a.Type == LeadActivityType.StageChanged);

        var timeline = await h.Leads.GetTimelineAsync(leadId, h.Admin);
        Assert.DoesNotContain(timeline, a => a.Type is LeadActivityType.StageChanged
            or LeadActivityType.QualificationChanged
            or LeadActivityType.NegotiationUpdate);
        Assert.Equal(timeline.Count,
            (Assert.IsType<LeadDetailResponseDto>(await h.Leads.GetByIdAsync(leadId, h.Admin))).Counts.Timeline);

        var reopen = Assert.Single(timeline, a => a.Type == LeadActivityType.LeadReopened);
        Assert.Equal("Lead reopened (was Lost).", reopen.Summary);

        string[] detailed =
        [
            "Contacted", "Qualified", "Negotiation", "FirstContactPending", "First contact pending",
            "SiteVisitScheduled", "SiteVisitCompleted", "Site visit completed",
            "DocumentsInProgress", "Documents in progress", "BookingPending", "Booking pending",
            "Stage changed", "Stage moved", "Qualification changed"
        ];
        foreach (var entry in timeline)
        {
            var text = $"{entry.Summary} {entry.Notes} {entry.PreviousValue} {entry.NewValue}";
            Assert.DoesNotContain(detailed, name => text.Contains(name, StringComparison.Ordinal));
        }
    }

    private static UpdateLeadDto Update(string token, string phone = "0300-1234567", string? email = "bilal@example.com") => new()
    {
        ConcurrencyToken = token,
        FirstName = "Bilal",
        LastName = "Khan",
        Phone = phone,
        Email = email,
        BudgetMin = 500_000m,
        BudgetMax = 9_000_000m,
        PurchaseIntent = LeadPurchaseIntent.Investment
    };
}
