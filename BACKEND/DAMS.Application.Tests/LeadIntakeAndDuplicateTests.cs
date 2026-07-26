using DAMS.Application.Common;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

public sealed class LeadIntakeAndDuplicateTests
{
    [Fact]
    public async Task ManualLead_IsCreatedWithReference_SourceAndTimeline()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        var result = await h.Leads.IngestAsync(LeadTestHarness.Intake(sourceCode: "walk_in"), h.Admin);

        Assert.False(result.IsDuplicate);
        Assert.NotNull(result.Lead);
        Assert.Equal("walk_in", result.Lead!.SourceCode);
        Assert.Equal(LeadStage.New, result.Lead.Stage);
        Assert.Equal(LeadAssignmentState.Unassigned, result.Lead.AssignmentState);
        Assert.StartsWith("LD-", result.Lead.LeadReference);
        Assert.DoesNotContain("PENDING", result.Lead.LeadReference);

        var timeline = await h.TimelineAsync(result.Lead.Id);
        Assert.Contains(timeline, a => a.Type == LeadActivityType.LeadCreated);
        Assert.Contains(timeline, a => a.Type == LeadActivityType.SourceRecorded);
    }

    [Fact]
    public async Task NewLead_AlertsAdminsAndManagers()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        var lead = await h.Leads.IngestAsync(LeadTestHarness.Intake(), h.Admin);

        var alerts = await h.Db.Notifications
            .Where(n => n.EntityType == NotificationEntityType.Lead && n.EntityId == lead.Lead!.Id && n.Type == NotificationType.LeadCreated)
            .ToListAsync();

        Assert.Contains(alerts, n => n.RecipientUserId == h.AdminUserId);
    }

    [Fact]
    public async Task CampaignAndAdAttribution_ArePreserved()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        var dto = LeadTestHarness.Intake(sourceCode: "facebook");
        dto.CampaignName = "Summer Launch";
        dto.CampaignReference = "CMP-77";
        dto.AdReference = "AD-9";
        dto.ExternalFormReference = "FORM-1";

        var result = await h.Leads.IngestAsync(dto, h.Admin);

        Assert.Equal("Summer Launch", result.Lead!.CampaignName);
        Assert.Equal("CMP-77", result.Lead.CampaignReference);
        Assert.Equal("AD-9", result.Lead.AdReference);
        Assert.Equal("FORM-1", result.Lead.ExternalFormReference);
    }

    [Theory]
    [InlineData("0300-1234567", "0300 1234567")]
    [InlineData("0300-1234567", "+92 300 1234567")]
    [InlineData("0300-1234567", "923001234567")]
    public async Task DuplicatePhone_IsDetectedAcrossFormats(string first, string second)
    {
        await using var h = await LeadTestHarness.CreateAsync();
        await h.Leads.IngestAsync(LeadTestHarness.Intake(phone: first), h.Admin);

        var duplicate = await h.Leads.IngestAsync(
            LeadTestHarness.Intake(firstName: "Someone", phone: second, email: null), h.Admin);

        Assert.True(duplicate.IsDuplicate);
        Assert.Null(duplicate.Lead);
        Assert.Equal("phone", duplicate.Match!.MatchedOn);
        Assert.Equal(1, await h.Db.Leads.CountAsync());
    }

    [Fact]
    public async Task DuplicateEmail_IsDetected()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        await h.Leads.IngestAsync(LeadTestHarness.Intake(email: "same@example.com"), h.Admin);

        var duplicate = await h.Leads.IngestAsync(
            LeadTestHarness.Intake(firstName: "Other", phone: "03219998888", email: "SAME@example.com"), h.Admin);

        Assert.True(duplicate.IsDuplicate);
        Assert.Equal("email", duplicate.Match!.MatchedOn);
    }

    [Fact]
    public async Task AllowDuplicate_EnrichesExistingLead_WithoutOverwritingOriginalSource()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var original = await h.Leads.IngestAsync(LeadTestHarness.Intake(sourceCode: "walk_in"), h.Admin);

        var repeat = LeadTestHarness.Intake(firstName: "Bilal", email: null, sourceCode: "facebook");
        repeat.AllowDuplicate = true;
        repeat.City = "Lahore";
        repeat.Notes = "Asked about a corner unit.";
        repeat.CampaignName = "Summer Launch";

        var enriched = await h.Leads.IngestAsync(repeat, h.Admin);

        Assert.True(enriched.EnrichedExisting);
        Assert.Equal(original.Lead!.Id, enriched.Lead!.Id);
        Assert.Equal(1, await h.Db.Leads.CountAsync());
        // The original source stands; the new enquiry is appended, not substituted.
        Assert.Equal("walk_in", enriched.Lead.SourceCode);
        Assert.Contains("Facebook", enriched.Lead.SourceDetails);
        Assert.Equal("Lahore", enriched.Lead.City);
        Assert.Equal("Summer Launch", enriched.Lead.CampaignName);

        var timeline = await h.TimelineAsync(original.Lead.Id);
        Assert.Contains(timeline, a => a.Type == LeadActivityType.LeadEnriched);
    }

    [Fact]
    public async Task ClosedLead_DoesNotBlockANewEnquiryFromTheSamePerson()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var first = await h.CreateLeadAsync();
        var reasonId = await ReasonIdAsync(h, "not_interested");
        await h.Leads.CloseAsync(first, dormant: false, new CloseLeadDto { ClosureReasonId = reasonId }, h.Admin);

        var second = await h.Leads.IngestAsync(LeadTestHarness.Intake(), h.Admin);

        Assert.False(second.IsDuplicate);
        Assert.Equal(2, await h.Db.Leads.CountAsync());
    }

    [Fact]
    public async Task ExternalSubmission_IsIdempotent()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        var dto = LeadTestHarness.Intake(sourceCode: "facebook");
        dto.ExternalProvider = "facebook";
        dto.ExternalLeadId = "fb-lead-991";
        dto.AllowDuplicate = true;

        var first = await h.Leads.IngestAsync(dto, actor: null);
        var replay = await h.Leads.IngestAsync(dto, actor: null);

        Assert.False(first.AlreadyIngested);
        Assert.True(replay.AlreadyIngested);
        Assert.Equal(first.Lead!.Id, replay.Lead!.Id);
        Assert.Equal(1, await h.Db.Leads.CountAsync());
    }

    [Fact]
    public async Task ExistingCustomer_IsReportedButDoesNotBlockTheLead()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        h.Db.Customers.Add(new Domain.Entities.Customer
        {
            FullName = "Bilal Khan",
            Phone = "03001234567",
            Email = "bilal@example.com"
        });
        await h.Db.SaveChangesAsync();

        var result = await h.Leads.IngestAsync(LeadTestHarness.Intake(), h.Admin);

        Assert.False(result.IsDuplicate);
        Assert.NotNull(result.Lead);
        Assert.NotNull(result.Match);
        Assert.NotNull(result.Match!.CustomerId);
    }

    [Fact]
    public async Task UnknownOrInactiveSource_IsRejected()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Leads.IngestAsync(LeadTestHarness.Intake(sourceCode: "carrier_pigeon"), h.Admin));

        var source = await h.Db.LeadSources.FirstAsync(s => s.Code == "exhibition");
        source.IsActive = false;
        await h.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Leads.IngestAsync(LeadTestHarness.Intake(sourceCode: "exhibition"), h.Admin));
    }

    [Fact]
    public async Task UnusablePhone_IsRejected()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Leads.IngestAsync(LeadTestHarness.Intake(phone: "12345"), h.Admin));
    }

    [Fact]
    public async Task EmployeeCannotAssignOnCreation()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        var dto = LeadTestHarness.Intake();
        dto.AssignedEmployeeId = h.SalesEmployeeId;

        await Assert.ThrowsAsync<LeadAuthorizationException>(() => h.Leads.IngestAsync(dto, h.Sales));
    }

    [Fact]
    public async Task DefaultSources_AreSeededAndConfigurable()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        var sources = await h.Configuration.GetSourcesAsync(includeInactive: true);
        Assert.Equal(13, sources.Count);
        Assert.Contains(sources, s => s.Code == "whatsapp");
        Assert.Contains(sources, s => s.Code == "broker");
        Assert.All(sources, s => Assert.True(s.IsSystem));

        var created = await h.Configuration.CreateSourceAsync(new CreateLeadSourceDto
        {
            Code = "roadshow",
            Name = "Road Show",
            CustomerSource = CustomerSource.Other
        }, h.Admin);

        Assert.False(created.IsSystem);
        var lead = await h.Leads.IngestAsync(LeadTestHarness.Intake(sourceCode: "roadshow"), h.Admin);
        Assert.Equal("roadshow", lead.Lead!.SourceCode);
    }

    [Fact]
    public async Task OnlyAdminsCanChangeConfiguration()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        await Assert.ThrowsAsync<LeadAuthorizationException>(() => h.Configuration.CreateSourceAsync(
            new CreateLeadSourceDto { Code = "x", Name = "X" }, h.Manager));

        await Assert.ThrowsAsync<LeadAuthorizationException>(() => h.Configuration.CreateClosureReasonAsync(
            new CreateLeadClosureReasonDto { Code = "y", Name = "Y" }, h.Sales));
    }

    internal static async Task<int> ReasonIdAsync(LeadTestHarness h, string code) =>
        await h.Db.LeadClosureReasons.Where(r => r.Code == code).Select(r => r.Id).FirstAsync();
}
