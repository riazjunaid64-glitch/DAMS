using DAMS.Application.Common;
using DAMS.Application.Services.Integrations;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests.Integrations;

/// <summary>
/// Discovery is what makes a newly created ad or form appear without anyone reconnecting
/// Meta. It only ever adds or updates — an asset that stops being returned is deactivated,
/// never deleted, because leads still reference it.
/// </summary>
public class MetaResourceSyncTests
{
    private static MetaDiscoveredResource Page(string id, string name, string? token = "page-token") => new()
    {
        ResourceType = ExternalResourceTypes.FacebookPage,
        ExternalId = id,
        Name = name,
        ResourceToken = token
    };

    [Fact]
    public async Task SyncDiscoversPagesInstagramAccountsAndAdAccounts()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, _) = await h.ConnectPageAsync(pageId: "page-1");

        h.Graph.Pages =
        [
            Page("page-1", "Acme Sales"),
            Page("page-2", "Acme Rentals"),
            new()
            {
                ResourceType = ExternalResourceTypes.InstagramAccount,
                ExternalId = "ig-1",
                ParentExternalId = "page-1",
                Name = "acme.sales"
            }
        ];
        h.Graph.AdAccounts = [new() { ResourceType = ExternalResourceTypes.AdAccount, ExternalId = "act_1", Name = "Acme Ads" }];
        h.Graph.AdAccountChildren =
        [
            new() { ResourceType = ExternalResourceTypes.Campaign, ExternalId = "camp-1", ParentExternalId = "act_1", Name = "Summer" }
        ];

        var result = await h.Sync.SyncConnectionAsync(connection.Id);

        Assert.True(result.Discovered >= 4);

        var resources = await h.Db.ExternalIntegrationResources
            .Where(r => r.ExternalIntegrationConnectionId == connection.Id)
            .ToListAsync();

        Assert.Contains(resources, r => r.ResourceType == ExternalResourceTypes.InstagramAccount && r.ParentExternalId == "page-1");
        Assert.Contains(resources, r => r.ResourceType == ExternalResourceTypes.AdAccount && r.ExternalId == "act_1");
        Assert.Contains(resources, r => r.ResourceType == ExternalResourceTypes.Campaign);

        var refreshed = await h.Db.ExternalIntegrationConnections.SingleAsync(c => c.Id == connection.Id);
        Assert.NotNull(refreshed.LastSyncedAt);
    }

    [Fact]
    public async Task ANewlyCreatedLeadForm_AppearsOnTheNextSync()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await h.ConnectPageAsync(pageId: "page-1");

        h.Graph.Pages = [Page("page-1", "Acme Sales")];
        await h.Sync.SyncConnectionAsync(connection.Id);

        Assert.Empty(await h.Db.ExternalIntegrationResources
            .Where(r => r.ResourceType == ExternalResourceTypes.LeadForm)
            .ToListAsync());

        // The advertiser publishes a new form; nobody touches DAMS.
        h.Graph.LeadForms =
        [
            new()
            {
                ResourceType = ExternalResourceTypes.LeadForm,
                ExternalId = "form-9",
                ParentExternalId = page.ExternalId,
                Name = "Autumn enquiry"
            }
        ];

        await h.Sync.SyncConnectionAsync(connection.Id);

        var form = await h.Db.ExternalIntegrationResources
            .SingleAsync(r => r.ResourceType == ExternalResourceTypes.LeadForm);
        Assert.Equal("Autumn enquiry", form.Name);
    }

    [Fact]
    public async Task AVanishedPage_IsDeactivatedRatherThanDeleted_AndKeepsItsSettingWhenItReturns()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, _) = await h.ConnectPageAsync(pageId: "page-1", enabled: true);

        h.Graph.Pages = [Page("page-1", "Acme Sales")];
        await h.Sync.SyncConnectionAsync(connection.Id);

        // Meta stops returning it — a permission change, not a deletion.
        h.Graph.Pages = [];
        await h.Sync.SyncConnectionAsync(connection.Id);

        var page = await h.Db.ExternalIntegrationResources.SingleAsync(r => r.ExternalId == "page-1");
        Assert.False(page.IsActive);
        // Still present, because leads captured through it point here for attribution.
        Assert.True(page.IsEnabled);

        h.Graph.Pages = [Page("page-1", "Acme Sales")];
        await h.Sync.SyncConnectionAsync(connection.Id);

        var restored = await h.Db.ExternalIntegrationResources.SingleAsync(r => r.ExternalId == "page-1");
        Assert.True(restored.IsActive);
        Assert.True(restored.IsEnabled);
    }

    [Fact]
    public async Task SyncNeverTurnsAPageOnByItself()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, _) = await h.ConnectPageAsync(pageId: "page-1", enabled: false);

        h.Graph.Pages = [Page("page-1", "Acme Sales"), Page("page-2", "Acme Rentals")];
        await h.Sync.SyncConnectionAsync(connection.Id);

        // Discovering a page is not consent to ingest leads from it.
        var pages = await h.Db.ExternalIntegrationResources
            .Where(r => r.ResourceType == ExternalResourceTypes.FacebookPage)
            .ToListAsync();

        Assert.Equal(2, pages.Count);
        Assert.All(pages, p => Assert.False(p.IsEnabled));
    }

    [Fact]
    public async Task ARejectedTokenDuringSync_FlagsTheConnectionForReconnection()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, _) = await h.ConnectPageAsync();

        h.Graph.DiscoveryFailure = new MetaAuthorizationException("Session has expired.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Sync.SyncConnectionAsync(connection.Id));

        var updated = await h.Db.ExternalIntegrationConnections.SingleAsync(c => c.Id == connection.Id);
        Assert.Equal(ExternalIntegrationConnectionStatus.NeedsReauthorization, updated.Status);
    }

    [Fact]
    public async Task OnlyConnectionsThatAreDue_AreSynced()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (fresh, _) = await h.ConnectPageAsync(pageId: "page-1", externalAccountId: "meta-user-a");
        var (stale, _) = await h.ConnectPageAsync(pageId: "page-2", externalAccountId: "meta-user-b");

        // A never-synced connection is always due, which is how a freshly authorised account
        // gets discovered without doing slow work inside the OAuth callback.
        fresh.LastSyncedAt = DateTime.UtcNow;
        stale.LastSyncedAt = null;
        await h.Db.SaveChangesAsync();

        h.Graph.Pages = [Page("page-2", "Acme Rentals")];

        Assert.Equal(1, await h.Sync.SyncDueConnectionsAsync());

        var refreshedStale = await h.Db.ExternalIntegrationConnections.SingleAsync(c => c.Id == stale.Id);
        Assert.NotNull(refreshedStale.LastSyncedAt);
    }

    [Fact]
    public async Task AnAdAccountAuthorizationFailure_KeepsAlreadyDiscoveredPagesAndDoesNotFlagTheConnection()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, _) = await h.ConnectPageAsync(pageId: "page-1");

        h.Graph.Pages = [Page("page-1", "Acme Sales")];
        // ads_read is commonly missing until Meta grants Advanced Access. That must degrade
        // only ad-account discovery, never the Page a lead actually needs to arrive through.
        h.Graph.AdAccountDiscoveryFailure = new MetaAuthorizationException("ads_read is not granted.");

        var result = await h.Sync.SyncConnectionAsync(connection.Id);

        var page = await h.Db.ExternalIntegrationResources.SingleAsync(r => r.ExternalId == "page-1");
        Assert.True(page.IsActive);
        Assert.NotNull(result.Warning);

        var refreshed = await h.Db.ExternalIntegrationConnections.SingleAsync(c => c.Id == connection.Id);
        Assert.Equal(ExternalIntegrationConnectionStatus.Connected, refreshed.Status);
        Assert.NotNull(refreshed.LastSyncedAt);
    }

    [Fact]
    public async Task TruncatedPagination_DoesNotDeactivateResourcesThatWereSimplyNotReadThisTime()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, _) = await h.ConnectPageAsync(pageId: "page-1", enabled: true);

        h.Graph.Pages = [Page("page-1", "Acme Sales")];
        await h.Sync.SyncConnectionAsync(connection.Id);

        // The next sync's page walk is capped before it finishes — Meta returned more pages
        // than MaxGraphPages allowed following. "Not seen" here must not mean "gone": the walk
        // never got far enough to say that.
        h.Graph.Pages = [];
        h.Graph.PagesTruncated = true;
        var result = await h.Sync.SyncConnectionAsync(connection.Id);

        var page = await h.Db.ExternalIntegrationResources.SingleAsync(r => r.ExternalId == "page-1");
        Assert.True(page.IsActive);
        Assert.Equal(0, result.Deactivated);
    }

    [Fact]
    public async Task ResourcesOfTwoConnections_StayIsolated()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (first, _) = await h.ConnectPageAsync(pageId: "page-a", externalAccountId: "meta-user-a");
        var (second, _) = await h.ConnectPageAsync(pageId: "page-b", externalAccountId: "meta-user-b");

        h.Graph.Pages = [Page("page-a", "Client A")];
        await h.Sync.SyncConnectionAsync(first.Id);

        // Syncing one client's account must not deactivate another client's pages.
        var otherPage = await h.Db.ExternalIntegrationResources
            .SingleAsync(r => r.ExternalIntegrationConnectionId == second.Id && r.ExternalId == "page-b");

        Assert.True(otherPage.IsActive);
    }
}
