using DAMS.Application.Common;
using DAMS.Application.DTOs.NotificationDtos;
using DAMS.Application.Services.Integrations;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests.Integrations;

/// <summary>
/// KAN-34: Meta lets the account's own token lapse after about 60 days while the Page tokens
/// leads are fetched with keep working. Lead capture must survive that, say so on the panel,
/// tell Admins when it genuinely stops, and catch up at once after a reconnect.
/// </summary>
public class MetaIntegrationHealthTests
{
    private static (string Name, string? Value)[] Fields =>
    [
        ("full_name", "Ali Khan"),
        ("phone_number", "+92 300 1234567"),
        ("email", "ali@example.com")
    ];

    private static MetaAuthorizationException Expired() =>
        new("Error validating access token: Session has expired.", code: 190, subCode: 463);

    // ── The account token lapses, the Page token does not ──────────────────────

    [Fact]
    public async Task AnExpiredAccountToken_WithAValidPageToken_StillCreatesLeadsAndWarns()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await h.ConnectPageAsync();
        h.Graph.DiscoveryFailure = Expired();
        // Ad discovery runs on the same token; asking would only be refused again and blamed on ads_read.
        h.Graph.AdAccountDiscoveryFailure = new MetaAuthorizationException("ads_read is not granted.");

        var result = await h.Sync.SyncConnectionAsync(connection.Id);

        Assert.Contains("reconnect", result.Warning);
        Assert.DoesNotContain("ads_read", result.Warning);
        var synced = await h.Db.ExternalIntegrationConnections.AsNoTracking().SingleAsync();
        Assert.Equal(ExternalIntegrationConnectionStatus.Connected, synced.Status);
        Assert.NotNull(synced.SyncRejectedAt);
        Assert.Contains("Session has expired", synced.LastError);
        // Nothing was refreshed, so "last synced" does not pretend otherwise.
        Assert.Null(synced.LastSyncedAt);
        Assert.True((await h.Db.ExternalIntegrationResources.AsNoTracking().SingleAsync()).IsEnabled);

        h.Graph.Leads["lead-1"] = FakeMetaGraphClient.Lead("lead-1", Fields, pageId: page.ExternalId);
        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1"));

        Assert.Equal(1, await h.Processor.ProcessPendingEventsAsync(10));
        Assert.Contains(("lead-1", "page-token-page-1"), h.Graph.LeadRequests);
        Assert.Equal(1, await h.Db.Leads.CountAsync());

        var shown = Assert.Single(await h.Integration.GetConnectionsAsync());
        Assert.Equal(ExternalIntegrationConnectionStatus.Connected, shown.Status);
        Assert.NotNull(shown.SyncRejectedAt);
        Assert.NotNull(shown.LastLeadReceivedAt);
    }

    [Fact]
    public async Task ARefusedSync_WaitsTheSyncIntervalBeforeAskingMetaAgain()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, _) = await h.ConnectPageAsync();
        h.Graph.DiscoveryFailure = Expired();
        await h.Sync.SyncConnectionAsync(connection.Id);

        var calls = 0;
        h.Graph.OnGetPages = () => calls++;

        // Never synced, yet not due: the refusal will not lift by itself within a tick.
        Assert.Equal(0, await h.Sync.SyncDueConnectionsAsync());
        Assert.Equal(0, calls);

        var stored = await h.Db.ExternalIntegrationConnections.SingleAsync();
        stored.SyncRejectedAt = DateTime.UtcNow.AddSeconds(-h.Options.ResourceSyncIntervalSeconds - 60);
        await h.Db.SaveChangesAsync();

        await h.Sync.SyncDueConnectionsAsync();
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ASyncThatGetsThroughAgain_ClearsTheWarning()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, _) = await h.ConnectPageAsync();
        h.Graph.DiscoveryFailure = Expired();
        await h.Sync.SyncConnectionAsync(connection.Id);

        h.Graph.DiscoveryFailure = null;
        h.Graph.Pages = [new() { ResourceType = ExternalResourceTypes.FacebookPage, ExternalId = "page-1", Name = "Page page-1", ResourceToken = "page-token" }];
        var result = await h.Sync.SyncConnectionAsync(connection.Id);

        Assert.Null(result.Warning);
        var synced = await h.Db.ExternalIntegrationConnections.AsNoTracking().SingleAsync();
        Assert.Null(synced.SyncRejectedAt);
        Assert.NotNull(synced.LastSyncedAt);
    }

    [Fact]
    public async Task APageTokenRefusedForLeadForms_IsAPageWarning_NotARefusedSignIn()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, _) = await h.ConnectPageAsync();
        h.Graph.Pages = [new() { ResourceType = ExternalResourceTypes.FacebookPage, ExternalId = "page-1", Name = "Page page-1", ResourceToken = "page-token" }];
        h.Graph.LeadFormsFailure = new MetaAuthorizationException("(#200) Requires leads_retrieval.", code: 200);

        var result = await h.Sync.SyncConnectionAsync(connection.Id);

        Assert.Contains("Lead forms for \"Page page-1\" could not be read.", result.Warning);
        var synced = await h.Db.ExternalIntegrationConnections.AsNoTracking().SingleAsync();
        Assert.Equal(ExternalIntegrationConnectionStatus.Connected, synced.Status);
        Assert.Null(synced.SyncRejectedAt);
        Assert.NotNull(synced.LastSyncedAt);
    }

    [Fact]
    public async Task ARejectedPageToken_StillParksLeadProcessing()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (_, page) = await h.ConnectPageAsync();
        h.Graph.LeadFailures.Enqueue(Expired());

        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1"));
        await h.Processor.ProcessPendingEventsAsync(10);

        var connection = await h.Db.ExternalIntegrationConnections.AsNoTracking().SingleAsync();
        Assert.Equal(ExternalIntegrationConnectionStatus.NeedsReauthorization, connection.Status);
        Assert.Equal(ExternalIntegrationEventStatus.Retry, (await h.Db.ExternalIntegrationEvents.AsNoTracking().SingleAsync()).Status);
    }

    // ── Reconnecting releases the backlog ──────────────────────────────────────

    [Fact]
    public async Task AfterAReconnect_ParkedEventsAreProcessedOnTheNextTick()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (_, page) = await h.ConnectPageAsync();
        h.Graph.LeadFailures.Enqueue(Expired());
        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1"));
        await h.Processor.ProcessPendingEventsAsync(10);

        var parked = await h.Db.ExternalIntegrationEvents.AsNoTracking().SingleAsync();
        Assert.True(parked.AvailableAt > DateTime.UtcNow.AddHours(5));

        await ReconnectAsync(h);

        var released = await h.Db.ExternalIntegrationEvents.AsNoTracking().SingleAsync();
        Assert.Equal(ExternalIntegrationEventStatus.Retry, released.Status);
        Assert.True(released.AvailableAt <= DateTime.UtcNow);
        Assert.Equal(0, released.Attempts);

        h.Graph.Leads["lead-1"] = FakeMetaGraphClient.Lead("lead-1", Fields, pageId: page.ExternalId);
        Assert.Equal(1, await h.Processor.ProcessPendingEventsAsync(10));
        Assert.Equal(1, await h.Db.Leads.CountAsync());
    }

    [Fact]
    public async Task AReconnectStillMissingAPermission_LeavesParkedEventsWaiting()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (_, page) = await h.ConnectPageAsync();
        h.Graph.LeadFailures.Enqueue(Expired());
        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1"));
        await h.Processor.ProcessPendingEventsAsync(10);

        h.Graph.Authorization.GrantedScopes = [MetaScopes.PagesShowList];
        await ReconnectAsync(h);

        Assert.True((await h.Db.ExternalIntegrationEvents.AsNoTracking().SingleAsync()).AvailableAt > DateTime.UtcNow.AddHours(5));
    }

    [Fact]
    public async Task AReconnect_ClearsARefusedSyncWarning()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, _) = await h.ConnectPageAsync();
        h.Graph.DiscoveryFailure = Expired();
        await h.Sync.SyncConnectionAsync(connection.Id);

        await ReconnectAsync(h);

        Assert.Null((await h.Db.ExternalIntegrationConnections.AsNoTracking().SingleAsync()).SyncRejectedAt);
    }

    // ── Admin alerts ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AConnectionNeedingReconnection_AlertsEveryActiveAdminOnce()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, _) = await h.ConnectPageAsync();
        var secondAdmin = new User { RoleId = 1, FullName = "Second Admin", Email = "second-admin@dams.test" };
        var disabledAdmin = new User
        {
            RoleId = 1, FullName = "Disabled Admin", Email = "disabled-admin@dams.test",
            AccountStatus = UserAccountStatus.Disabled
        };
        h.Db.Users.AddRange(secondAdmin, disabledAdmin);
        await MarkNeedsReauthorizationAsync(h, "Error validating access token.");

        Assert.Equal(2, await h.Alerts.RaiseAlertsAsync());
        Assert.Equal(0, await h.Alerts.RaiseAlertsAsync());

        var alerts = await IntegrationAlertsAsync(h);
        Assert.Equal(new[] { h.Leads.AdminUserId, secondAdmin.UserId }.OrderBy(id => id),
            alerts.Select(n => n.RecipientUserId!.Value).OrderBy(id => id));
        foreach (var alert in alerts)
        {
            Assert.Equal(NotificationEntityType.IntegrationConnection, alert.EntityType);
            Assert.Equal(connection.Id, alert.EntityId);
            Assert.Equal(NotificationCategory.Integrations, alert.Category);
            Assert.Equal("/crm/settings?tab=integrations", alert.DeepLink);
            Assert.Contains("needs reconnecting", alert.Title);
            Assert.Contains("Error validating access token.", alert.Message);
        }

        var admin = new NotificationUserContext { UserId = h.Leads.AdminUserId, Role = LeadRoles.Admin };
        var inbox = await h.Leads.Inbox.GetAsync(admin, new NotificationFilterDto { Category = NotificationCategory.Integrations });
        var mine = Assert.Single(inbox.Items);
        var opened = await h.Leads.Inbox.OpenAsync(mine.Id, admin);
        Assert.True(opened.Allowed);
        Assert.Equal("/crm/settings?tab=integrations", opened.DeepLink);
    }

    [Fact]
    public async Task AnIntegrationAlert_CannotBeAddressedToAManager()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, _) = await h.ConnectPageAsync();

        var queued = await h.Leads.Dispatcher.DispatchAsync(new NotificationRequest
        {
            Type = NotificationType.IntegrationAttentionRequired,
            RecipientUserId = h.Leads.ManagerUserId,
            DedupKey = "manager-integration-alert",
            EntityType = NotificationEntityType.IntegrationConnection,
            EntityId = connection.Id
        });

        Assert.False(queued);
        Assert.Empty(await IntegrationAlertsAsync(h));
    }

    [Fact]
    public async Task NeedingReconnectionAgainAfterAReconnect_IsANewAlert()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        await h.ConnectPageAsync();
        await MarkNeedsReauthorizationAsync(h, "Session has expired.");
        Assert.Equal(1, await h.Alerts.RaiseAlertsAsync());

        await ReconnectAsync(h);
        Assert.Equal(0, await h.Alerts.RaiseAlertsAsync());

        await MarkNeedsReauthorizationAsync(h, "The user has not authorized application.");
        Assert.Equal(1, await h.Alerts.RaiseAlertsAsync());
        Assert.Equal(2, (await IntegrationAlertsAsync(h)).Count);
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(-1, true)]
    [InlineData(10, false)]
    public async Task AnAccountTokenNearItsExpiry_AlertsOncePerToken(int daysLeft, bool expected)
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, _) = await h.ConnectPageAsync();
        connection.TokenExpiresAt = DateTime.UtcNow.AddDays(daysLeft);
        await h.Db.SaveChangesAsync();

        Assert.Equal(expected ? 1 : 0, await h.Alerts.RaiseAlertsAsync());
        Assert.Equal(0, await h.Alerts.RaiseAlertsAsync());

        if (expected)
        {
            var alert = Assert.Single(await IntegrationAlertsAsync(h));
            Assert.Contains(daysLeft < 0 ? "expired" : "expires soon", alert.Title);
        }
    }

    [Fact]
    public async Task ANewTokenNearingItsOwnExpiry_IsANewAlert()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, _) = await h.ConnectPageAsync();
        connection.TokenExpiresAt = DateTime.UtcNow.AddDays(2);
        await h.Db.SaveChangesAsync();
        Assert.Equal(1, await h.Alerts.RaiseAlertsAsync());

        connection.TokenExpiresAt = DateTime.UtcNow.AddDays(5);
        await h.Db.SaveChangesAsync();

        Assert.Equal(1, await h.Alerts.RaiseAlertsAsync());
    }

    [Fact]
    public async Task TheExpiryWarning_IsLeftToTheReconnectAlertAndCanBeSwitchedOff()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, _) = await h.ConnectPageAsync();
        connection.TokenExpiresAt = DateTime.UtcNow.AddDays(2);
        await h.Db.SaveChangesAsync();

        h.Options.TokenExpiryWarningDays = 0;
        Assert.Equal(0, await h.Alerts.RaiseAlertsAsync());

        h.Options.TokenExpiryWarningDays = 7;
        await MarkNeedsReauthorizationAsync(h, "Session has expired.");
        Assert.Equal(1, await h.Alerts.RaiseAlertsAsync());
        Assert.Contains("needs reconnecting", Assert.Single(await IntegrationAlertsAsync(h)).Title);
    }

    [Fact]
    public async Task FailedEvents_AlertOncePerConnectionPerDay()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await h.ConnectPageAsync();
        await AddEventAsync(h, connection, page, "lead-old", ExternalIntegrationEventStatus.Failed, DateTime.UtcNow.AddDays(-3));
        Assert.Equal(0, await h.Alerts.RaiseAlertsAsync());

        await AddEventAsync(h, connection, page, "lead-1", ExternalIntegrationEventStatus.Failed, DateTime.UtcNow);
        await AddEventAsync(h, connection, page, "lead-2", ExternalIntegrationEventStatus.Failed, DateTime.UtcNow);

        Assert.Equal(1, await h.Alerts.RaiseAlertsAsync());
        var alert = Assert.Single(await IntegrationAlertsAsync(h));
        Assert.Contains("2 lead events", alert.Message);

        await AddEventAsync(h, connection, page, "lead-3", ExternalIntegrationEventStatus.Failed, DateTime.UtcNow);
        Assert.Equal(0, await h.Alerts.RaiseAlertsAsync());
    }

    [Fact]
    public async Task AProcessorFailure_IsAlerted()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (_, page) = await h.ConnectPageAsync();
        h.Graph.LeadFailures.Enqueue(new MetaPermanentException("Lead does not exist."));
        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1"));
        await h.Processor.ProcessPendingEventsAsync(10);

        Assert.Equal(ExternalIntegrationEventStatus.Failed, (await h.Db.ExternalIntegrationEvents.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(1, await h.Alerts.RaiseAlertsAsync());
        Assert.Contains("failed", Assert.Single(await IntegrationAlertsAsync(h)).Title);
    }

    [Fact]
    public async Task ADisconnectedAccount_IsNeverAlertedAbout()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await h.ConnectPageAsync();
        await AddEventAsync(h, connection, page, "lead-1", ExternalIntegrationEventStatus.Failed, DateTime.UtcNow);
        connection.TokenExpiresAt = DateTime.UtcNow.AddDays(1);
        connection.Status = ExternalIntegrationConnectionStatus.Disconnected;
        await h.Db.SaveChangesAsync();

        Assert.Equal(0, await h.Alerts.RaiseAlertsAsync());
    }

    [Fact]
    public async Task AQuietPage_IsAlertedOnlyWhenConfiguredAndOnlyIfItEverDelivered()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (quietConnection, quietPage) = await h.ConnectPageAsync(pageId: "page-quiet", externalAccountId: "meta-user-a");
        var (busyConnection, busyPage) = await h.ConnectPageAsync(pageId: "page-busy", externalAccountId: "meta-user-b");
        await h.ConnectPageAsync(pageId: "page-new", externalAccountId: "meta-user-c");
        await AddEventAsync(h, quietConnection, quietPage, "lead-q", ExternalIntegrationEventStatus.Processed, DateTime.UtcNow.AddDays(-5));
        await AddEventAsync(h, busyConnection, busyPage, "lead-b", ExternalIntegrationEventStatus.Processed, DateTime.UtcNow.AddDays(-1));

        Assert.Equal(0, await h.Alerts.RaiseAlertsAsync());

        h.Options.QuietPageAlertDays = 3;
        Assert.Equal(1, await h.Alerts.RaiseAlertsAsync());
        var alert = Assert.Single(await IntegrationAlertsAsync(h));
        Assert.Equal(quietConnection.Id, alert.EntityId);
        Assert.Contains("Page page-quiet", alert.Title);
        Assert.Equal(0, await h.Alerts.RaiseAlertsAsync());
    }

    [Fact]
    public async Task OnlyAdminsAreOfferedTheIntegrationsCategory()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var policy = new Services.Notifications.NotificationEligibilityPolicy(h.Db);

        Assert.Contains(policy.CategoriesForRole(LeadRoles.Admin), c => c.Category == NotificationCategory.Integrations);
        Assert.DoesNotContain(policy.CategoriesForRole(LeadRoles.Manager), c => c.Category == NotificationCategory.Integrations);
        Assert.DoesNotContain(policy.CategoriesForRole(LeadRoles.Employee), c => c.Category == NotificationCategory.Integrations);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>The whole OAuth round trip, as the admin's browser drives it.</summary>
    private static async Task ReconnectAsync(MetaIntegrationHarness h)
    {
        var start = await h.Integration.StartConnectAsync(h.Leads.Admin, null);
        var state = new Uri(start.AuthorizationUrl).Query.TrimStart('?').Split('&')
            .Select(part => part.Split('=', 2))
            .Single(pair => pair[0] == "state")[1];

        var redirect = await h.Integration.CompleteCallbackAsync("code-1", Uri.UnescapeDataString(state), null);
        Assert.Contains("meta=connected", redirect);
        h.Db.ChangeTracker.Clear();
    }

    private static async Task MarkNeedsReauthorizationAsync(MetaIntegrationHarness h, string reason)
    {
        var connection = await h.Db.ExternalIntegrationConnections.SingleAsync();
        connection.Status = ExternalIntegrationConnectionStatus.NeedsReauthorization;
        connection.LastError = reason;
        connection.LastErrorAt = DateTime.UtcNow;
        await h.Db.SaveChangesAsync();
    }

    private static async Task AddEventAsync(
        MetaIntegrationHarness h,
        ExternalIntegrationConnection connection,
        ExternalIntegrationResource page,
        string leadgenId,
        ExternalIntegrationEventStatus status,
        DateTime at)
    {
        h.Db.ExternalIntegrationEvents.Add(new ExternalIntegrationEvent
        {
            Provider = IntegrationProviders.Meta,
            ExternalIntegrationConnectionId = connection.Id,
            ExternalIntegrationResourceId = page.Id,
            EventType = "leadgen",
            EventKey = $"{connection.Id}:{page.ExternalId}{MetaWebhookIntakeService.EventKeySuffix(leadgenId)}",
            ResourceExternalId = page.ExternalId,
            RawPayloadJson = "{}",
            ReceivedAt = at,
            ProcessedAt = at,
            AvailableAt = at,
            Status = status
        });
        await h.Db.SaveChangesAsync();
    }

    private static Task<List<Notification>> IntegrationAlertsAsync(MetaIntegrationHarness h) =>
        h.Db.Notifications.AsNoTracking()
            .Where(n => n.Type == NotificationType.IntegrationAttentionRequired)
            .ToListAsync();
}
