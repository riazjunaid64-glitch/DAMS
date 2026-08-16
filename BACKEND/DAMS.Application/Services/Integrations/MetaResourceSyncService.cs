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
            }

            return synced;
        }

        public async Task<MetaSyncResultDto> SyncConnectionAsync(
            int connectionId, CancellationToken cancellationToken = default)
        {
            var connection = await _context.ExternalIntegrationConnections
                .FirstOrDefaultAsync(c => c.Id == connectionId, cancellationToken)
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
                var pages = await _graph.GetPagesAsync(userToken, cancellationToken);
                discovered.AddRange(pages);
                fullyEnumerated.Add(ExternalResourceTypes.FacebookPage);
                fullyEnumerated.Add(ExternalResourceTypes.InstagramAccount);

                // Forms are read with the page's own token and only for pages an admin turned
                // on, so a client with fifty pages is not paged through fifty times over.
                foreach (var page in pages.Where(p => p.ResourceType == ExternalResourceTypes.FacebookPage))
                {
                    if (!IsEnabledLocally(existing, ExternalResourceTypes.FacebookPage, page.ExternalId))
                        continue;

                    var formToken = page.ResourceToken ?? userToken;
                    try
                    {
                        discovered.AddRange(await _graph.GetLeadFormsAsync(page.ExternalId, formToken, cancellationToken));
                    }
                    catch (MetaPermanentException ex)
                    {
                        warning = Combine(warning, $"Lead forms for \"{page.Name ?? page.ExternalId}\" could not be read.");
                        _logger.LogWarning(ex, "Reading lead forms for page {PageId} failed.", page.ExternalId);
                    }
                }

                var adAccounts = await _graph.GetAdAccountsAsync(userToken, cancellationToken);
                discovered.AddRange(adAccounts);
                fullyEnumerated.Add(ExternalResourceTypes.AdAccount);

                var everyAdAccountRead = true;
                foreach (var adAccount in adAccounts)
                {
                    try
                    {
                        discovered.AddRange(
                            await _graph.GetAdAccountChildrenAsync(adAccount.ExternalId, userToken, cancellationToken));
                    }
                    catch (MetaPermanentException ex)
                    {
                        everyAdAccountRead = false;
                        warning = Combine(warning, $"Campaigns for \"{adAccount.Name ?? adAccount.ExternalId}\" could not be read.");
                        _logger.LogWarning(ex, "Reading children of ad account {AdAccountId} failed.", adAccount.ExternalId);
                    }
                }

                // Partial data must not deactivate anything: one ad account refusing access
                // would otherwise wipe out every campaign belonging to the others.
                if (everyAdAccountRead)
                {
                    fullyEnumerated.Add(ExternalResourceTypes.Campaign);
                    fullyEnumerated.Add(ExternalResourceTypes.AdSet);
                    fullyEnumerated.Add(ExternalResourceTypes.Ad);
                }
            }
            catch (MetaAuthorizationException ex)
            {
                // Only an authorization failure aborts the whole sync: everything else is
                // partial data, but this means nothing will work until someone reconnects.
                await MarkNeedsReauthorizationAsync(connection, ex.Message, cancellationToken);
                throw new InvalidOperationException(
                    "Meta rejected this connection. Reconnect the account and approve all requested permissions.");
            }

            var result = ApplyDiscovered(connection, existing, discovered, fullyEnumerated);
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
