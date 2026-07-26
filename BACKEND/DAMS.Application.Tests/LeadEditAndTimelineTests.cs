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

        var updated = await h.Leads.UpdateAsync(leadId, Update(email: "new@example.com"), h.Sales);

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

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.UpdateAsync(second, Update(phone: "0300-1234567"), h.Admin));

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

        var dto = Update();
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

        await h.Leads.UpdateQualificationAsync(leadId,
            new UpdateLeadQualificationDto { Qualification = LeadQualification.Warm }, h.Sales);
        await h.Leads.ChangeStageAsync(leadId, new ChangeLeadStageDto { Stage = LeadStage.Qualified }, h.Sales);

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

    private static UpdateLeadDto Update(string phone = "0300-1234567", string? email = "bilal@example.com") => new()
    {
        FirstName = "Bilal",
        LastName = "Khan",
        Phone = phone,
        Email = email,
        BudgetMin = 500_000m,
        BudgetMax = 9_000_000m,
        PurchaseIntent = LeadPurchaseIntent.Investment
    };
}
