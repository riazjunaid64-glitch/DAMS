using System.Security.Claims;
using DAMS.Api.Controllers;
using DAMS.Application.Common;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// A lead's apartment type is the same fixed list units already use. Staff cannot store
/// anything else; Facebook, Instagram and website enquiries are never rejected for it.
/// </summary>
public sealed class LeadApartmentTypeTests
{
    [Theory]
    [InlineData("studio_apartment", false, "Studio")]
    [InlineData("1 bedroom apartment", false, "1 Bed")]
    [InlineData("1_bedroom_apartment", false, "1 Bed")]
    [InlineData("one bedroom", false, "1 Bed")]
    [InlineData("1 bhk", false, "1 Bed")]
    [InlineData("1bed", false, "1 Bed")]
    [InlineData("2 bedroom apartment", false, "2 Bed")]
    [InlineData("two bedroom", false, "2 Bed")]
    [InlineData("3_bedroom_apartment", false, "3 Bed")]
    [InlineData("three bedroom", false, "3 Bed")]
    [InlineData("parking", false, "Parking space")]
    [InlineData("Parking space", false, "Parking space")]
    [InlineData("  ", false, null)]
    [InlineData(null, false, null)]
    [InlineData("sky villa", true, null)]
    [InlineData("2_bedroom_apartment", true, "2 Bed")]
    public void KnownWords_BecomeTheCanonicalType_AndAnExternalUnknown_IsLeftEmpty(string? raw, bool external, string? expected)
    {
        Assert.Equal(expected, UnitTypes.ForLead(raw, external));
    }

    [Fact]
    public void AnUnrecognisedStaffValue_IsRefusedWithTheFixedMessage()
    {
        var error = Assert.Throws<InvalidOperationException>(() => UnitTypes.ForLead("Penthouse", externalEnquiry: false));
        Assert.Equal("Apartment type must be Studio, 1 Bed, 2 Bed or 3 Bed.", error.Message);
    }

    [Fact]
    public async Task Creating_StoresASynonymAsTheCanonicalType_AndRejectsAnUnknownOne()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        var created = await h.Leads.IngestAsync(Intake(propertyType: "2_bedroom_apartment"), h.Admin);
        Assert.Equal("2 Bed", created.Lead!.PropertyType);

        var blank = await h.Leads.IngestAsync(Intake(phone: "03215556677", email: "blank@example.com", propertyType: "  "), h.Admin);
        Assert.Null(blank.Lead!.PropertyType);

        var parking = await h.Leads.IngestAsync(Intake(phone: "03216667788", email: "park@example.com", propertyType: "parking"), h.Admin);
        Assert.Equal("Parking space", parking.Lead!.PropertyType);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.IngestAsync(Intake(phone: "03217778899", email: "nope@example.com", propertyType: "Penthouse"), h.Admin));
        Assert.Equal(UnitTypes.UnrecognisedLeadApartmentType, error.Message);
        Assert.Equal(3, await h.Db.Leads.CountAsync());
    }

    [Fact]
    public async Task Updating_StoresASynonym_ClearsABlank_AndRejectsAnUnknownWithoutChangingTheLead()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = (await h.Leads.IngestAsync(Intake(propertyType: "studio apartment"), h.Admin)).Lead!.Id;
        Assert.Equal("Studio", (await h.LoadLeadAsync(leadId)).PropertyType);

        var updated = await h.Leads.UpdateAsync(leadId, new UpdateLeadDto
        {
            ConcurrencyToken = await h.ConcurrencyTokenAsync(leadId),
            PropertyType = "one bedroom"
        }, h.Admin);
        Assert.Equal("1 Bed", updated.PropertyType);

        var cleared = await h.Leads.UpdateAsync(leadId, new UpdateLeadDto
        {
            ConcurrencyToken = updated.ConcurrencyToken,
            PropertyType = null
        }, h.Admin);
        Assert.Null(cleared.PropertyType);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Leads.UpdateAsync(leadId, new UpdateLeadDto
        {
            ConcurrencyToken = cleared.ConcurrencyToken,
            PropertyType = "villa"
        }, h.Admin));
        Assert.Equal(UnitTypes.UnrecognisedLeadApartmentType, error.Message);

        h.Db.ChangeTracker.Clear();
        Assert.Null((await h.LoadLeadAsync(leadId)).PropertyType);
    }

    [Fact]
    public async Task AnEditThatDoesNotMentionApartmentType_LeavesALegacyValueAlone()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = (await h.Leads.IngestAsync(Intake(), h.Admin)).Lead!.Id;
        var tracked = await h.Db.Leads.SingleAsync(l => l.Id == leadId);
        tracked.PropertyType = "Penthouse";
        await h.Db.SaveChangesAsync();

        var updated = await h.Leads.UpdateAsync(leadId, new UpdateLeadDto
        {
            ConcurrencyToken = await h.ConcurrencyTokenAsync(leadId),
            City = "Lahore"
        }, h.Admin);

        Assert.Equal("Lahore", updated.City);
        Assert.Equal("Penthouse", updated.PropertyType);
    }

    [Fact]
    public async Task Enriching_FillsAMissingTypeFromASynonym_AndAStaffUnknown_ChangesNothing()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var original = await h.Leads.IngestAsync(Intake(), h.Admin);

        var repeat = Intake(email: null);
        repeat.AllowDuplicate = true;
        repeat.PropertyType = "two bedroom";
        var enriched = await h.Leads.IngestAsync(repeat, h.Admin);

        Assert.True(enriched.EnrichedExisting);
        Assert.Equal(original.Lead!.Id, enriched.Lead!.Id);
        Assert.Equal("2 Bed", enriched.Lead.PropertyType);

        var again = Intake(email: null);
        again.AllowDuplicate = true;
        again.PropertyType = "villa";
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Leads.IngestAsync(again, h.Admin));
        Assert.Equal(UnitTypes.UnrecognisedLeadApartmentType, error.Message);

        h.Db.ChangeTracker.Clear();
        Assert.Equal("2 Bed", (await h.LoadLeadAsync(original.Lead.Id)).PropertyType);
        Assert.Equal("Bilal", (await h.LoadLeadAsync(original.Lead.Id)).FirstName);
    }

    [Theory]
    [InlineData("facebook", "meta", "fb-1", "not a real type")]
    [InlineData("instagram", "meta", "ig-1", "penthouse")]
    [InlineData("website", "website", "web-1", "sky villa")]
    public async Task AnExternalEnquiry_WithAnUnknownApartmentType_IsStoredWithoutOne(string source, string provider, string externalId, string propertyType)
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var result = await h.Leads.IngestAsync(new LeadIntakeDto
        {
            FirstName = "Ayesha",
            Phone = "03007654321",
            SourceCode = source,
            ExternalProvider = provider,
            ExternalLeadId = externalId,
            PropertyType = propertyType,
            AllowDuplicate = true
        }, actor: null, trustedExternal: true);

        Assert.NotNull(result.Lead);
        Assert.Null(result.Lead!.PropertyType);

        var submission = await h.Db.LeadExternalSubmissions.AsNoTracking().SingleAsync();
        var shown = await h.Leads.GetExternalSubmissionsAsync(result.Lead.Id, h.Admin);
        var answer = Assert.Single(Assert.Single(shown).FieldData);
        Assert.Equal(submission.Id, Assert.Single(shown).Id);
        Assert.Equal("property_type", answer.Name);
        Assert.Equal(propertyType, answer.Value);
        Assert.False(answer.IsMapped);
        Assert.Contains(propertyType, submission.FieldDataJson);
    }

    [Fact]
    public async Task ATrustedChannelWithoutAProviderId_IsNotRejectedForAnUnknownApartmentType()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        var result = await h.Leads.IngestAsync(new LeadIntakeDto
        {
            FirstName = "Ayesha",
            Phone = "03007654321",
            SourceCode = "website",
            PropertyType = "sky villa"
        }, actor: null, trustedExternal: true);

        Assert.NotNull(result.Lead);
        Assert.Null(result.Lead!.PropertyType);
    }

    [Fact]
    public async Task AnExternalRepeat_FillsAMissingType_AndDoesNotWipeOneItCannotRead()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var created = await h.Leads.IngestAsync(Intake(phone: "03001112233", email: "ayi@example.com"), h.Admin);

        var filled = await h.Leads.IngestAsync(ExternalRepeat("3_bedroom_apartment", "web-fill"), actor: null, trustedExternal: true);
        Assert.Equal(created.Lead!.Id, filled.Lead!.Id);
        Assert.Equal("3 Bed", filled.Lead.PropertyType);
        Assert.Null((await h.Db.LeadExternalSubmissions.AsNoTracking().SingleAsync(s => s.ExternalLeadId == "web-fill")).FieldDataJson);

        var kept = await h.Leads.IngestAsync(ExternalRepeat("sky villa", "web-keep"), actor: null, trustedExternal: true);
        Assert.Equal("3 Bed", kept.Lead!.PropertyType);
        Assert.Contains("sky villa", (await h.Db.LeadExternalSubmissions.AsNoTracking().SingleAsync(s => s.ExternalLeadId == "web-keep")).FieldDataJson);
    }

    [Fact]
    public async Task ApartmentTypesEndpoint_ReturnsTheFourLeadChoices()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var controller = new LeadConfigurationController(new FixedResolver(h.Admin), h.Configuration)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var ok = Assert.IsType<OkObjectResult>(await controller.GetApartmentTypes(CancellationToken.None));
        Assert.Equal(new[] { "Studio", "1 Bed", "2 Bed", "3 Bed" }, Assert.IsAssignableFrom<IReadOnlyList<string>>(ok.Value));
    }

    [Fact]
    public async Task AnUnrecognisedApartmentType_IsABadRequest()
    {
        var controller = new ApartmentProbe(new FixedResolver(new LeadUserContext { UserId = 1, Role = LeadRoles.Admin }))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.Probe(() => throw new InvalidOperationException(UnitTypes.UnrecognisedLeadApartmentType));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains(UnitTypes.UnrecognisedLeadApartmentType, badRequest.Value!.ToString());
    }

    private static LeadIntakeDto Intake(string? phone = "0300-1234567", string? email = "bilal@example.com", string? propertyType = null)
    {
        var dto = LeadTestHarness.Intake(phone: phone, email: email);
        dto.PropertyType = propertyType;
        return dto;
    }

    private static LeadIntakeDto ExternalRepeat(string propertyType, string externalId) => new()
    {
        FirstName = "Bilal",
        Phone = "03001112233",
        Email = "ayi@example.com",
        SourceCode = "website",
        ExternalProvider = "website",
        ExternalLeadId = externalId,
        PropertyType = propertyType,
        AllowDuplicate = true
    };

    private sealed class ApartmentProbe(ILeadUserContextResolver resolver) : LeadControllerBase(resolver)
    {
        public Task<IActionResult> Probe(Func<Task<int>> action) => RunAsync(_ => action(), CancellationToken.None);
    }

    private sealed class FixedResolver(LeadUserContext ctx) : ILeadUserContextResolver
    {
        public Task<LeadUserContext> ResolveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default) =>
            Task.FromResult(ctx);
    }
}
