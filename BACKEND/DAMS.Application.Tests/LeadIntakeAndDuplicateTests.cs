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

        // The notification is queued before the lead's final LD-###### reference is flushed to
        // the database (both happen in the same save, so a crash between them can never lose
        // the notification). Its Data must still carry the real reference, not the
        // LD-PENDING-<guid> placeholder that exists in the database at that instant.
        var alert = alerts.Single(n => n.RecipientUserId == h.AdminUserId);
        Assert.Contains(lead.Lead!.LeadReference, alert.DataJson);
        Assert.DoesNotContain("PENDING", alert.DataJson);
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

    // "Add this enquiry to LD-…" names one lead; the server must add to that lead or to none.

    [Fact]
    public async Task AddingToTheChosenLead_EnrichesIt_WhenItStillMatches()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var original = await h.Leads.IngestAsync(LeadTestHarness.Intake(), h.Admin);

        var repeat = LeadTestHarness.Intake(email: null);
        repeat.AllowDuplicate = true;
        repeat.ExpectedExistingLeadId = original.Lead!.Id;
        repeat.Notes = "Second visit.";

        var result = await h.Leads.IngestAsync(repeat, h.Admin);

        Assert.True(result.EnrichedExisting);
        Assert.Equal(original.Lead.Id, result.Lead!.Id);
        Assert.Contains("Second visit.", result.Lead.Notes);
        Assert.Equal(1, await h.Db.Leads.CountAsync());
    }

    [Fact]
    public async Task AddingToTheChosenLead_CreatesNothing_WhenItWasClosedInTheMeantime()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var original = await h.Leads.IngestAsync(LeadTestHarness.Intake(), h.Admin);
        var stored = await h.Db.Leads.SingleAsync(l => l.Id == original.Lead!.Id);
        stored.Stage = LeadStage.Lost;
        await h.Db.SaveChangesAsync();

        var repeat = LeadTestHarness.Intake(email: null);
        repeat.AllowDuplicate = true;
        repeat.ExpectedExistingLeadId = original.Lead!.Id;

        var result = await h.Leads.IngestAsync(repeat, h.Admin);

        // Without the expected id this exact request creates a new lead, which the
        // "add to LD-…" button never promised.
        Assert.Null(result.Lead);
        Assert.False(result.IsDuplicate);
        Assert.Contains("no longer match", result.Message);
        Assert.Equal(1, await h.Db.Leads.CountAsync());
    }

    [Fact]
    public async Task AddingToTheChosenLead_TouchesNeitherLead_WhenTheDetailsNowMatchAnother()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var chosen = await h.Leads.IngestAsync(LeadTestHarness.Intake(), h.Admin);
        var other = await h.Leads.IngestAsync(
            LeadTestHarness.Intake(firstName: "Other", phone: "0321-7654321", email: "other@example.com"), h.Admin);

        var repeat = LeadTestHarness.Intake(phone: "0321-7654321", email: null);
        repeat.AllowDuplicate = true;
        repeat.ExpectedExistingLeadId = chosen.Lead!.Id;
        repeat.Notes = "Must not land anywhere.";

        var result = await h.Leads.IngestAsync(repeat, h.Admin);

        Assert.True(result.IsDuplicate);
        Assert.False(result.EnrichedExisting);
        Assert.Null(result.Lead);
        Assert.Equal(other.Lead!.Id, result.Match!.LeadId);
        Assert.Equal(2, await h.Db.Leads.CountAsync());
        Assert.DoesNotContain(await h.Db.Leads.Select(l => l.Notes).ToListAsync(),
            n => n != null && n.Contains("Must not land anywhere."));
    }

    [Fact]
    public async Task StaffRepeatCannotRevealEnrichOrDuplicateAnInaccessibleLead()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var original = await h.Leads.IngestAsync(LeadTestHarness.Intake(), h.Admin);
        await h.Leads.AssignAsync(original.Lead!.Id,
            new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin);

        var repeat = LeadTestHarness.Intake(firstName: "Different person", email: "repeat@example.com");
        repeat.AllowDuplicate = true;
        repeat.City = "Lahore";

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Leads.IngestAsync(repeat, h.OtherSales));

        Assert.DoesNotContain(original.Lead.LeadReference, error.Message);
        Assert.Equal(1, await h.Db.Leads.CountAsync());

        var unchanged = await h.LoadLeadAsync(original.Lead.Id);
        Assert.Null(unchanged.City);
        Assert.Equal(h.SalesEmployeeId, unchanged.AssignedEmployeeId);
    }

    [Fact]
    public async Task StaffSuppliedExternalReferenceCannotReplayOrDuplicateAnInaccessibleLead()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var external = LeadTestHarness.Intake();
        external.ExternalProvider = "portal";
        external.ExternalLeadId = "submission-1";

        var original = await h.Leads.IngestAsync(external, actor: null, trustedExternal: true);
        await h.Leads.AssignAsync(original.Lead!.Id,
            new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin);

        var manual = LeadTestHarness.Intake(firstName: "Walk in", email: "walkin@example.com");
        manual.ExternalProvider = "portal";
        manual.ExternalLeadId = "submission-1";
        manual.City = "Lahore";

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Leads.IngestAsync(manual, h.OtherSales));

        Assert.DoesNotContain(original.Lead.LeadReference, error.Message);
        Assert.Equal(1, await h.Db.Leads.CountAsync());
    }

    // A number is one identity whichever field it arrives in, in whatever format.
    [Theory]
    [InlineData("phone", "phone")]
    [InlineData("phone", "whatsapp")]
    [InlineData("whatsapp", "phone")]
    [InlineData("whatsapp", "whatsapp")]
    public async Task TheSameNumber_MatchesWhicheverFieldItWasEnteredIn(string existingField, string incomingField)
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var existing = LeadTestHarness.Intake(phone: null, email: null);
        SetNumber(existing, existingField, "0300-1234567");
        var original = await h.Leads.IngestAsync(existing, h.Admin);

        var incoming = LeadTestHarness.Intake(firstName: "Again", phone: null, email: null);
        SetNumber(incoming, incomingField, "+92 300 1234567");
        var result = await h.Leads.IngestAsync(incoming, h.Admin);

        Assert.True(result.IsDuplicate);
        Assert.Null(result.Lead);
        Assert.Equal(original.Lead!.Id, result.Match!.LeadId);
        // Named after the incoming field, so staff see which of their entries matched.
        Assert.Equal(incomingField, result.Match.MatchedOn);
        Assert.Equal(1, await h.Db.Leads.CountAsync());
    }

    [Theory]
    [InlineData("phone", "phone")]
    [InlineData("phone", "whatsapp")]
    [InlineData("whatsapp", "phone")]
    [InlineData("whatsapp", "whatsapp")]
    public async Task DifferentNumbers_AreNeverMatched(string existingField, string incomingField)
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var existing = LeadTestHarness.Intake(phone: null, email: null);
        SetNumber(existing, existingField, "0300-1234567");
        await h.Leads.IngestAsync(existing, h.Admin);

        var incoming = LeadTestHarness.Intake(firstName: "Someone Else", phone: null, email: null);
        SetNumber(incoming, incomingField, "0300-1234568");
        var result = await h.Leads.IngestAsync(incoming, h.Admin);

        Assert.False(result.IsDuplicate);
        Assert.NotNull(result.Lead);
        Assert.Equal(2, await h.Db.Leads.CountAsync());
    }

    [Theory]
    [InlineData("phone", "phone")]
    [InlineData("phone", "whatsapp")]
    [InlineData("whatsapp", "phone")]
    [InlineData("whatsapp", "whatsapp")]
    public async Task EditingInAnotherOpenLeadsNumber_IsRefused_WhicheverFieldEitherUses(
        string otherLeadField, string editedField)
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var other = LeadTestHarness.Intake(firstName: "Other", phone: null, email: null);
        SetNumber(other, otherLeadField, "0300-1234567");
        await h.Leads.IngestAsync(other, h.Admin);
        var mine = await h.Leads.IngestAsync(
            LeadTestHarness.Intake(firstName: "Mine", phone: null, email: "mine@example.com"), h.Admin);

        var edit = new UpdateLeadDto { FirstName = "Mine", Email = "mine@example.com" };
        if (editedField == "phone") edit.Phone = "+92 300 1234567"; else edit.WhatsappNumber = "+92 300 1234567";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Leads.UpdateAsync(mine.Lead!.Id, edit, h.Admin));
        Assert.Contains("Another open lead already uses", ex.Message);
    }

    private static void SetNumber(LeadIntakeDto dto, string field, string number)
    {
        if (field == "phone") dto.Phone = number; else dto.WhatsappNumber = number;
    }

    [Fact]
    public async Task ReopeningALead_IsRefused_WhenThatPersonAlreadyHasAnotherOpenLead()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var old = await h.Leads.IngestAsync(LeadTestHarness.Intake(phone: "0300-1234567", email: null), h.Admin);
        var reasonId = await ReasonIdAsync(h, "not_interested");
        await h.Leads.CloseAsync(old.Lead!.Id, dormant: false, new CloseLeadDto { ClosureReasonId = reasonId }, h.Admin);

        // They came back; with the old lead closed, the new enquiry rightly became a new lead —
        // this time with the number given as WhatsApp.
        var fresh = LeadTestHarness.Intake(firstName: "Back Again", phone: null, email: null);
        fresh.WhatsappNumber = "+92 300 1234567";
        var current = await h.Leads.IngestAsync(fresh, h.Admin);
        Assert.NotEqual(old.Lead.Id, current.Lead!.Id);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.ReopenAsync(old.Lead.Id, new ReopenLeadDto { Stage = LeadStage.New, Reason = "They called back." }, h.Admin));

        Assert.Contains(current.Lead.LeadReference, refused.Message);
        Assert.Contains("phone number", refused.Message);
        Assert.Equal(LeadStage.Lost, (await h.Db.Leads.AsNoTracking().SingleAsync(l => l.Id == old.Lead.Id)).Stage);
    }

    [Fact]
    public async Task ReopeningALead_StillWorks_WhenNoOtherOpenLeadHasItsDetails()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var old = await h.Leads.IngestAsync(LeadTestHarness.Intake(phone: "0300-1234567", email: null), h.Admin);
        var reasonId = await ReasonIdAsync(h, "not_interested");
        await h.Leads.CloseAsync(old.Lead!.Id, dormant: false, new CloseLeadDto { ClosureReasonId = reasonId }, h.Admin);

        var reopened = await h.Leads.ReopenAsync(old.Lead.Id, new ReopenLeadDto { Stage = LeadStage.New, Reason = "They called back." }, h.Admin);

        Assert.Equal(LeadStage.New.ToString(), reopened.Stage.ToString());
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

        var first = await h.Leads.IngestAsync(dto, actor: null, trustedExternal: true);
        var replay = await h.Leads.IngestAsync(dto, actor: null, trustedExternal: true);

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
    public async Task UnusablePhone_IsRejected_WhenItIsTheOnlyContactMethod()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Leads.IngestAsync(LeadTestHarness.Intake(phone: "12345", email: null), h.Admin));
    }

    [Fact]
    public async Task UnusablePhone_IsDiscarded_WhenAnotherContactMethodExists()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        var result = await h.Leads.IngestAsync(
            LeadTestHarness.Intake(phone: "12345", email: "reachable@example.com"), h.Admin);

        // The lead is worth keeping — but an undialable number must not be stored as if it
        // were real, because it would follow the record around and never match anything.
        Assert.NotNull(result.Lead);
        Assert.Null(result.Lead!.Phone);

        var stored = await h.Db.Leads.SingleAsync(l => l.Id == result.Lead.Id);
        Assert.Null(stored.NormalizedPhone);
        Assert.Equal("reachable@example.com", stored.Email);
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
        Assert.Equal(14, sources.Count);
        Assert.Contains(sources, s => s.Code == "whatsapp");
        Assert.Contains(sources, s => s.Code == "broker");
        // Used when a Meta lead cannot be pinned to Facebook or Instagram with confidence.
        Assert.Contains(sources, s => s.Code == "meta");
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
