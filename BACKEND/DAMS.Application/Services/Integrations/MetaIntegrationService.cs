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
                        .ToList(),
                    PendingEventCount = _context.ExternalIntegrationEvents.Count(e =>
                        e.ExternalIntegrationConnectionId == c.Id
                        && (e.Status == ExternalIntegrationEventStatus.Pending
                            || e.Status == ExternalIntegrationEventStatus.Retry
                            || e.Status == ExternalIntegrationEventStatus.Processing)),
                    FailedEventCount = _context.ExternalIntegrationEvents.Count(e =>
                        e.ExternalIntegrationConnectionId == c.Id
                        && e.Status == ExternalIntegrationEventStatus.Failed)
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
                EnabledResourceCount = row.Resources.Count(r => r.IsEnabled),
                PendingEventCount = row.PendingEventCount,
                FailedEventCount = row.FailedEventCount
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
            int connectionId, int take, ExternalIntegrationEventStatus? status = null,
            CancellationToken cancellationToken = default)
        {
            await EnsureConnectionExistsAsync(connectionId, cancellationToken);

            var query = _context.ExternalIntegrationEvents
                .AsNoTracking()
                .Where(e => e.ExternalIntegrationConnectionId == connectionId);
            if (status.HasValue)
                query = query.Where(e => e.Status == status.Value);

            return await ProjectEvents(query
                .OrderByDescending(e => e.ReceivedAt)
                .ThenByDescending(e => e.Id)
                .Take(Math.Clamp(take, 1, 200)))
                .ToListAsync(cancellationToken);
        }

        private IQueryable<MetaEventDto> ProjectEvents(IQueryable<ExternalIntegrationEvent> query) => query.Select(e => new MetaEventDto
        {
            Id = e.Id,
            EventType = e.EventType,
            Status = e.Status,
            Attempts = e.Attempts,
            ReceivedAt = e.ReceivedAt,
            ProcessedAt = e.ProcessedAt,
            LeadId = e.LeadId,
            ResourceName = e.Resource != null ? e.Resource.Name : null,
            LastError = e.LastError,
            RetryCount = e.RetryCount,
            LastRetriedAt = e.LastRetriedAt,
            LastRetriedByName = e.LastRetriedByUserId.HasValue
                ? _context.Users
                    .Where(u => u.UserId == e.LastRetriedByUserId.Value)
                    .Select(u => u.FullName)
                    .FirstOrDefault()
                : null
        });

        public async Task<MetaEventDto> RetryEventAsync(
            int connectionId, int eventId, LeadUserContext actor, CancellationToken cancellationToken = default)
        {
            if (!actor.IsAdmin)
                throw new LeadAuthorizationException("Only an Admin can retry Meta integration events.");

            await EnsureConnectionExistsAsync(connectionId, cancellationToken);

            var integrationEvent = await _context.ExternalIntegrationEvents
                .Include(e => e.Connection)
                .Include(e => e.Resource)
                .FirstOrDefaultAsync(e => e.Id == eventId
                                          && e.Provider == IntegrationProviders.Meta
                                          && e.ExternalIntegrationConnectionId == connectionId,
                    cancellationToken)
                ?? throw new LeadNotFoundException("That Meta event does not belong to this connection.");

            // A retry request is deliberately idempotent. Once the first operator has moved the
            // event to the queue, later clicks must not reset attempts or create another audit.
            if (integrationEvent.Status == ExternalIntegrationEventStatus.Failed)
            {
                if (integrationEvent.Connection is null ||
                    integrationEvent.Connection.Status == ExternalIntegrationConnectionStatus.Disconnected)
                    throw new InvalidOperationException(
                        "Reconnect this Meta connection before retrying the event.");

                if (integrationEvent.Resource is null || !integrationEvent.Resource.IsActive)
                    throw new InvalidOperationException(
                        "Sync this Meta connection before retrying the event because its Page is no longer available.");

                if (!integrationEvent.Resource.IsEnabled)
                    throw new InvalidOperationException(
                        "Enable the Meta Page before retrying this event.");

                integrationEvent.Status = ExternalIntegrationEventStatus.Pending;
                integrationEvent.Attempts = 0;
                integrationEvent.AvailableAt = DateTime.UtcNow;
                integrationEvent.LockedUntil = null;
                integrationEvent.LockedBy = null;
                integrationEvent.ProcessedAt = null;
                integrationEvent.LastError = null;
                integrationEvent.RetryCount++;
                integrationEvent.LastRetriedAt = DateTime.UtcNow;
                integrationEvent.LastRetriedByUserId = actor.UserId;
                _context.ExternalIntegrationEventRetries.Add(new ExternalIntegrationEventRetry
                {
                    ExternalIntegrationEventId = integrationEvent.Id,
                    RequestedByUserId = actor.UserId,
                    RequestedAt = integrationEvent.LastRetriedAt.Value
                });

                try
                {
                    await _context.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Another Admin already requeued this event. Treat that race as the same
                    // idempotent outcome as a repeated click and return the committed state.
                    _context.ChangeTracker.Clear();
                }
            }

            var result = await ProjectEvents(_context.ExternalIntegrationEvents
                .AsNoTracking()
                .Where(e => e.Id == eventId))
                .SingleOrDefaultAsync(cancellationToken);

            return result ?? throw new LeadNotFoundException("That Meta event no longer exists.");
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
            //
            // Ownership is exactly what UX_ExternalIntegrationResources_EnabledFacebookPage says it
            // is: the one enabled copy of the Page, active or not. An enabled copy that Meta no
            // longer returns for its own connection (IsActive = false) cannot deliver leads there
            // (intake and the processor both require IsActive), so it is not a real claim — but it
            // still holds the index. It is therefore released in this same save rather than
            // ignored, otherwise the check below would pass, Meta would be subscribed, and the
            // save would still be refused by the index.
            var staleClaims = new List<ExternalIntegrationResource>();
            if (isEnabled && resource.ResourceType == ExternalResourceTypes.FacebookPage)
            {
                var enabledElsewhere = await _context.ExternalIntegrationResources
                    .Where(r => r.Id != resource.Id
                                && r.Provider == IntegrationProviders.Meta
                                && r.ResourceType == ExternalResourceTypes.FacebookPage
                                && r.ExternalId == resource.ExternalId
                                && r.IsEnabled)
                    .ToListAsync(cancellationToken);

                if (enabledElsewhere.Any(r => r.IsActive))
                    throw new InvalidOperationException(
                        "This Facebook Page is already enabled through another DAMS connection. " +
                        "Disable it there first before enabling it here.");

                staleClaims = enabledElsewhere;
            }

            var skipPageUnsubscribe = false;
            if (!isEnabled && resource.ResourceType == ExternalResourceTypes.FacebookPage)
            {
                // IsEnabled is the local ownership claim. A disabled copy has no authority to
                // change the app-to-Page subscription, especially when another connection owns it.
                var ownedElsewhere = await _context.ExternalIntegrationResources
                    .AnyAsync(r => r.Id != resource.Id
                                   && r.Provider == IntegrationProviders.Meta
                                   && r.ResourceType == ExternalResourceTypes.FacebookPage
                                   && r.ExternalId == resource.ExternalId
                                   && r.IsEnabled, cancellationToken);

                // A Disconnected connection has no credentials left (DisconnectAsync cleared them),
                // so it cannot reach Meta, and trying would mark it NeedsReauthorization and
                // undo the disconnect. A Page still flagged IsSubscribed there is the record of
                // an unsubscribe Meta refused during the disconnect; it is left as it is for the
                // sync after a reconnect to take down.
                var disconnected = connection.Status == ExternalIntegrationConnectionStatus.Disconnected;

                skipPageUnsubscribe = ownedElsewhere || disconnected
                                      || (!resource.IsEnabled && !resource.IsSubscribed);
                if (ownedElsewhere)
                    resource.IsSubscribed = false;
            }

            // Enabling a page, or disabling its current owner, is what changes lead delivery.
            // Already-disabled and non-owning copies only update their local bookkeeping.
            if (resource.ResourceType == ExternalResourceTypes.FacebookPage
                && (isEnabled || !skipPageUnsubscribe))
                await ApplyPageSubscriptionAsync(connection, resource, isEnabled, cancellationToken);

            // Released only once Meta has accepted this connection's Subscribe: a refusal above
            // saves the connection's NeedsReauthorization state, and must not save this with it.
            // The subscription is app-to-Page, so this connection's Subscribe is what now keeps
            // the Page delivering; the stale copy no longer holds it.
            foreach (var staleClaim in staleClaims)
            {
                staleClaim.IsEnabled = false;
                staleClaim.IsSubscribed = false;
                staleClaim.UpdatedAt = DateTime.UtcNow;
            }

            resource.IsEnabled = isEnabled;
            resource.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                _context.Entry(resource).State = EntityState.Detached;

                if (IsDuplicatePageOwnership(ex))
                {
                    // Another connection's save won this exact race and now legitimately owns
                    // the physical Page's subscription — the Subscribe call this attempt made
                    // was therefore redundant (subscribing an already-subscribed page is a
                    // no-op on Meta's side), and "reverting" it now would unsubscribe the
                    // winner's real, current subscription, not this attempt's. Nothing this
                    // attempt did to Meta needs undoing; only the local claim does, and that
                    // was never persisted.
                    throw new InvalidOperationException(
                        "This Facebook Page is already enabled through another DAMS connection. " +
                        "Disable it there first before enabling it here.");
                }

                // Any other save failure: Meta has already been told about a change this save
                // failed to record, so the two are now out of step and something has to put them
                // back. What Meta should be left holding is deliberately not assumed to be
                // "whatever this attempt started from" — see the helper for why undoing this
                // attempt's own call is the wrong repair.
                if (resource.ResourceType == ExternalResourceTypes.FacebookPage)
                    await RestorePageSubscriptionToCommittedStateAsync(connection, resource, cancellationToken);

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
        /// Puts Meta back in step with what the database actually holds for a physical Page after
        /// a save that failed.
        ///
        /// Blindly undoing what this attempt did to Meta is wrong precisely when the save failed
        /// because somebody else's change won. Two admins toggling the same Page at once both
        /// load the same rowversion, both call Meta, and the loser's SaveChanges is rejected by
        /// the database — so "reverting" the loser's own call would tear down the state the
        /// winner just committed, leaving DAMS claiming a subscription Meta no longer has and
        /// losing every lead until the next sync. The same is true in reverse for two concurrent
        /// disables. The desired state is therefore read back from what actually committed, and
        /// read page-globally, because a Page's subscription is app-to-Page rather than
        /// connection-to-Page.
        ///
        /// Best-effort by design: this runs inside a catch that is about to rethrow, so nothing
        /// here may replace the original failure. Whatever is left unreconciled is picked up by
        /// MetaResourceSyncService's periodic reconciliation.
        /// </summary>
        private async Task RestorePageSubscriptionToCommittedStateAsync(
            ExternalIntegrationConnection connection,
            ExternalIntegrationResource page,
            CancellationToken cancellationToken)
        {
            try
            {
                // AsNoTracking on purpose: the tracked graph still carries the change this save
                // could not commit, and identity resolution would hand back exactly the state
                // that must not be trusted here.
                var shouldBeSubscribed = await _context.ExternalIntegrationResources
                    .AsNoTracking()
                    .AnyAsync(r => r.Provider == IntegrationProviders.Meta
                                   && r.ResourceType == ExternalResourceTypes.FacebookPage
                                   && r.ExternalId == page.ExternalId
                                   && r.IsActive
                                   && r.IsEnabled, cancellationToken);

                var token = _protector.TryUnprotect(page.ResourceTokenProtected)
                            ?? _protector.TryUnprotect(connection.AccessTokenProtected);
                if (token is null)
                    return;

                if (shouldBeSubscribed)
                    await _graph.SubscribePageAsync(page.ExternalId, token, cancellationToken);
                else
                    await _graph.UnsubscribePageAsync(page.ExternalId, token, cancellationToken);
            }
            catch (Exception ex)
            {
                // Intentionally broad, cancellation included: the caller is mid-catch and about
                // to rethrow the real failure, which must not be swapped out for a secondary one
                // raised while cleaning up after it.
                _logger.LogError(ex,
                    "Could not put the Meta subscription for page {PageId} back in step with the database " +
                    "after a failed save. Meta and DAMS may disagree about this page until the next sync.",
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

            // Rows whose subscription this disconnect could not actually take down at Meta, so
            // the local flag is not cleared as though it had.
            var stillSubscribedRemotely = new HashSet<int>();
            var unresolved = new List<string>();

            // Stop delivery at Meta's end where we still can, but never let a failure here block
            // the disconnect — the point of disconnecting is often that the token no longer works.
            foreach (var page in resources.Where(r => r.ResourceType == ExternalResourceTypes.FacebookPage && r.IsSubscribed))
            {
                // A Page's webhook subscription is app-to-Page, not connection-to-Page. If
                // another DAMS connection currently owns this exact physical Page, unsubscribing
                // it here would silently stop that connection's lead delivery — which is the one
                // thing disconnecting an unrelated account must never do. This row is simply no
                // longer subscribed as far as this connection is concerned.
                var ownedElsewhere = await _context.ExternalIntegrationResources
                    .AnyAsync(r => r.Id != page.Id
                                   && r.Provider == IntegrationProviders.Meta
                                   && r.ResourceType == ExternalResourceTypes.FacebookPage
                                   && r.ExternalId == page.ExternalId
                                   && r.IsActive
                                   && r.IsEnabled, cancellationToken);

                if (ownedElsewhere)
                    continue;

                var token = _protector.TryUnprotect(page.ResourceTokenProtected)
                            ?? _protector.TryUnprotect(connection.AccessTokenProtected);

                if (token is null)
                {
                    stillSubscribedRemotely.Add(page.Id);
                    unresolved.Add(page.Name ?? page.ExternalId);
                    continue;
                }

                try
                {
                    await _graph.UnsubscribePageAsync(page.ExternalId, token, cancellationToken);
                }
                catch (MetaGraphException ex)
                {
                    stillSubscribedRemotely.Add(page.Id);
                    unresolved.Add(page.Name ?? page.ExternalId);
                    _logger.LogWarning(ex,
                        "Could not unsubscribe Meta page {PageId} during disconnect. The local connection is still being disabled.",
                        page.ExternalId);
                }
            }

            foreach (var resource in resources)
            {
                resource.IsEnabled = false;
                // Kept truthful rather than tidy: a Page whose unsubscribe never reached Meta is
                // still subscribed there, and clearing the flag would erase the only local record
                // of it. Reconnecting this account is what lets the next sync's reconciliation
                // finally take it down, since the credentials to do so are cleared below.
                resource.IsSubscribed = stillSubscribedRemotely.Contains(resource.Id);
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

            if (unresolved.Count > 0)
            {
                // The credentials needed to retry are gone by design, so this is the only place
                // an admin can learn that Meta may still be delivering for these Pages. Said
                // plainly, with both ways out of it, rather than buried in a server log.
                connection.LastErrorAt = DateTime.UtcNow;
                connection.LastError = MetaCredentialScrubber.ScrubAndLimit(
                    "Disconnected, but Meta could not be told to stop sending leads for: " +
                    $"{string.Join(", ", unresolved)}. Reconnect this account to let the next sync " +
                    "retry, or remove this app from those Pages in Meta's own settings.", 1000);
            }

            await _context.SaveChangesAsync(cancellationToken);
        }

        private async Task MarkNeedsReauthorizationAsync(
            ExternalIntegrationConnection connection, string reason, CancellationToken cancellationToken)
        {
            // Disconnecting is a deliberate, final choice: only a reconnect may bring the
            // connection back, never a failed call that happened to run against it afterwards.
            if (connection.Status == ExternalIntegrationConnectionStatus.Disconnected)
                return;

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
