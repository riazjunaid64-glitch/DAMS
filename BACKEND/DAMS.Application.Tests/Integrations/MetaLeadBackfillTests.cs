using DAMS.Application.Common;
using DAMS.Application.DTOs.IntegrationDtos;
using DAMS.Application.Services.Integrations;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests.Integrations;

/// <summary>
/// KAN-35: leads the webhook never delivered are read back from each form's own edge and handed
/// to the normal event processor, without duplicating anything the webhook did deliver.
/// </summary>
public class MetaLeadBackfillTests
{
    private static MetaLead Lead(string leadgenId, string name, DateTime createdAt, string formId = "form-1")
    {
        var lead = FakeMetaGraphClient.Lead(leadgenId,
            [("full_name", name), ("phone_number", $"+92 300 {Math.Abs(leadgenId.GetHashCode()) % 10000000:0000000}")],
            formId: formId);
        lead.CreatedTime = createdAt;
        return lead;
    }

    /// <summary>A connected, enabled Page with one synced lead form.</summary>
    private static async Task<(ExternalIntegrationConnection Connection, ExternalIntegrationResource Page)> SetUpAsync(
        MetaIntegrationHarness h, bool enabled = true)
    {
        var (connection, page) = await h.ConnectPageAsync(enabled: enabled);
        h.Db.ExternalIntegrationResources.Add(new ExternalIntegrationResource
        {
            ExternalIntegrationConnectionId = connection.Id,
            Provider = IntegrationProviders.Meta,
            ResourceType = ExternalResourceTypes.LeadForm,
            ExternalId = "form-1",
            ParentExternalId = page.ExternalId,
            Name = "Floria enquiry",
            IsActive = true
        });
        await h.Db.SaveChangesAsync();
        return (connection, page);
    }

    private static ImportMetaLeadsDto ForPage(ExternalIntegrationResource page, int daysBack) =>
        new() { ResourceId = page.Id, Since = DateOnly.FromDateTime(PakistanTime.Today.AddDays(-daysBack)) };

    [Fact]
    public async Task AnImport_QueuesMissedLeadsAsBackfillEvents_ThatBecomeLeadsThroughTheNormalPath()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await SetUpAsync(h);
        var lead = Lead("lead-1", "Ali Khan", DateTime.UtcNow.AddDays(-3));
        h.Graph.FormLeads["form-1"] = [lead];
        h.Graph.Leads["lead-1"] = lead;

        var result = await h.Backfill.ImportAsync(connection.Id, ForPage(page, 7), h.Leads.Admin);

        Assert.Equal((1, 1, 0, 0), (result.Found, result.New, result.AlreadyInDams, result.Failed));
        var queued = await h.Db.ExternalIntegrationEvents.AsNoTracking().SingleAsync();
        Assert.Equal(MetaLeadBackfillService.BackfillEventType, queued.EventType);
        Assert.Equal(ExternalIntegrationEventStatus.Pending, queued.Status);
        // Read with the Page's own token — the credential leads are fetched with.
        Assert.Equal("page-token-page-1", Assert.Single(h.Graph.FormLeadRequests).Token);

        Assert.Equal(1, await h.Processor.ProcessPendingEventsAsync(10));
        var created = await h.Db.Leads.AsNoTracking().SingleAsync();
        Assert.Equal("Ali", created.FirstName);
        Assert.Equal(lead.CreatedTime, created.ExternalSubmittedAt);
    }

    [Fact]
    public async Task ALeadTheWebhookAlreadyDelivered_IsCountedNotRepeated()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await SetUpAsync(h);
        var lead = Lead("lead-1", "Ali Khan", DateTime.UtcNow.AddHours(-2));
        h.Graph.Leads["lead-1"] = lead;
        h.Graph.FormLeads["form-1"] = [lead];
        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1"));
        await h.Processor.ProcessPendingEventsAsync(10);

        var result = await h.Backfill.ImportAsync(connection.Id, ForPage(page, 1), h.Leads.Admin);
        var again = await h.Backfill.ImportAsync(connection.Id, ForPage(page, 1), h.Leads.Admin);

        Assert.Equal((1, 0, 1), (result.Found, result.New, result.AlreadyInDams));
        Assert.Equal((1, 0, 1), (again.Found, again.New, again.AlreadyInDams));
        Assert.Equal(1, await h.Db.ExternalIntegrationEvents.CountAsync());
        Assert.Equal(1, await h.Db.Leads.CountAsync());
    }

    [Fact]
    public async Task ALeadIgnoredWhileItsPageWasOff_IsRecoveredOnceThePageIsOn()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await SetUpAsync(h, enabled: false);
        var lead = Lead("lead-1", "Ali Khan", DateTime.UtcNow.AddHours(-2));
        h.Graph.Leads["lead-1"] = lead;
        h.Graph.FormLeads["form-1"] = [lead];
        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1"));
        Assert.Equal(ExternalIntegrationEventStatus.Ignored, (await h.Db.ExternalIntegrationEvents.AsNoTracking().SingleAsync()).Status);

        page.IsEnabled = true;
        await h.Db.SaveChangesAsync();
        var result = await h.Backfill.ReconcileAsync(connection.Id);

        Assert.Equal(1, result.New);
        Assert.Equal(1, await h.Processor.ProcessPendingEventsAsync(10));
        Assert.Equal(1, await h.Db.Leads.CountAsync());
        Assert.Equal(1, await h.Db.ExternalIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task EverySync_ReconcilesTheConfiguredWindowOfEnabledPages()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await SetUpAsync(h);
        h.Graph.Pages = [new() { ResourceType = ExternalResourceTypes.FacebookPage, ExternalId = page.ExternalId, Name = page.Name, ResourceToken = "page-token-page-1" }];
        h.Graph.LeadForms = [new() { ResourceType = ExternalResourceTypes.LeadForm, ExternalId = "form-1", ParentExternalId = page.ExternalId, Name = "Floria enquiry" }];
        h.Graph.FormLeads["form-1"] =
        [
            Lead("lead-recent", "Recent Buyer", DateTime.UtcNow.AddHours(-30)),
            Lead("lead-old", "Old Buyer", DateTime.UtcNow.AddHours(-60))
        ];

        await h.Sync.SyncConnectionAsync(connection.Id);

        var request = Assert.Single(h.Graph.FormLeadRequests);
        Assert.InRange(request.Since, DateTime.UtcNow.AddHours(-48.1), DateTime.UtcNow.AddHours(-47.9));
        var queued = await h.Db.ExternalIntegrationEvents.AsNoTracking().SingleAsync();
        Assert.EndsWith(":lead-recent", queued.EventKey);

        h.Graph.FormLeadRequests.Clear();
        h.Options.ReconciliationLookbackHours = 0;
        await h.Sync.SyncConnectionAsync(connection.Id);
        Assert.Empty(h.Graph.FormLeadRequests);
    }

    [Fact]
    public async Task RecoveredLeads_DoNotHideAWebhookThatStoppedDelivering()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await SetUpAsync(h);
        h.Db.ExternalIntegrationEvents.Add(new ExternalIntegrationEvent
        {
            Provider = IntegrationProviders.Meta, ExternalIntegrationConnectionId = connection.Id,
            ExternalIntegrationResourceId = page.Id, EventType = "leadgen",
            EventKey = MetaWebhookIntakeService.EventKey(connection.Id, page.ExternalId, "lead-webhook"),
            RawPayloadJson = "{}", ReceivedAt = DateTime.UtcNow.AddDays(-5), Status = ExternalIntegrationEventStatus.Processed
        });
        await h.Db.SaveChangesAsync();
        h.Graph.FormLeads["form-1"] = [Lead("lead-recovered", "Ali Khan", DateTime.UtcNow.AddHours(-5))];
        await h.Backfill.ReconcileAsync(connection.Id);

        h.Options.QuietPageAlertDays = 3;

        Assert.Equal(1, await h.Alerts.RaiseAlertsAsync());
        var shown = Assert.Single(await h.Integration.GetConnectionsAsync());
        Assert.True(shown.LastLeadReceivedAt < DateTime.UtcNow.AddDays(-4));
    }

    [Fact]
    public async Task ReconciliationStillRuns_WhenMetaRefusesTheAccountsSignIn()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, _) = await SetUpAsync(h);
        h.Graph.DiscoveryFailure = new MetaAuthorizationException("Session has expired.", code: 190);
        h.Graph.FormLeads["form-1"] = [Lead("lead-1", "Ali Khan", DateTime.UtcNow.AddHours(-3))];

        await h.Sync.SyncConnectionAsync(connection.Id);

        Assert.Equal(1, await h.Db.ExternalIntegrationEvents.CountAsync());
    }

    [Theory]
    [InlineData(91)]
    [InlineData(-1)]
    public async Task AnImport_IsLimitedToNinetyDaysAndNotTheFuture(int daysBack)
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await SetUpAsync(h);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Backfill.ImportAsync(connection.Id, ForPage(page, daysBack), h.Leads.Admin));
        Assert.Empty(h.Graph.FormLeadRequests);
    }

    [Fact]
    public async Task AnImportOfExactlyNinetyDays_IsAllowed()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await SetUpAsync(h);

        await h.Backfill.ImportAsync(connection.Id, new ImportMetaLeadsDto
        {
            ResourceId = page.Id,
            Since = DateOnly.FromDateTime(PakistanTime.Today.AddDays(-89))
        }, h.Leads.Admin);

        Assert.Single(h.Graph.FormLeadRequests);
    }

    [Fact]
    public async Task APermissionRefusal_IsReportedAndLeavesTheConnectionAlone()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await SetUpAsync(h);
        h.Graph.FormLeadsFailure = new MetaAuthorizationException("(#200) Requires pages_manage_ads permission", code: 200);

        var result = await h.Backfill.ImportAsync(connection.Id, ForPage(page, 7), h.Leads.Admin);

        Assert.Equal((0, 0, 1), (result.Found, result.New, result.Failed));
        Assert.Contains("pages_manage_ads", result.Warning);
        Assert.Equal(ExternalIntegrationConnectionStatus.Connected,
            (await h.Db.ExternalIntegrationConnections.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task AnImport_IsForAdminsAndForEnabledPagesOnly()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await SetUpAsync(h, enabled: false);

        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            h.Backfill.ImportAsync(connection.Id, ForPage(page, 7), h.Leads.Manager));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Backfill.ImportAsync(connection.Id, ForPage(page, 7), h.Leads.Admin));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Backfill.ImportAsync(connection.Id, new ImportMetaLeadsDto { Since = DateOnly.FromDateTime(PakistanTime.Today) }, h.Leads.Admin));
    }

    [Fact]
    public async Task OneFormCanBeImportedOnItsOwn()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, _) = await SetUpAsync(h);
        h.Graph.FormLeads["form-1"] = [Lead("lead-1", "Ali Khan", DateTime.UtcNow.AddDays(-2))];

        var result = await h.Backfill.ImportAsync(connection.Id, new ImportMetaLeadsDto
        {
            FormExternalId = "form-1",
            Since = DateOnly.FromDateTime(PakistanTime.Today.AddDays(-7))
        }, h.Leads.Admin);

        Assert.Equal(1, result.New);
    }
}
