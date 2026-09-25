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
/// An edit replaces every detail field, so one made from an older copy of the lead would
/// silently revert whatever was saved since. These pin that such an edit is refused instead.
/// </summary>
public sealed class LeadEditConcurrencyTests
{
    [Fact]
    public async Task TwoFormsOpenedBeforeEitherSaves_TheSecondSaveIsRefused_AndTheFirstIsKept()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();

        // Both people open the edit form on the same version.
        var first = await h.Leads.GetByIdAsync(leadId, h.Sales);
        var second = await h.Leads.GetByIdAsync(leadId, h.Admin);
        Assert.Equal(first!.ConcurrencyToken, second!.ConcurrencyToken);

        var saved = await h.Leads.UpdateAsync(leadId, Edit(first, city: "Lahore", notes: "Wants a corner plot"), h.Sales);
        Assert.NotEqual(first.ConcurrencyToken, saved.ConcurrencyToken);

        var error = await Assert.ThrowsAsync<LeadConcurrencyException>(() =>
            h.Leads.UpdateAsync(leadId, Edit(second, city: "Karachi", notes: null), h.Admin));
        Assert.Contains("Reload", error.Message);

        h.Db.ChangeTracker.Clear();
        var stored = await h.LoadLeadAsync(leadId);
        Assert.Equal("Lahore", stored.City);
        Assert.Equal("Wants a corner plot", stored.Notes);
    }

    [Fact]
    public async Task AnExternalEnquiryEnrichingTheLead_IsNotRevertedByAFormOpenedBeforeIt()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var created = await h.Leads.IngestAsync(LeadTestHarness.Intake(), h.Admin);
        var form = await h.Leads.GetByIdAsync(created.Lead!.Id, h.Admin);

        var enquiry = LeadTestHarness.Intake(firstName: "Bilal", phone: "+92 300 1234567");
        enquiry.ExternalProvider = "meta";
        enquiry.ExternalLeadId = "meta-lead-1";
        enquiry.City = "Islamabad";
        enquiry.BudgetMax = 12_000_000m;
        // Exactly as the Meta processor sends it: a repeat enquiry enriches the open lead.
        enquiry.AllowDuplicate = true;
        var enriched = await h.Leads.IngestAsync(enquiry, actor: null, trustedExternal: true);
        Assert.True(enriched.EnrichedExisting);

        // The form still shows no city and no budget; saving it as-is would wipe both.
        await Assert.ThrowsAsync<LeadConcurrencyException>(() =>
            h.Leads.UpdateAsync(form!.Id, Edit(form, city: null, notes: "Called back"), h.Admin));

        h.Db.ChangeTracker.Clear();
        var stored = await h.LoadLeadAsync(form!.Id);
        Assert.Equal("Islamabad", stored.City);
        Assert.Equal(12_000_000m, stored.BudgetMax);
        Assert.Equal(enriched.Lead!.SourceDetails, stored.SourceDetails);
        Assert.Null(stored.Notes);
    }

    [Fact]
    public async Task AfterAConflict_EditingFromTheReloadedLeadSaves()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var stale = await h.Leads.GetByIdAsync(leadId, h.Admin);
        await h.Leads.UpdateAsync(leadId, Edit(stale!, city: "Lahore", notes: null), h.Sales);
        await Assert.ThrowsAsync<LeadConcurrencyException>(() =>
            h.Leads.UpdateAsync(leadId, Edit(stale!, city: stale!.City, notes: "Mine"), h.Admin));

        h.Db.ChangeTracker.Clear();
        var reloaded = await h.Leads.GetByIdAsync(leadId, h.Admin);
        var saved = await h.Leads.UpdateAsync(leadId, Edit(reloaded!, city: reloaded!.City, notes: "Mine"), h.Admin);

        Assert.Equal("Lahore", saved.City);
        Assert.Equal("Mine", saved.Notes);
    }

    [Fact]
    public async Task ConsecutiveEditsThatEachSendTheLatestVersion_AllSave()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var current = (await h.Leads.GetByIdAsync(leadId, h.Sales))!;

        foreach (var city in new[] { "Lahore", "Multan", "Quetta" })
            current = await h.Leads.UpdateAsync(leadId, Edit(current, city: city, notes: null), h.Sales);

        Assert.Equal("Quetta", current.City);
    }

    // A client that does not say what version it saw has a bug; nothing changed underneath it,
    // so this is a plain bad request, not the conflict a reload-and-merge would answer.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64!")]
    public async Task AnEditWithoutAUsableVersion_IsRefusedAsABadRequest(string? token)
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var lead = await h.Leads.GetByIdAsync(leadId, h.Admin);
        var dto = Edit(lead!, city: "Lahore", notes: null);
        dto.ConcurrencyToken = token;

        // Exactly InvalidOperationException (400), not its LeadConcurrencyException subclass (409).
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Leads.UpdateAsync(leadId, dto, h.Admin));

        h.Db.ChangeTracker.Clear();
        Assert.Null((await h.LoadLeadAsync(leadId)).City);
    }

    [Fact]
    public async Task TheEndpointAnswersAStaleEditWith409_AMissingVersionWith400_AndACurrentOneWith200()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var controller = new LeadsController(new FixedResolver(h.Admin), h.Leads, h.Alerts)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var first = await h.Leads.GetByIdAsync(leadId, h.Admin);
        var second = await h.Leads.GetByIdAsync(leadId, h.Admin);

        var ok = await controller.Update(leadId, Edit(first!, city: "Lahore", notes: null), CancellationToken.None);
        Assert.IsType<OkObjectResult>(ok);

        var stale = await controller.Update(leadId, Edit(second!, city: "Karachi", notes: null), CancellationToken.None);
        var conflict = Assert.IsType<ConflictObjectResult>(stale);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);

        h.Db.ChangeTracker.Clear();
        var unversioned = Edit((await h.Leads.GetByIdAsync(leadId, h.Admin))!, city: "Quetta", notes: null);
        unversioned.ConcurrencyToken = null;
        Assert.IsType<BadRequestObjectResult>(await controller.Update(leadId, unversioned, CancellationToken.None));
    }

    // Calls, follow-ups, visits, comments and documents also write the lead row but let EF's own
    // exception escape. A lost race there must read as a conflict, not a server error.
    [Fact]
    public async Task ARawRowVersionRaceFromAnyLeadEndpoint_Answers409()
    {
        var controller = new ProbeController
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.Probe(() => throw new DbUpdateConcurrencyException("lost race"));

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains(LeadConcurrencyException.DefaultMessage, conflict.Value!.ToString());
    }

    private static UpdateLeadDto Edit(LeadResponseDto lead, string? city, string? notes) => new()
    {
        FirstName = lead.FirstName,
        LastName = lead.LastName,
        Phone = lead.Phone,
        WhatsappNumber = lead.WhatsappNumber,
        Email = lead.Email,
        Address = lead.Address,
        City = city,
        PreferredContactMethod = lead.PreferredContactMethod,
        PreferredContactTime = lead.PreferredContactTime,
        SourceDetails = lead.SourceDetails,
        CampaignName = lead.CampaignName,
        CampaignReference = lead.CampaignReference,
        AdReference = lead.AdReference,
        InterestedProjectId = lead.InterestedProjectId,
        InterestedUnitId = lead.InterestedUnitId,
        PropertyType = lead.PropertyType,
        PreferredLocation = lead.PreferredLocation,
        BudgetMin = lead.BudgetMin,
        BudgetMax = lead.BudgetMax,
        PurchaseIntent = lead.PurchaseIntent,
        Notes = notes,
        ConcurrencyToken = lead.ConcurrencyToken
    };

    private sealed class ProbeController() : LeadControllerBase(new FixedResolver(new LeadUserContext { UserId = 1, Role = LeadRoles.Admin }))
    {
        public Task<IActionResult> Probe(Func<Task<int>> action) => RunAsync(_ => action(), CancellationToken.None);
    }

    private sealed class FixedResolver(LeadUserContext ctx) : ILeadUserContextResolver
    {
        public Task<LeadUserContext> ResolveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default) =>
            Task.FromResult(ctx);
    }
}
