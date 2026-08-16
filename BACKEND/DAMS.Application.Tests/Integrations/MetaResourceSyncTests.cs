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
    public async Task AConnectionLockedByAnotherWorker_IsSkippedRatherThanSyncedTwice()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, _) = await h.ConnectPageAsync(pageId: "page-1");

        // Due, but another instance's sweep is already mid-sync on it.
        connection.LastSyncedAt = DateTime.UtcNow.AddDays(-1);
        connection.SyncLockedUntil = DateTime.UtcNow.AddMinutes(10);
        connection.SyncLockedBy = "another-worker:1";
        await h.Db.SaveChangesAsync();

        h.Graph.Pages = [Page("page-1", "Acme Sales")];

        Assert.Equal(0, await h.Sync.SyncDueConnectionsAsync());

        // Untouched: this worker never even attempted the sync, let alone the lease.
        var reloaded = await h.Db.ExternalIntegrationConnections.SingleAsync(c => c.Id == connection.Id);
        Assert.Equal("another-worker:1", reloaded.SyncLockedBy);
    }

    [Fact]
    public async Task ADueConnectionWithAnExpiredLease_IsStillSynced()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, _) = await h.ConnectPageAsync(pageId: "page-1");

        // The worker that took this lease is presumed dead; its lease has lapsed.
        connection.LastSyncedAt = DateTime.UtcNow.AddDays(-1);
        connection.SyncLockedUntil = DateTime.UtcNow.AddMinutes(-5);
        connection.SyncLockedBy = "dead-worker:9";
        await h.Db.SaveChangesAsync();

        h.Graph.Pages = [Page("page-1", "Acme Sales")];

        Assert.Equal(1, await h.Sync.SyncDueConnectionsAsync());

        var reloaded = await h.Db.ExternalIntegrationConnections.SingleAsync(c => c.Id == connection.Id);
        // Released again once this run finishes, not left claimed forever.
        Assert.Null(reloaded.SyncLockedUntil);
        Assert.Null(reloaded.SyncLockedBy);
    }

    [Fact]
    public async Task AFailedSync_ReleasesItsLeaseImmediately_SoTheNextTickCanRetry()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, _) = await h.ConnectPageAsync(pageId: "page-1");

        connection.LastSyncedAt = DateTime.UtcNow.AddDays(-1);
        await h.Db.SaveChangesAsync();

        h.Graph.DiscoveryFailure = new MetaTransientException("Meta could not be reached.");

        Assert.Equal(0, await h.Sync.SyncDueConnectionsAsync());

        var reloaded = await h.Db.ExternalIntegrationConnections.SingleAsync(c => c.Id == connection.Id);
        // A failure must not hold the lease for its full duration — the lease is a ceiling for
        // a wedged worker, not the normal wait before the next attempt.
        Assert.Null(reloaded.SyncLockedUntil);
        Assert.Null(reloaded.SyncLockedBy);
    }

    [Fact]
    public async Task ADivergedSubscribe_IsReconciledDuringTheNextSync()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await h.ConnectPageAsync(pageId: "page-1", enabled: true);

        // Simulates the local save that should have recorded a successful Meta subscribe call
        // having failed: Meta already thinks the page is subscribed, DAMS does not agree yet.
        page.IsSubscribed = false;
        await h.Db.SaveChangesAsync();

        h.Graph.Pages = [Page("page-1", "Acme Sales")];
        await h.Sync.SyncConnectionAsync(connection.Id);

        var reloaded = await h.Db.ExternalIntegrationResources.SingleAsync(r => r.Id == page.Id);
        Assert.True(reloaded.IsSubscribed);
        Assert.Contains("page-1", h.Graph.SubscribedPages);
    }

    [Fact]
    public async Task ADivergedUnsubscribe_IsAlsoReconciled()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await h.ConnectPageAsync(pageId: "page-1", enabled: false);

        // DAMS still thinks the page is subscribed even though it is disabled locally — the
        // mirror image of the previous test.
        page.IsSubscribed = true;
        await h.Db.SaveChangesAsync();

        h.Graph.Pages = [Page("page-1", "Acme Sales")];
        await h.Sync.SyncConnectionAsync(connection.Id);

        var reloaded = await h.Db.ExternalIntegrationResources.SingleAsync(r => r.Id == page.Id);
        Assert.False(reloaded.IsSubscribed);
        Assert.Contains("page-1", h.Graph.UnsubscribedPages);
    }

    [Fact]
    public async Task AStaleDisabledCopyOfAPage_NeverUnsubscribesAnotherConnectionsRealOwnership()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();

        // Two DAMS connections both hold a resource row for the same physical Facebook Page.
        // "winner" is the one actually enabled and subscribed; "loser" is disabled locally but
        // still carries a stale IsSubscribed=true from some earlier state (e.g. a compensation
        // that never landed). This is the same physical Page id on purpose.
        var (_, winnerPage) = await h.ConnectPageAsync(pageId: "page-1", externalAccountId: "meta-user-winner", enabled: true);
        var (_, loserPage) = await h.ConnectPageAsync(pageId: "page-1", externalAccountId: "meta-user-loser", enabled: false);
        loserPage.IsSubscribed = true;
        await h.Db.SaveChangesAsync();

        h.Graph.Pages = [Page("page-1", "Acme Sales")];

        // Syncing the LOSER's own connection must not unsubscribe a physical Page it no longer
        // owns just because its own bookkeeping is stale — the winner's real subscription
        // belongs to a connection this sync has no visibility into.
        var loserConnectionId = (await h.Db.ExternalIntegrationResources.SingleAsync(r => r.Id == loserPage.Id)).ExternalIntegrationConnectionId;
        await h.Sync.SyncConnectionAsync(loserConnectionId);

        Assert.DoesNotContain("page-1", h.Graph.UnsubscribedPages);

        var reloadedLoser = await h.Db.ExternalIntegrationResources.SingleAsync(r => r.Id == loserPage.Id);
        Assert.False(reloadedLoser.IsSubscribed);

        var reloadedWinner = await h.Db.ExternalIntegrationResources.SingleAsync(r => r.Id == winnerPage.Id);
        Assert.True(reloadedWinner.IsEnabled);
        Assert.True(reloadedWinner.IsSubscribed);
    }

    [Fact]
    public async Task AnInstagramAccount_StaysActive_WhenALaterSyncFallsBackWithoutInstagramExpansion()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, _) = await h.ConnectPageAsync(pageId: "page-1");

        h.Graph.Pages =
        [
            Page("page-1", "Acme Sales"),
            new()
            {
                ResourceType = ExternalResourceTypes.InstagramAccount,
                ExternalId = "ig-1",
                ParentExternalId = "page-1",
                Name = "acme.sales"
            }
        ];
        await h.Sync.SyncConnectionAsync(connection.Id);
        Assert.True((await h.Db.ExternalIntegrationResources.SingleAsync(r => r.ExternalId == "ig-1")).IsActive);

        // instagram_basic becomes unavailable; discovery falls back to Pages without Instagram
        // field expansion. This run says nothing about Instagram accounts at all — it must not
        // be read as "Meta no longer has this one", which is exactly the bug this fallback
        // introduced: a page listing with no truncation used to mark InstagramAccount as fully
        // enumerated regardless of whether Instagram was actually asked about.
        h.Graph.Pages = [Page("page-1", "Acme Sales")];
        h.Graph.PagesIncludeInstagramAccounts = false;
        var result = await h.Sync.SyncConnectionAsync(connection.Id);

        var stillThere = await h.Db.ExternalIntegrationResources.SingleAsync(r => r.ExternalId == "ig-1");
        Assert.True(stillThere.IsActive);
        Assert.Equal(0, result.Deactivated);
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public async Task SyncNow_RefusesToRunWhileTheConnectionIsAlreadySyncing()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, _) = await h.ConnectPageAsync(pageId: "page-1");

        // The background sweep is presumed mid-sync on this connection right now.
        connection.SyncLockedUntil = DateTime.UtcNow.AddMinutes(10);
        connection.SyncLockedBy = "background-worker:1";
        await h.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Sync.SyncNowAsync(connection.Id));
        Assert.Contains("already syncing", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SyncNow_ClaimsAndThenReleasesItsOwnLease()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, _) = await h.ConnectPageAsync(pageId: "page-1");

        h.Graph.Pages = [Page("page-1", "Acme Sales")];
        await h.Sync.SyncNowAsync(connection.Id);

        // Not left claimed — a background sweep or another admin's click must be free to run
        // immediately afterward.
        var reloaded = await h.Db.ExternalIntegrationConnections.SingleAsync(c => c.Id == connection.Id);
        Assert.Null(reloaded.SyncLockedUntil);
        Assert.Null(reloaded.SyncLockedBy);
    }

    [Fact]
    public async Task SyncNow_StillReleasesItsLease_WhenTheCallersTokenIsCancelledMidSync()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, _) = await h.ConnectPageAsync(pageId: "page-1");

        // Simulates a browser disconnecting (or the request being cancelled) after the lease was
        // already claimed but before the sync finished: the token SyncNowAsync was given becomes
        // cancelled while the sync is genuinely in flight, not before it starts.
        using var cts = new CancellationTokenSource();
        h.Graph.OnGetPages = () => cts.Cancel();
        h.Graph.DiscoveryFailure = new OperationCanceledException(cts.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(() => h.Sync.SyncNowAsync(connection.Id, cts.Token));

        Assert.True(cts.IsCancellationRequested);

        // Releasing the lease must not itself be cancelled by the same now-cancelled token —
        // otherwise it stays held until it simply expires on its own.
        var reloaded = await h.Db.ExternalIntegrationConnections.SingleAsync(c => c.Id == connection.Id);
        Assert.Null(reloaded.SyncLockedUntil);
        Assert.Null(reloaded.SyncLockedBy);
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
