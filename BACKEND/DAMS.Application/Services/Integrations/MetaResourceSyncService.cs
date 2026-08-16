using DAMS.Application.Common;
using DAMS.Application.DTOs.IntegrationDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DAMS.Application.Services.Integrations
{
    /// <summary>
    /// Keeps the local picture of a Meta account's assets up to date, which is what makes a
    /// newly created ad or form appear in DAMS without anyone reconnecting anything.
    ///
    /// Sync only ever adds or updates. An asset Meta stops returning is marked inactive rather
    /// than deleted, because leads captured through it still point at it — and because a page
    /// that comes back should keep the enabled state an admin already chose for it.
    /// </summary>
    public sealed class MetaResourceSyncService : IMetaResourceSyncService
    {
        private static readonly string WorkerId = $"{Environment.MachineName}:{Environment.ProcessId}";

        private readonly AppDbContext _context;
        private readonly IMetaGraphClient _graph;
        private readonly IIntegrationSecretProtector _protector;
        private readonly MetaIntegrationOptions _options;
        private readonly ILogger<MetaResourceSyncService> _logger;

        public MetaResourceSyncService(
            AppDbContext context,
            IMetaGraphClient graph,
            IIntegrationSecretProtector protector,
            MetaIntegrationOptions options,
            ILogger<MetaResourceSyncService> logger)
        {
            _context = context;
            _graph = graph;
            _protector = protector;
            _options = options;
            _logger = logger;
        }

        public async Task<int> SyncDueConnectionsAsync(CancellationToken cancellationToken = default)
        {
            if (!_options.IsConfigured)
                return 0;

            var due = DateTime.UtcNow.AddSeconds(-Math.Max(60, _options.ResourceSyncIntervalSeconds));

            // A connection that has never synced is always due, which is how a freshly
            // authorised account gets discovered without doing slow work inside the callback.
            var connectionIds = await _context.ExternalIntegrationConnections
                .Where(c => c.Provider == IntegrationProviders.Meta
                            && c.Status == ExternalIntegrationConnectionStatus.Connected
                            && (c.LastSyncedAt == null || c.LastSyncedAt < due))
                .OrderBy(c => c.LastSyncedAt)
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);

            var synced = 0;
            foreach (var id in connectionIds)
            {
                // Several instances can run this same sweep at once and would otherwise all see
                // the same "due" connection and sync it concurrently. Only the worker that wins
                // this lease actually runs the sync; a loser simply moves on to the next
                // connection rather than duplicating the work.
                if (!await TryClaimForSyncAsync(id, cancellationToken))
                    continue;

                try
                {
                    await SyncConnectionAsync(id, cancellationToken);
                    synced++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One bad connection must not stop the others from syncing.
                    _logger.LogError(ex, "Syncing Meta connection {ConnectionId} failed. It will be retried.", id);
                }
                finally
                {
                    // Released immediately regardless of outcome — including failure — so a
                    // connection that just failed to sync is eligible again on the very next
                    // tick instead of waiting out the lease.
                    await ReleaseSyncLeaseAsync(id, cancellationToken);
                }
            }

            return synced;
        }

        /// <summary>
        /// Mirrors MetaLeadEventProcessor.ClaimEventsAsync's approach: a tracked update guarded
        /// by the row's own RowVersion, not a raw conditional UPDATE. Two workers loading the
        /// same connection before either commits will both try to set the lease, but only the
        /// first SaveChanges can possibly succeed — the second targets a RowVersion that no
        /// longer matches and is rejected by the database itself.
        /// </summary>
        private async Task<bool> TryClaimForSyncAsync(int connectionId, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;

            var connection = await _context.ExternalIntegrationConnections.FindAsync(
                [connectionId], cancellationToken);

            if (connection is null || connection.SyncLockedUntil > now)
                return false;

            connection.SyncLockedUntil = now.AddMinutes(_options.SyncLeaseMinutes);
            connection.SyncLockedBy = WorkerId;

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another worker's claim (or any other concurrent change to this row) landed
                // first; this attempt loses the race and moves on to the next connection.
                _context.Entry(connection).State = EntityState.Detached;
                return false;
            }
        }

        private async Task ReleaseSyncLeaseAsync(int connectionId, CancellationToken cancellationToken)
        {
            var connection = await _context.ExternalIntegrationConnections.FindAsync(
                [connectionId], cancellationToken);

            if (connection is null)
                return;

            connection.SyncLockedUntil = null;
            connection.SyncLockedBy = null;

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Whatever else changed this row since it was loaded already moved past the
                // lease this call was trying to clear; nothing left worth forcing through.
                _context.Entry(connection).State = EntityState.Detached;
            }
        }

        public async Task<MetaSyncResultDto> SyncConnectionAsync(
            int connectionId, CancellationToken cancellationToken = default)
        {
            var connection = await _context.ExternalIntegrationConnections
                .FirstOrDefaultAsync(
                    c => c.Id == connectionId && c.Provider == IntegrationProviders.Meta, cancellationToken)
                ?? throw new LeadNotFoundException("That Meta connection no longer exists.");

            if (connection.Status == ExternalIntegrationConnectionStatus.Disconnected)
                throw new InvalidOperationException("Reconnect this Meta account before syncing it.");

            var userToken = _protector.TryUnprotect(connection.AccessTokenProtected);
            if (userToken is null)
            {
                await MarkNeedsReauthorizationAsync(connection, "The stored credential could not be read.", cancellationToken);
                throw new InvalidOperationException("This Meta connection needs to be reconnected before it can sync.");
            }

            var existing = await _context.ExternalIntegrationResources
                .Where(r => r.ExternalIntegrationConnectionId == connectionId)
                .ToListAsync(cancellationToken);

            var discovered = new List<MetaDiscoveredResource>();
            string? warning = null;

            // Which asset kinds this run actually asked Meta for in full. Only these may be
            // deactivated when absent — derived from the calls made, never from what came
            // back, because "Meta returned no pages at all" is itself meaningful.
            var fullyEnumerated = new HashSet<string>();

            try
            {
                var pagePage = await _graph.GetPagesAsync(userToken, cancellationToken);
                discovered.AddRange(pagePage.Items);
                if (!pagePage.Truncated)
                {
                    fullyEnumerated.Add(ExternalResourceTypes.FacebookPage);

                    // A Page listing that fell back to no Instagram field expansion (missing
                    // instagram_basic) said nothing at all about Instagram accounts — it must
                    // not be read as "Meta has none", which would deactivate real ones already
                    // on file simply because this run could not ask about them.
                    if (pagePage.IncludesInstagramAccounts)
                    {
                        fullyEnumerated.Add(ExternalResourceTypes.InstagramAccount);
                    }
                    else
                    {
                        warning = Combine(warning,
                            "Instagram accounts could not be re-checked this sync because instagram_basic " +
                            "is not granted; any already on file were left unchanged.");
                    }
                }
                else
                {
                    warning = Combine(warning,
                        "This account has more Pages than could be read in one sync; some may not yet be listed.");
                }

                // Forms are read with the page's own token and only for pages an admin turned
                // on, so a client with fifty pages is not paged through fifty times over. They
                // are never added to fullyEnumerated regardless (see ApplyDiscovered), so
                // truncation here only needs to be surfaced, not tracked for deactivation.
                foreach (var page in pagePage.Items.Where(p => p.ResourceType == ExternalResourceTypes.FacebookPage))
                {
                    if (!IsEnabledLocally(existing, ExternalResourceTypes.FacebookPage, page.ExternalId))
                        continue;

                    var formToken = page.ResourceToken ?? userToken;
                    try
                    {
                        var formPage = await _graph.GetLeadFormsAsync(page.ExternalId, formToken, cancellationToken);
                        discovered.AddRange(formPage.Items);
                        if (formPage.Truncated)
                            warning = Combine(warning, $"Not every lead form for \"{page.Name ?? page.ExternalId}\" could be read.");
                    }
                    catch (MetaPermanentException ex)
                    {
                        warning = Combine(warning, $"Lead forms for \"{page.Name ?? page.ExternalId}\" could not be read.");
                        _logger.LogWarning(ex, "Reading lead forms for page {PageId} failed.", page.ExternalId);
                    }
                }
            }
            catch (MetaAuthorizationException ex)
            {
                // Page discovery needs only lead-critical scopes, so a rejection here means the
                // connection genuinely cannot work until someone reconnects.
                await MarkNeedsReauthorizationAsync(connection, ex.Message, cancellationToken);
                throw new InvalidOperationException(
                    "Meta rejected this connection. Reconnect the account and approve all requested permissions.");
            }

            // Ad account / campaign discovery needs ads_read, which commonly waits on Meta's
            // own App Review and is not required for a single lead to be captured. Its failure
            // must never discard the pages already found above or send the connection into
            // NeedsReauthorization — that would park real, working lead delivery over a
            // permission this integration does not need to function.
            try
            {
                var adAccountPage = await _graph.GetAdAccountsAsync(userToken, cancellationToken);
                discovered.AddRange(adAccountPage.Items);
                if (!adAccountPage.Truncated)
                {
                    fullyEnumerated.Add(ExternalResourceTypes.AdAccount);
                }
                else
                {
                    warning = Combine(warning,
                        "This account has more ad accounts than could be read in one sync; some may not yet be listed.");
                }

                var everyAdAccountRead = !adAccountPage.Truncated;
                foreach (var adAccount in adAccountPage.Items)
                {
                    try
                    {
                        var children = await _graph.GetAdAccountChildrenAsync(adAccount.ExternalId, userToken, cancellationToken);
                        discovered.AddRange(children.Items);
                        if (children.Truncated)
                            everyAdAccountRead = false;
                    }
                    catch (MetaPermanentException ex)
                    {
                        everyAdAccountRead = false;
                        warning = Combine(warning, $"Campaigns for \"{adAccount.Name ?? adAccount.ExternalId}\" could not be read.");
                        _logger.LogWarning(ex, "Reading children of ad account {AdAccountId} failed.", adAccount.ExternalId);
                    }
                }

                // Partial data must not deactivate anything: one ad account refusing access, or
                // any page of campaigns/ad sets/ads being truncated, would otherwise wipe out
                // resources that are still there and simply were not fully read this time.
                if (everyAdAccountRead)
                {
                    fullyEnumerated.Add(ExternalResourceTypes.Campaign);
                    fullyEnumerated.Add(ExternalResourceTypes.AdSet);
                    fullyEnumerated.Add(ExternalResourceTypes.Ad);
                }
            }
            catch (MetaAuthorizationException ex)
            {
                warning = Combine(warning,
                    "Ad account and campaign discovery is unavailable until Meta grants ads_read. Lead delivery is not affected.");
                _logger.LogWarning(ex, "Ad account discovery failed for Meta connection {ConnectionId}.", connection.Id);
            }

            var result = ApplyDiscovered(connection, existing, discovered, fullyEnumerated);

            // Self-healing for the window between a subscribe/unsubscribe call actually reaching
            // Meta and the local save that was supposed to record it — a crash, a lost
            // connection, or any other save failure in that gap leaves Meta and DAMS disagreeing
            // about whether a Page is really subscribed. Both Graph operations are idempotent, so
            // simply re-applying the enabled flag here brings Meta back in line without needing
            // to ask it what it currently thinks.
            var subscriptionWarning = await ReconcileSubscriptionsAsync(connection, existing, cancellationToken);
            if (subscriptionWarning is not null)
                warning = Combine(warning, subscriptionWarning);

            result.Warning = warning;

            connection.LastSyncedAt = DateTime.UtcNow;
            connection.LastValidatedAt = DateTime.UtcNow;
            connection.UpdatedAt = DateTime.UtcNow;
            if (connection.Status == ExternalIntegrationConnectionStatus.Error)
                connection.Status = ExternalIntegrationConnectionStatus.Connected;

            await _context.SaveChangesAsync(cancellationToken);

            result.SyncedAt = connection.LastSyncedAt.Value;
            return result;
        }

        private MetaSyncResultDto ApplyDiscovered(
            ExternalIntegrationConnection connection,
            List<ExternalIntegrationResource> existing,
            List<MetaDiscoveredResource> discovered,
            HashSet<string> fullyEnumerated)
        {
            var now = DateTime.UtcNow;
            var result = new MetaSyncResultDto();

            var byKey = existing.ToDictionary(r => (r.ResourceType, r.ExternalId));
            var seen = new HashSet<(string, string)>();

            foreach (var item in discovered)
            {
                var key = (item.ResourceType, item.ExternalId);
                seen.Add(key);

                if (byKey.TryGetValue(key, out var resource))
                {
                    resource.Name = item.Name ?? resource.Name;
                    resource.ExternalStatus = item.ExternalStatus ?? resource.ExternalStatus;
                    resource.ParentExternalId = item.ParentExternalId ?? resource.ParentExternalId;
                    resource.MetadataJson = item.MetadataJson ?? resource.MetadataJson;
                    resource.LastSeenAt = now;
                    resource.LastSyncedAt = now;
                    resource.UpdatedAt = now;
                    resource.IsActive = true;
                    // IsEnabled is never touched here: it is an operator's decision, not Meta's.

                    if (item.ResourceToken is { Length: > 0 })
                        resource.ResourceTokenProtected = _protector.Protect(item.ResourceToken);

                    result.Updated++;
                    continue;
                }

                var created = new ExternalIntegrationResource
                {
                    ExternalIntegrationConnectionId = connection.Id,
                    Provider = IntegrationProviders.Meta,
                    ResourceType = item.ResourceType,
                    ExternalId = item.ExternalId,
                    ParentExternalId = item.ParentExternalId,
                    Name = item.Name,
                    ExternalStatus = item.ExternalStatus,
                    MetadataJson = item.MetadataJson,
                    // Off until an admin says otherwise: discovering a page is not consent to
                    // start ingesting leads from it.
                    IsEnabled = false,
                    IsActive = true,
                    FirstSeenAt = now,
                    LastSeenAt = now,
                    LastSyncedAt = now,
                    ResourceTokenProtected = item.ResourceToken is { Length: > 0 }
                        ? _protector.Protect(item.ResourceToken)
                        : null
                };

                _context.ExternalIntegrationResources.Add(created);
                existing.Add(created);
                byKey[key] = created;
                result.Discovered++;
            }

            // Lead forms are never in this set: they are only read for enabled pages, so an
            // unseen form means "not looked at" rather than "gone", and deactivating those
            // would make them flap on every sync.
            foreach (var resource in existing)
            {
                if (!resource.IsActive
                    || seen.Contains((resource.ResourceType, resource.ExternalId))
                    || !fullyEnumerated.Contains(resource.ResourceType))
                    continue;

                resource.IsActive = false;
                resource.UpdatedAt = now;
                result.Deactivated++;
            }

            return result;
        }

        private async Task<string?> ReconcileSubscriptionsAsync(
            ExternalIntegrationConnection connection,
            List<ExternalIntegrationResource> resources,
            CancellationToken cancellationToken)
        {
            string? warning = null;

            foreach (var page in resources.Where(r =>
                r.IsActive
                && r.ResourceType == ExternalResourceTypes.FacebookPage
                && r.IsEnabled != r.IsSubscribed))
            {
                var token = _protector.TryUnprotect(page.ResourceTokenProtected)
                            ?? _protector.TryUnprotect(connection.AccessTokenProtected);
                if (token is null)
                    continue;

                try
                {
                    if (page.IsEnabled)
                        await _graph.SubscribePageAsync(page.ExternalId, token, cancellationToken);
                    else
                        await _graph.UnsubscribePageAsync(page.ExternalId, token, cancellationToken);

                    page.IsSubscribed = page.IsEnabled;
                    page.UpdatedAt = DateTime.UtcNow;
                }
                catch (MetaGraphException ex)
                {
                    warning = Combine(warning,
                        $"Could not reconcile the subscription for \"{page.Name ?? page.ExternalId}\"; it will be retried on the next sync.");
                    _logger.LogWarning(ex, "Reconciling Meta subscription for page {PageId} failed.", page.ExternalId);
                }
            }

            return warning;
        }

        public async Task<MetaSyncResultDto> SyncNowAsync(int connectionId, CancellationToken cancellationToken = default)
        {
            // An admin's "Sync now" and the background sweep must share one concurrency rule,
            // not two: without this lease, a due connection could be mid-sync from the
            // background worker at the exact moment an admin also triggers it by hand.
            if (!await TryClaimForSyncAsync(connectionId, cancellationToken))
                throw new InvalidOperationException(
                    "This connection is already syncing. Wait for it to finish and try again.");

            try
            {
                return await SyncConnectionAsync(connectionId, cancellationToken);
            }
            finally
            {
                await ReleaseSyncLeaseAsync(connectionId, cancellationToken);
            }
        }

        private static bool IsEnabledLocally(
            List<ExternalIntegrationResource> existing, string resourceType, string externalId) =>
            existing.Any(r => r.ResourceType == resourceType
                              && r.ExternalId == externalId
                              && r.IsEnabled);

        private async Task MarkNeedsReauthorizationAsync(
            ExternalIntegrationConnection connection, string reason, CancellationToken cancellationToken)
        {
            connection.Status = ExternalIntegrationConnectionStatus.NeedsReauthorization;
            connection.LastErrorAt = DateTime.UtcNow;
            connection.LastError = MetaCredentialScrubber.ScrubAndLimit(reason, 1000);
            connection.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }

        private static string Combine(string? existing, string addition) =>
            string.IsNullOrWhiteSpace(existing) ? addition : $"{existing} {addition}";
    }
}
