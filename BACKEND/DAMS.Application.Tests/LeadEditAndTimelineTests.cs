using System.Reflection;
using DAMS.Application.Common;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
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
        Assert.IsNotType<LeadDetailResponseDto>(updated);
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
        Assert.IsType<LeadListItemDto>(listed);
        foreach (var property in typeof(LeadListItemDto).GetProperties(BindingFlags.Instance | BindingFlags.Public))
            Assert.Equal(property.GetValue(listed), property.GetValue(detail));

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

    [Fact]
    public async Task KAN48_TheTimelineComesFiftyAtATimeNewestFirst_AndEachOlderPageStartsWhereTheLastEnded()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var start = DateTime.UtcNow.AddDays(-10);

        // Written out of time order and often sharing a time, with some of the detailed-stage
        // rows the timeline leaves out among them.
        for (var i = 0; i < 130; i++)
        {
            h.Db.LeadActivities.Add(new LeadActivity
            {
                LeadId = leadId,
                Type = i % 10 == 0 ? LeadActivityType.StageChanged : LeadActivityType.InternalNote,
                Summary = $"Entry {i}",
                IsSystemGenerated = true,
                OccurredAt = start.AddMinutes(i * 37 % 60)
            });
        }
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        // Written last, dated when the call took place.
        await h.Communications.RecordAsync(leadId, new RecordLeadCommunicationDto
        {
            Channel = LeadCommunicationChannel.Phone,
            Direction = LeadCommunicationDirection.Outbound,
            OccurredAt = start.AddMinutes(30),
            Summary = "Logged ten days late."
        }, h.Sales);

        var expected = await h.Db.LeadActivities.AsNoTracking()
            .Where(a => a.LeadId == leadId && !LeadTimeline.NotShownOnTimeline.Contains(a.Type))
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .Select(a => a.Id)
            .ToListAsync();
        Assert.True(expected.Count > 100);

        var first = await h.Leads.GetTimelineAsync(leadId, h.Admin);
        Assert.Equal(expected.Take(50), first.Select(a => a.Id));
        var second = await h.Leads.GetTimelineAsync(leadId, h.Admin, before: first[^1].Id);
        Assert.Equal(expected.Skip(50).Take(50), second.Select(a => a.Id));

        // Small pages walk the whole timeline without skipping or repeating an entry.
        var walked = new List<int>();
        int? before = null;
        while (true)
        {
            var page = await h.Leads.GetTimelineAsync(leadId, h.Admin, take: 7, before: before);
            Assert.InRange(page.Count, 0, 7);
            if (page.Count == 0)
                break;
            walked.AddRange(page.Select(a => a.Id));
            before = page[^1].Id;
        }
        Assert.Equal(expected, walked);

        Assert.Equal(100, (await h.Leads.GetTimelineAsync(leadId, h.Admin, take: 500)).Count);
        Assert.Single(await h.Leads.GetTimelineAsync(leadId, h.Admin, take: 0));

        // A position on another lead's timeline, or on none, is refused rather than guessed at.
        var otherLead = await h.CreateWorkedLeadAsync("0300-7654321");
        var otherEntry = (await h.Leads.GetTimelineAsync(otherLead, h.Admin))[0].Id;
        await Assert.ThrowsAsync<LeadNotFoundException>(() => h.Leads.GetTimelineAsync(leadId, h.Admin, before: otherEntry));
        await Assert.ThrowsAsync<LeadNotFoundException>(() => h.Leads.GetTimelineAsync(leadId, h.Admin, before: int.MaxValue));
    }

    [Fact]
    public void LeadProjectionsTranslateOnSqlServer_AndTheListRowReadsNoSubSelectsOrLongText()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost;Database=dams_lead_mapping_shape;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False")
            .Options;
        using var db = new AppDbContext(options);

        var listSql = db.Leads.Select(LeadMapping.ToListItem).ToQueryString();
        var responseSql = db.Leads.Select(LeadMapping.ToResponse(db)).ToQueryString();
        var detailSql = db.Leads.Select(LeadMapping.ToDetail(db)).ToQueryString();

        // Shared lead columns stay on every query.
        foreach (var sql in new[] { listSql, responseSql, detailSql })
        {
            Assert.Contains("LeadReference", sql);
            Assert.Contains("FirstName", sql);
        }

        // The whole lead reads its per-row sub-selects and long text; a list row reads none of them.
        foreach (var part in new[] { "[BookingRequests]", "[LeadFollowUps]", "[LeadDocuments]", "[Notes]", "[ClosureNotes]", "[IntegrationError]" })
        {
            Assert.Contains(part, responseSql);
            Assert.DoesNotContain(part, listSql);
        }

        // Closed-by is a page-header sub-select, so it is on the lead page only.
        Assert.DoesNotContain("PerformedByName", responseSql);
        Assert.Contains("PerformedByName", detailSql);
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
