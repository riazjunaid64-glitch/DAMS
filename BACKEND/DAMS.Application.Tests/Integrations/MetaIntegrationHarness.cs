using DAMS.Application.Common;
using DAMS.Application.Interfaces;
using DAMS.Application.Services.Integrations;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace DAMS.Application.Tests.Integrations;

/// <summary>
/// The Meta integration wired on top of the existing lead harness, so tests exercise the
/// real ingestion path — duplicates, enrichment, notifications — rather than a stand-in.
/// </summary>
internal sealed class MetaIntegrationHarness : IAsyncDisposable
{
    public LeadTestHarness Leads { get; }
    public FakeMetaGraphClient Graph { get; }
    public MetaIntegrationOptions Options { get; }
    public MetaWebhookIntakeService Intake { get; }
    public MetaLeadEventProcessor Processor { get; }
    public MetaLeadBackfillService Backfill { get; }
    public MetaResourceSyncService Sync { get; }
    public MetaIntegrationService Integration { get; }
    public MetaIntegrationAlertService Alerts { get; }

    public Infrastructure.Data.AppDbContext Db => Leads.Db;

    private MetaIntegrationHarness(LeadTestHarness leads, IIntegrationSecretProtector protector)
    {
        Leads = leads;
        Graph = new FakeMetaGraphClient();

        Options = new MetaIntegrationOptions
        {
            AppId = "test-app-id",
            AppSecret = "test-app-secret",
            WebhookVerifyToken = "test-verify-token",
            OAuthCallbackUrl = "https://dams.test/api/integrations/meta/callback",
            FrontendReturnUrl = "https://dams.test/crm/settings",
            BaseRetryDelaySeconds = 60,
            MaxRetryDelayMinutes = 60,
            MaxAttempts = 3,
            AuthRetryDelayHours = 6
        };

        Intake = new MetaWebhookIntakeService(leads.Db, NullLogger<MetaWebhookIntakeService>.Instance);
        Backfill = new MetaLeadBackfillService(leads.Db, Graph, protector, Options, NullLogger<MetaLeadBackfillService>.Instance);
        Sync = new MetaResourceSyncService(leads.Db, Graph, protector, Options, Backfill, NullLogger<MetaResourceSyncService>.Instance);
        Processor = new MetaLeadEventProcessor(
            leads.Db, Graph, protector, leads.Leads, leads.Dispatcher, Options, NullLogger<MetaLeadEventProcessor>.Instance);
        Integration = new MetaIntegrationService(
            leads.Db, Graph, protector, Sync, Options, NullLogger<MetaIntegrationService>.Instance);
        Alerts = new MetaIntegrationAlertService(leads.Db, leads.Dispatcher, Options);
    }

    public static async Task<MetaIntegrationHarness> CreateAsync(IIntegrationSecretProtector? protector = null) =>
        new(await LeadTestHarness.CreateAsync(), protector ?? new PlaintextSecretProtector());

    /// <summary>
    /// A connected account with one enabled page, which is the state every ingestion test
    /// starts from. Returns the connection and the page.
    /// </summary>
    public async Task<(ExternalIntegrationConnection Connection, ExternalIntegrationResource Page)> ConnectPageAsync(
        string pageId = "page-1",
        string displayName = "Acme Marketing",
        string externalAccountId = "meta-user-1",
        bool enabled = true,
        string resourceType = "facebook_page")
    {
        var protector = new PlaintextSecretProtector();

        var connection = new ExternalIntegrationConnection
        {
            Provider = IntegrationProviders.Meta,
            ExternalAccountId = externalAccountId,
            DisplayName = displayName,
            Status = ExternalIntegrationConnectionStatus.Connected,
            AccessTokenProtected = protector.Protect($"user-token-{externalAccountId}"),
            ConnectedByUserId = Leads.AdminUserId,
            ConnectedAt = DateTime.UtcNow
        };
        Db.ExternalIntegrationConnections.Add(connection);
        await Db.SaveChangesAsync();

        var page = new ExternalIntegrationResource
        {
            ExternalIntegrationConnectionId = connection.Id,
            Provider = IntegrationProviders.Meta,
            ResourceType = resourceType,
            ExternalId = pageId,
            Name = $"Page {pageId}",
            IsEnabled = enabled,
            IsActive = true,
            IsSubscribed = enabled,
            ResourceTokenProtected = protector.Protect($"page-token-{pageId}")
        };
        Db.ExternalIntegrationResources.Add(page);
        await Db.SaveChangesAsync();

        return (connection, page);
    }

    /// <summary>The exact body shape Meta posts for a lead-ad submission.</summary>
    public static string WebhookBody(string pageId, string leadgenId, string formId = "form-1", string? adId = null) => $$"""
        {
          "object": "page",
          "entry": [{
            "id": "{{pageId}}",
            "time": 1755331200,
            "changes": [{
              "field": "leadgen",
              "value": {
                {{(adId is null ? "" : $"\"ad_id\": \"{adId}\",")}}
                "page_id": "{{pageId}}",
                "form_id": "{{formId}}",
                "leadgen_id": "{{leadgenId}}",
                "created_time": 1755331200
              }
            }]
          }]
        }
        """;

    public ValueTask DisposeAsync() => Leads.DisposeAsync();
}
