using DAMS.Application.Common;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests.Integrations;

/// <summary>
/// Reaching the original Meta data from a lead: the provider's complete response and the webhook
/// event that delivered it, for the roles allowed to see it, including for an enquiry that was
/// held and resolved into the lead later.
/// </summary>
public class LeadOriginalMetaDataTests
{
    private static (string Name, string? Value)[] StandardFields =>
    [
        ("full_name", "Ali Khan"),
        ("phone_number", "+92 300 1234567"),
        ("email", "ali@example.com"),
        ("city", "Lahore")
    ];

    [Fact]
    public async Task ALeadFromMeta_ShowsItsOriginalResponseAndWebhookEvent()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (leadId, submissionId) = await IngestAsync(h, "lead-1");

        var raw = await h.Leads.Leads.GetExternalSubmissionRawAsync(leadId, submissionId, h.Leads.Admin);

        Assert.Equal(("meta", "lead-1"), (raw.Provider, raw.ExternalLeadId));
        Assert.Contains("Ali Khan", raw.RawPayloadJson);
        Assert.NotNull(raw.Event);
        Assert.Equal(ExternalIntegrationEventStatus.Processed, raw.Event!.Status);
        Assert.Contains("\"leadgen_id\": \"lead-1\"", raw.Event.RawPayloadJson);

        // Reading it is read-only: the separately stored raw data is exactly as it was.
        var stored = await h.Db.ExternalIntegrationEvents.AsNoTracking().SingleAsync();
        Assert.Equal(stored.RawPayloadJson, raw.Event.RawPayloadJson);
    }

    [Fact]
    public async Task ResolvingAHeldEnquiry_LinksItsWebhookEvent_AndOnlyThatOne()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (_, page) = await h.ConnectPageAsync();
        await h.Leads.Leads.IngestAsync(
            LeadTestHarness.Intake(firstName: "Phone Owner", phone: "0300-1234567", email: "someone@example.com"), h.Leads.Admin);
        await h.Leads.Leads.IngestAsync(
            LeadTestHarness.Intake(firstName: "Email Owner", phone: "0321-7654321", email: "ali@example.com"), h.Leads.Admin);

        // Both match both leads, so both are held. "lead-11" ends in "lead-1", which is exactly
        // the id a careless suffix match would confuse with it.
        foreach (var leadgenId in new[] { "lead-1", "lead-11" })
        {
            h.Graph.Leads[leadgenId] = FakeMetaGraphClient.Lead(leadgenId, StandardFields, pageId: page.ExternalId);
            await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, leadgenId));
        }
        await h.Processor.ProcessPendingEventsAsync(10);
        Assert.All(await h.Db.ExternalIntegrationEvents.AsNoTracking().ToListAsync(), e => Assert.Null(e.LeadId));

        var hold = await h.Db.LeadIntakeHolds.AsNoTracking().SingleAsync(x => x.ExternalLeadId == "lead-1");
        var chosen = await h.Db.Leads.AsNoTracking().Where(l => l.FirstName == "Email Owner").Select(l => l.Id).SingleAsync();
        await h.Leads.Leads.ResolveIntakeHoldAsync(hold.Id, new ResolveLeadIntakeHoldDto { LeadId = chosen }, h.Leads.Admin);

        var events = await h.Db.ExternalIntegrationEvents.AsNoTracking().ToListAsync();
        Assert.Equal(chosen, events.Single(e => e.EventKey.EndsWith(":lead-1")).LeadId);
        Assert.Null(events.Single(e => e.EventKey.EndsWith(":lead-11")).LeadId);

        // And the resolved lead now reaches both the stored response and the webhook event.
        var submission = await h.Db.LeadExternalSubmissions.AsNoTracking().SingleAsync();
        var raw = await h.Leads.Leads.GetExternalSubmissionRawAsync(chosen, submission.Id, h.Leads.Admin);
        Assert.Contains("Ali Khan", raw.RawPayloadJson);
        Assert.NotNull(raw.Event);
        Assert.Contains("\"leadgen_id\": \"lead-1\"", raw.Event!.RawPayloadJson);
    }

    [Fact]
    public async Task AManager_CanViewItForALeadInTheirScope()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (leadId, submissionId) = await IngestAsync(h, "lead-1");
        await h.Leads.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.Leads.SalesEmployeeId }, h.Leads.Admin);

        var raw = await h.Leads.Leads.GetExternalSubmissionRawAsync(leadId, submissionId, h.Leads.Manager);

        Assert.Contains("Ali Khan", raw.RawPayloadJson);
    }

    [Fact]
    public async Task AManager_CannotReachItForALeadOutsideTheirTeams()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (leadId, submissionId) = await IngestAsync(h, "lead-1");
        await h.Leads.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.Leads.OtherSalesEmployeeId }, h.Leads.Admin);

        await Assert.ThrowsAsync<LeadNotFoundException>(() =>
            h.Leads.Leads.GetExternalSubmissionRawAsync(leadId, submissionId, h.Leads.Manager));
    }

    [Fact]
    public async Task ASalesAgent_CannotViewIt_EvenOnTheirOwnLead()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (leadId, submissionId) = await IngestAsync(h, "lead-1");
        await h.Leads.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.Leads.SalesEmployeeId }, h.Leads.Admin);

        // The agent still sees the business-friendly receipt, just not the raw payload.
        Assert.Single(await h.Leads.Leads.GetExternalSubmissionsAsync(leadId, h.Leads.Sales));
        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            h.Leads.Leads.GetExternalSubmissionRawAsync(leadId, submissionId, h.Leads.Sales));
    }

    [Fact]
    public async Task ASubmissionOfAnotherLead_IsNotFound()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (_, submissionId) = await IngestAsync(h, "lead-1");
        var otherLead = await h.Leads.Leads.IngestAsync(
            LeadTestHarness.Intake(firstName: "Someone Else", phone: "0333-9998887", email: "else@example.com"), h.Leads.Admin);

        await Assert.ThrowsAsync<LeadNotFoundException>(() =>
            h.Leads.Leads.GetExternalSubmissionRawAsync(otherLead.Lead!.Id, submissionId, h.Leads.Admin));
    }

    private static async Task<(int LeadId, int SubmissionId)> IngestAsync(MetaIntegrationHarness h, string leadgenId)
    {
        var (_, page) = await h.ConnectPageAsync();
        h.Graph.Leads[leadgenId] = FakeMetaGraphClient.Lead(leadgenId, StandardFields, pageId: page.ExternalId);
        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, leadgenId));
        await h.Processor.ProcessPendingEventsAsync(10);

        var submission = await h.Db.LeadExternalSubmissions.AsNoTracking().SingleAsync(s => s.ExternalLeadId == leadgenId);
        return (submission.LeadId, submission.Id);
    }
}
