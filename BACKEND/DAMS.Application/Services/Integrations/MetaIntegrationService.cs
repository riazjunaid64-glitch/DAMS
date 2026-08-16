using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DAMS.Application.Common;
using DAMS.Application.DTOs.IntegrationDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DAMS.Application.Services.Integrations
{
    /// <summary>
    /// The admin side of the Meta integration: connecting, inspecting what was found,
    /// choosing which pages are live, and disconnecting.
    /// </summary>
    public sealed class MetaIntegrationService : IMetaIntegrationService
    {
        /// <summary>Fixed vocabulary for callback outcomes. A provider message is never echoed into a URL.</summary>
        private static class Reasons
        {
            public const string Denied = "denied";
            public const string InvalidState = "invalid_state";
            public const string ExchangeFailed = "exchange_failed";
            public const string NotConfigured = "not_configured";
        }

        private readonly AppDbContext _context;
        private readonly IMetaGraphClient _graph;
        private readonly IIntegrationSecretProtector _protector;
        private readonly IMetaResourceSyncService _sync;
        private readonly MetaIntegrationOptions _options;
        private readonly ILogger<MetaIntegrationService> _logger;

        public MetaIntegrationService(
            AppDbContext context,
            IMetaGraphClient graph,
            IIntegrationSecretProtector protector,
            IMetaResourceSyncService sync,
            MetaIntegrationOptions options,
            ILogger<MetaIntegrationService> logger)
        {
            _context = context;
            _graph = graph;
            _protector = protector;
            _sync = sync;
            _options = options;
            _logger = logger;
        }

        // ── Connecting ──────────────────────────────────────────────────────────────

        public async Task<MetaConnectStartDto> StartConnectAsync(
            LeadUserContext actor, string? returnPath, CancellationToken cancellationToken = default)
        {
            EnsureConfigured();

            // The raw state goes to Meta; only its hash is kept. A leaked database row
            // therefore cannot be replayed against the callback.
            var rawState = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');

            var expiresAt = DateTime.UtcNow.AddMinutes(_options.OAuthStateLifetimeMinutes);

            _context.ExternalIntegrationOAuthStates.Add(new ExternalIntegrationOAuthState
            {
                Provider = IntegrationProviders.Meta,
                StateHash = Hash(rawState),
                CreatedByUserId = actor.UserId,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt,
                ReturnPath = SafeReturnPath(returnPath)
            });

            await _context.SaveChangesAsync(cancellationToken);

            var url =
                $"https://www.facebook.com/{_options.GraphApiVersion}/dialog/oauth" +
                $"?client_id={Uri.EscapeDataString(_options.AppId!)}" +
                $"&redirect_uri={Uri.EscapeDataString(_options.OAuthCallbackUrl!)}" +
                $"&state={Uri.EscapeDataString(rawState)}" +
                $"&response_type=code" +
                $"&scope={Uri.EscapeDataString(MetaScopes.Joined)}";

            return new MetaConnectStartDto { AuthorizationUrl = url, ExpiresAt = expiresAt };
        }

        public async Task<string> CompleteCallbackAsync(
            string? code, string? state, string? error, CancellationToken cancellationToken = default)
        {
            if (!_options.IsConfigured)
                return Redirect(null, Reasons.NotConfigured);

            // State is validated and consumed before anything else, including a denial — Meta
            // echoes the same state back whether the admin approved or declined, and this is a
            // one-time token regardless of outcome. Checking it only on the success path would
            // leave a denial's state unconsumed until it simply expires, which is the wrong kind
            // of "one-time".
            if (string.IsNullOrWhiteSpace(state))
                return Redirect(null, Reasons.InvalidState);

            var stateRow = await ConsumeStateAsync(state, cancellationToken);
            if (stateRow is null)
                return Redirect(null, Reasons.InvalidState);

            // The admin declined consent at Meta. Not an error worth alarming anyone about.
            if (!string.IsNullOrWhiteSpace(error))
                return Redirect(stateRow.ReturnPath, Reasons.Denied);

            if (string.IsNullOrWhiteSpace(code))
                return Redirect(stateRow.ReturnPath, Reasons.InvalidState);

            try
            {
                var authorization = await _graph.CompleteAuthorizationAsync(code, cancellationToken);
                await UpsertConnectionAsync(authorization, stateRow.CreatedByUserId, cancellationToken);
                return Redirect(stateRow.ReturnPath, null);
            }
            catch (MetaGraphException ex)
            {
                // The scrubbed message is safe to log but still never travels to the browser;
                // the redirect carries only a fixed token.
                _logger.LogError(ex, "Completing the Meta authorization failed: {Reason}",
                    MetaCredentialScrubber.Scrub(ex.Message));
                return Redirect(stateRow.ReturnPath, Reasons.ExchangeFailed);
            }
        }

        /// <summary>
        /// Redeems a state exactly once. Two callbacks arriving together both find an unconsumed
        /// row, so the row version decides the winner and the loser is treated as a replay.
        /// </summary>
        private async Task<ExternalIntegrationOAuthState?> ConsumeStateAsync(
            string rawState, CancellationToken cancellationToken)
        {
            var hash = Hash(rawState);
            var now = DateTime.UtcNow;

            var stateRow = await _context.ExternalIntegrationOAuthStates
                .FirstOrDefaultAsync(s => s.StateHash == hash, cancellationToken);

            if (stateRow is null || stateRow.ConsumedAt != null || stateRow.ExpiresAt < now)
                return null;

            stateRow.ConsumedAt = now;

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return stateRow;
            }
            catch (DbUpdateConcurrencyException)
            {
                _context.Entry(stateRow).State = EntityState.Detached;
                return null;
            }
        }

        private async Task UpsertConnectionAsync(
            MetaAuthorizationResult authorization, int actingUserId, CancellationToken cancellationToken)
        {
            // Reconnecting the same Meta account must land on the existing row, so its pages
            // keep the enabled state an admin already chose and its historical leads keep
            // pointing somewhere meaningful.
            var connection = await _context.ExternalIntegrationConnections
                .FirstOrDefaultAsync(c => c.Provider == IntegrationProviders.Meta
                                          && c.ExternalAccountId == authorization.UserId, cancellationToken);

            var isNewConnection = connection is null;
            if (connection is null)
            {
                connection = new ExternalIntegrationConnection
                {
                    Provider = IntegrationProviders.Meta,
                    ExternalAccountId = authorization.UserId,
                    ConnectedAt = DateTime.UtcNow
                };
                _context.ExternalIntegrationConnections.Add(connection);
            }

            ApplyAuthorization(connection, authorization, actingUserId);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (isNewConnection && IsDuplicateConnection(ex))
            {
                // Two browsers completing OAuth for the same Meta account at nearly the same
                // moment both saw no existing row and both tried to insert one; the unique index
                // on (Provider, ExternalAccountId) is what actually decided. Reload the row the
                // winner created and apply this callback's result onto it, rather than losing
                // this callback's own authorization or surfacing a raw database error.
                _context.Entry(connection).State = EntityState.Detached;

                connection = await _context.ExternalIntegrationConnections
                    .FirstOrDefaultAsync(c => c.Provider == IntegrationProviders.Meta
                                              && c.ExternalAccountId == authorization.UserId, cancellationToken)
                    ?? throw new MetaPermanentException("Could not complete the Meta connection. Try again.");

                ApplyAuthorization(connection, authorization, actingUserId);
                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        private void ApplyAuthorization(
            ExternalIntegrationConnection connection, MetaAuthorizationResult authorization, int actingUserId)
        {
            var missingCriticalScopes = MetaScopes.LeadCritical
                .Where(scope => !authorization.GrantedScopes.Contains(scope, StringComparer.OrdinalIgnoreCase))
                .ToList();
            var missingOptionalScopes = MetaScopes.Optional
                .Where(scope => !authorization.GrantedScopes.Contains(scope, StringComparer.OrdinalIgnoreCase))
                .ToList();

            connection.DisplayName = authorization.DisplayName;
            connection.AccessTokenProtected = _protector.Protect(authorization.AccessToken);
            connection.TokenExpiresAt = authorization.ExpiresAt;
            connection.GrantedScopesJson = JsonSerializer.Serialize(authorization.GrantedScopes);
            connection.ConnectedByUserId = actingUserId;
            connection.ConnectedAt = DateTime.UtcNow;
            connection.LastValidatedAt = DateTime.UtcNow;
            connection.DisconnectedAt = null;
            connection.DisconnectedByUserId = null;
            connection.UpdatedAt = DateTime.UtcNow;
            // Left null on purpose: the worker treats "never synced" as "sync me now", so the
            // first discovery happens in the background rather than inside a browser redirect.
            connection.LastSyncedAt = null;

            if (missingCriticalScopes.Count > 0)
            {
                // Connected, but it cannot actually do its job. Saying so now is far kinder
                // than letting every lead retrieval fail silently later.
                connection.Status = ExternalIntegrationConnectionStatus.NeedsReauthorization;
                connection.LastErrorAt = DateTime.UtcNow;
                connection.LastError =
                    $"Meta did not grant: {string.Join(", ", missingCriticalScopes)}. Reconnect and approve all requested permissions.";
            }
            else
            {
                connection.Status = ExternalIntegrationConnectionStatus.Connected;
                // ads_read commonly waits on Meta's own App Review before it is granted. That
                // must not block a Page from delivering leads, but it is still worth surfacing
                // as a soft warning rather than pretending nothing is missing.
                if (missingOptionalScopes.Count > 0)
                {
                    connection.LastErrorAt = DateTime.UtcNow;
                    connection.LastError =
                        $"Connected, but ad account/campaign discovery is unavailable until Meta grants: " +
                        $"{string.Join(", ", missingOptionalScopes)}. Lead delivery is not affected.";
                }
                else
                {
                    connection.LastErrorAt = null;
                    connection.LastError = null;
                }
            }
        }

        // ── Reading ─────────────────────────────────────────────────────────────────

        public async Task<List<MetaConnectionDto>> GetConnectionsAsync(CancellationToken cancellationToken = default)
        {
            var connections = await _context.ExternalIntegrationConnections
                .AsNoTracking()
                .Where(c => c.Provider == IntegrationProviders.Meta)
                .OrderByDescending(c => c.ConnectedAt)
                .Select(c => new
                {
                    Connection = c,
                    ConnectedByName = _context.Users
                        .Where(u => u.UserId == c.ConnectedByUserId)
                        .Select(u => u.FullName)
                        .FirstOrDefault(),
                    Resources = _context.ExternalIntegrationResources
                        .Where(r => r.ExternalIntegrationConnectionId == c.Id)
                        .Select(r => new { r.ResourceType, r.IsEnabled, r.IsActive })
                        .ToList()
                })
                .ToListAsync(cancellationToken);

            return connections.Select(row => new MetaConnectionDto
            {
                Id = row.Connection.Id,
                Provider = row.Connection.Provider,
                DisplayName = row.Connection.DisplayName,
                Status = row.Connection.Status,
                ConnectedAt = row.Connection.ConnectedAt,
                ConnectedByName = row.ConnectedByName,
                LastSyncedAt = row.Connection.LastSyncedAt,
                LastErrorAt = row.Connection.LastErrorAt,
                LastError = row.Connection.LastError,
                TokenExpiresAt = row.Connection.TokenExpiresAt,
                GrantedScopes = ReadScopes(row.Connection.GrantedScopesJson),
                PageCount = row.Resources.Count(r => r.IsActive && r.ResourceType == ExternalResourceTypes.FacebookPage),
                InstagramCount = row.Resources.Count(r => r.IsActive && r.ResourceType == ExternalResourceTypes.InstagramAccount),
                AdAccountCount = row.Resources.Count(r => r.IsActive && r.ResourceType == ExternalResourceTypes.AdAccount),
                LeadFormCount = row.Resources.Count(r => r.IsActive && r.ResourceType == ExternalResourceTypes.LeadForm),
                EnabledResourceCount = row.Resources.Count(r => r.IsEnabled)
            }).ToList();
        }

        private static List<string> ReadScopes(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return [];

            try
            {
                return JsonSerializer.Deserialize<List<string>>(json) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }

        public async Task<List<MetaResourceGroupDto>> GetResourcesAsync(
            int connectionId, CancellationToken cancellationToken = default)
        {
            await EnsureConnectionExistsAsync(connectionId, cancellationToken);

            var resources = await _context.ExternalIntegrationResources
                .AsNoTracking()
                .Where(r => r.ExternalIntegrationConnectionId == connectionId)
                .OrderBy(r => r.Name)
                .Select(r => new MetaResourceDto
                {
                    Id = r.Id,
                    ResourceType = r.ResourceType,
                    ExternalId = r.ExternalId,
                    ParentExternalId = r.ParentExternalId,
                    Name = r.Name,
                    ExternalStatus = r.ExternalStatus,
                    IsEnabled = r.IsEnabled,
                    IsActive = r.IsActive,
                    IsSubscribed = r.IsSubscribed,
                    LastSeenAt = r.LastSeenAt
                })
                .ToListAsync(cancellationToken);

            // Fixed order and labels, so the screen reads the same whatever Meta returned.
            string[] order =
            [
                ExternalResourceTypes.FacebookPage,
                ExternalResourceTypes.InstagramAccount,
                ExternalResourceTypes.LeadForm,
                ExternalResourceTypes.AdAccount,
                ExternalResourceTypes.Campaign,
                ExternalResourceTypes.AdSet,
                ExternalResourceTypes.Ad
            ];

            return order
                .Select(type => new MetaResourceGroupDto
                {
                    ResourceType = type,
                    Label = Label(type),
                    Items = resources.Where(r => r.ResourceType == type).ToList()
                })
                .Where(group => group.Items.Count > 0)
                .ToList();
        }

        private static string Label(string resourceType) => resourceType switch
        {
            ExternalResourceTypes.FacebookPage => "Facebook Pages",
            ExternalResourceTypes.InstagramAccount => "Instagram accounts",
            ExternalResourceTypes.AdAccount => "Ad accounts",
            ExternalResourceTypes.Campaign => "Campaigns",
            ExternalResourceTypes.AdSet => "Ad sets",
            ExternalResourceTypes.Ad => "Ads",
            ExternalResourceTypes.LeadForm => "Lead forms",
            _ => resourceType
        };

        public async Task<List<MetaEventDto>> GetEventsAsync(
            int connectionId, int take, CancellationToken cancellationToken = default)
        {
            await EnsureConnectionExistsAsync(connectionId, cancellationToken);

            return await _context.ExternalIntegrationEvents
                .AsNoTracking()
                .Where(e => e.ExternalIntegrationConnectionId == connectionId)
                .OrderByDescending(e => e.ReceivedAt)
                .ThenByDescending(e => e.Id)
                .Take(Math.Clamp(take, 1, 200))
                .Select(e => new MetaEventDto
                {
                    Id = e.Id,
                    EventType = e.EventType,
                    Status = e.Status,
                    Attempts = e.Attempts,
                    ReceivedAt = e.ReceivedAt,
                    ProcessedAt = e.ProcessedAt,
                    LeadId = e.LeadId,
                    ResourceName = e.Resource != null ? e.Resource.Name : null,
                    LastError = e.LastError
                })
                .ToListAsync(cancellationToken);
        }

        // ── Enabling and disconnecting ──────────────────────────────────────────────

        public async Task<MetaResourceDto> SetResourceEnabledAsync(
            int connectionId, int resourceId, bool isEnabled, CancellationToken cancellationToken = default)
        {
            var connection = await LoadConnectionAsync(connectionId, cancellationToken);

            var resource = await _context.ExternalIntegrationResources
                .FirstOrDefaultAsync(r => r.Id == resourceId
                                          && r.ExternalIntegrationConnectionId == connectionId, cancellationToken)
                ?? throw new LeadNotFoundException("That resource does not belong to this connection.");

            if (isEnabled && connection.Status == ExternalIntegrationConnectionStatus.Disconnected)
                throw new InvalidOperationException("Reconnect this Meta account before enabling its resources.");

            if (isEnabled && !resource.IsActive)
                throw new InvalidOperationException(
                    "Meta no longer returns this resource. Run a sync first to confirm it still exists.");

            // A physical Facebook Page's webhook subscription is app-to-Page, not
            // connection-to-Page: two DAMS connections both claiming the same Page would let
            // disconnecting one silently unsubscribe leads for the other, since Meta only knows
            // one relationship exists. Ownership is therefore kept exclusive at the point a Page
            // is turned on, rather than left for the webhook to guess between candidates later.
            if (isEnabled && resource.ResourceType == ExternalResourceTypes.FacebookPage)
            {
                var alreadyOwnedElsewhere = await _context.ExternalIntegrationResources
                    .AnyAsync(r => r.Id != resource.Id
                                   && r.Provider == IntegrationProviders.Meta
                                   && r.ResourceType == ExternalResourceTypes.FacebookPage
                                   && r.ExternalId == resource.ExternalId
                                   && r.IsEnabled, cancellationToken);

                if (alreadyOwnedElsewhere)
                    throw new InvalidOperationException(
                        "This Facebook Page is already enabled through another DAMS connection. " +
                        "Disable it there first before enabling it here.");
            }

            // Enabling a page is what actually starts lead delivery, so the subscription call
            // has to succeed before the flag is trusted.
            if (resource.ResourceType == ExternalResourceTypes.FacebookPage)
                await ApplyPageSubscriptionAsync(connection, resource, isEnabled, cancellationToken);

            resource.IsEnabled = isEnabled;
            resource.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                // Meta has already applied the subscription change at this point — this is not
                // the "AnyAsync raced" case alone, it is any failure to persist it locally, and
                // both leave Meta and DAMS disagreeing about whether the page is subscribed
                // unless that call is undone. The periodic sync's own reconciliation step would
                // eventually catch this too, but there is no reason to leave a lead delivery gap
                // open until the next sync when the failure is known right here.
                _context.Entry(resource).State = EntityState.Detached;

                if (resource.ResourceType == ExternalResourceTypes.FacebookPage)
                    await RevertPageSubscriptionBestEffortAsync(connection, resource, isEnabled, cancellationToken);

                if (IsDuplicatePageOwnership(ex))
                    throw new InvalidOperationException(
                        "This Facebook Page is already enabled through another DAMS connection. " +
                        "Disable it there first before enabling it here.");

                throw;
            }

            return new MetaResourceDto
            {
                Id = resource.Id,
                ResourceType = resource.ResourceType,
                ExternalId = resource.ExternalId,
                ParentExternalId = resource.ParentExternalId,
                Name = resource.Name,
                ExternalStatus = resource.ExternalStatus,
                IsEnabled = resource.IsEnabled,
                IsActive = resource.IsActive,
                IsSubscribed = resource.IsSubscribed,
                LastSeenAt = resource.LastSeenAt
            };
        }

        private async Task ApplyPageSubscriptionAsync(
            ExternalIntegrationConnection connection,
            ExternalIntegrationResource page,
            bool isEnabled,
            CancellationToken cancellationToken)
        {
            var token = _protector.TryUnprotect(page.ResourceTokenProtected)
                        ?? _protector.TryUnprotect(connection.AccessTokenProtected);

            if (token is null)
            {
                await MarkNeedsReauthorizationAsync(connection, "The stored credential could not be read.", cancellationToken);
                throw new InvalidOperationException(
                    "This Meta connection needs to be reconnected before its pages can be changed.");
            }

            try
            {
                if (isEnabled)
                    await _graph.SubscribePageAsync(page.ExternalId, token, cancellationToken);
                else
                    await _graph.UnsubscribePageAsync(page.ExternalId, token, cancellationToken);

                page.IsSubscribed = isEnabled;
            }
            catch (MetaAuthorizationException ex)
            {
                await MarkNeedsReauthorizationAsync(connection, ex.Message, cancellationToken);
                throw new InvalidOperationException(
                    "Meta rejected the request. Reconnect this account and approve all requested permissions.");
            }
            catch (MetaGraphException ex)
            {
                _logger.LogError(ex, "Changing the Meta page subscription failed for page {PageId}.", page.ExternalId);
                throw new InvalidOperationException(
                    "Meta could not update this page's lead delivery just now. Try again shortly.");
            }
        }

        /// <summary>
        /// Undoes a subscribe/unsubscribe call that reached Meta but could not be saved locally,
        /// so the two do not silently drift apart. Best-effort: if this also fails, the mismatch
        /// is left for MetaResourceSyncService's own reconciliation to catch on the next
        /// periodic sync, rather than compounding one failure into an unhandled second one.
        /// </summary>
        private async Task RevertPageSubscriptionBestEffortAsync(
            ExternalIntegrationConnection connection,
            ExternalIntegrationResource page,
            bool appliedIsEnabled,
            CancellationToken cancellationToken)
        {
            var token = _protector.TryUnprotect(page.ResourceTokenProtected)
                        ?? _protector.TryUnprotect(connection.AccessTokenProtected);
            if (token is null)
                return;

            try
            {
                if (appliedIsEnabled)
                    await _graph.UnsubscribePageAsync(page.ExternalId, token, cancellationToken);
                else
                    await _graph.SubscribePageAsync(page.ExternalId, token, cancellationToken);
            }
            catch (MetaGraphException ex)
            {
                _logger.LogError(ex,
                    "Reverting the Meta subscription for page {PageId} after a database save failure also " +
                    "failed. Meta and DAMS may disagree about whether this page is subscribed until the next sync.",
                    page.ExternalId);
            }
        }

        public async Task DisconnectAsync(
            int connectionId, LeadUserContext actor, CancellationToken cancellationToken = default)
        {
            var connection = await LoadConnectionAsync(connectionId, cancellationToken);

            var resources = await _context.ExternalIntegrationResources
                .Where(r => r.ExternalIntegrationConnectionId == connectionId)
                .ToListAsync(cancellationToken);

            // Stop delivery at Meta's end where we still can, but never let a failure here block
            // the disconnect — the point of disconnecting is often that the token no longer works.
            foreach (var page in resources.Where(r => r.ResourceType == ExternalResourceTypes.FacebookPage && r.IsSubscribed))
            {
                var token = _protector.TryUnprotect(page.ResourceTokenProtected)
                            ?? _protector.TryUnprotect(connection.AccessTokenProtected);

                if (token is null)
                    continue;

                try
                {
                    await _graph.UnsubscribePageAsync(page.ExternalId, token, cancellationToken);
                }
                catch (MetaGraphException ex)
                {
                    _logger.LogWarning(ex,
                        "Could not unsubscribe Meta page {PageId} during disconnect. The local connection is still being disabled.",
                        page.ExternalId);
                }
            }

            foreach (var resource in resources)
            {
                resource.IsEnabled = false;
                resource.IsSubscribed = false;
                // Credentials go; the rows stay, because leads reference them for attribution.
                resource.ResourceTokenProtected = null;
                resource.ResourceTokenExpiresAt = null;
                resource.UpdatedAt = DateTime.UtcNow;
            }

            connection.Status = ExternalIntegrationConnectionStatus.Disconnected;
            connection.AccessTokenProtected = null;
            connection.TokenExpiresAt = null;
            connection.DisconnectedAt = DateTime.UtcNow;
            connection.DisconnectedByUserId = actor.UserId;
            connection.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);
        }

        private async Task MarkNeedsReauthorizationAsync(
            ExternalIntegrationConnection connection, string reason, CancellationToken cancellationToken)
        {
            connection.Status = ExternalIntegrationConnectionStatus.NeedsReauthorization;
            connection.LastErrorAt = DateTime.UtcNow;
            connection.LastError = MetaCredentialScrubber.ScrubAndLimit(reason, 1000);
            connection.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────

        private static bool IsDuplicatePageOwnership(DbUpdateException ex) =>
            ex.InnerException is SqlException { Number: 2601 or 2627 } sql
            && sql.Message.Contains("UX_ExternalIntegrationResources_EnabledFacebookPage", StringComparison.OrdinalIgnoreCase);

        private static bool IsDuplicateConnection(DbUpdateException ex) =>
            ex.InnerException is SqlException { Number: 2601 or 2627 } sql
            && sql.Message.Contains("IX_ExternalIntegrationConnections_Provider_ExternalAccountId", StringComparison.OrdinalIgnoreCase);

        private async Task<ExternalIntegrationConnection> LoadConnectionAsync(int id, CancellationToken cancellationToken) =>
            await _context.ExternalIntegrationConnections
                .FirstOrDefaultAsync(c => c.Id == id && c.Provider == IntegrationProviders.Meta, cancellationToken)
            ?? throw new LeadNotFoundException("That Meta connection no longer exists.");

        private async Task EnsureConnectionExistsAsync(int id, CancellationToken cancellationToken)
        {
            var exists = await _context.ExternalIntegrationConnections
                .AnyAsync(c => c.Id == id && c.Provider == IntegrationProviders.Meta, cancellationToken);

            if (!exists)
                throw new LeadNotFoundException("That Meta connection no longer exists.");
        }

        private void EnsureConfigured()
        {
            if (!_options.IsConfigured)
                throw new InvalidOperationException(
                    "The Meta integration is not configured. Supply MetaIntegration settings before connecting an account.");
        }

        private static string Hash(string value) =>
            Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

        /// <summary>Relative paths only. An absolute URL here would make the callback an open redirect.</summary>
        private static string? SafeReturnPath(string? returnPath) =>
            string.IsNullOrWhiteSpace(returnPath)
            || !returnPath.StartsWith('/')
            || returnPath.StartsWith("//", StringComparison.Ordinal)
                ? null
                : returnPath.Trim();

        private string Redirect(string? returnPath, string? failureReason)
        {
            // The callback that builds this URL always runs on the API host, never the SPA's.
            // returnPath is always relative (SafeReturnPath enforces this) and reflects exactly
            // where the admin started — it must be combined with the configured frontend origin,
            // not substituted for it outright, or a split-host deployment sends the browser back
            // to the API instead of the CRM.
            var target = !string.IsNullOrWhiteSpace(returnPath)
                ? CombineWithFrontendOrigin(returnPath)
                : string.IsNullOrWhiteSpace(_options.FrontendReturnUrl)
                    ? "/crm/settings"
                    : _options.FrontendReturnUrl;

            var separator = target.Contains('?') ? '&' : '?';

            return failureReason is null
                ? $"{target}{separator}meta=connected"
                : $"{target}{separator}meta=error&reason={failureReason}";
        }

        /// <summary>
        /// FrontendReturnUrl may be configured as either a bare origin or a full default path —
        /// only its scheme and host are trustworthy here, since the path itself belongs to
        /// returnPath, which reflects where this particular admin actually started from.
        /// </summary>
        private string CombineWithFrontendOrigin(string returnPath)
        {
            if (string.IsNullOrWhiteSpace(_options.FrontendReturnUrl)
                || !Uri.TryCreate(_options.FrontendReturnUrl, UriKind.Absolute, out var frontend))
                return returnPath;

            return $"{frontend.GetLeftPart(UriPartial.Authority)}{returnPath}";
        }
    }
}
