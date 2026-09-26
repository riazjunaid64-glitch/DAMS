using System.Globalization;
using DAMS.Application.Common;
using DAMS.Application.Interfaces;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services.Integrations
{
    /// <summary>
    /// Tells every Admin when Meta lead capture needs a person, before anyone happens to open the
    /// Integrations panel.
    ///
    /// A sweep over current state rather than a hook at each transition: a connection reaches
    /// NeedsReauthorization from several places, an expiring sign-in and a quiet Page have no
    /// transition at all, and a sweep also recovers an alert lost between a state change and its
    /// notification. Every alert's dedup key names the connection, the condition and the episode
    /// it belongs to, so however often this runs each Admin hears about an episode once, and a new
    /// one — a later reconnect, a new token, another day's failures — is told about again.
    /// </summary>
    public sealed class MetaIntegrationAlertService : IMetaIntegrationAlertService
    {
        private const string Settings = "CRM settings > Integrations";

        private readonly AppDbContext _context;
        private readonly INotificationDispatcher _notifications;
        private readonly MetaIntegrationOptions _options;

        public MetaIntegrationAlertService(
            AppDbContext context, INotificationDispatcher notifications, MetaIntegrationOptions options)
        {
            _context = context;
            _notifications = notifications;
            _options = options;
        }

        private sealed record ConnectionState(
            int Id,
            string Name,
            ExternalIntegrationConnectionStatus Status,
            DateTime ConnectedAt,
            DateTime? TokenExpiresAt,
            string? LastError);

        /// <summary>One condition on one connection. <see cref="Episode"/> keeps a repeat of it apart from this one.</summary>
        private sealed record Alert(int ConnectionId, string ConnectionName, string Episode, string Title, string Message);

        public async Task<int> RaiseAlertsAsync(CancellationToken cancellationToken = default)
        {
            // A disconnected account is somebody's deliberate choice, not a fault to report.
            var connections = await _context.ExternalIntegrationConnections
                .AsNoTracking()
                .Where(c => c.Provider == IntegrationProviders.Meta
                            && c.Status != ExternalIntegrationConnectionStatus.Disconnected)
                .Select(c => new ConnectionState(c.Id, c.DisplayName, c.Status, c.ConnectedAt, c.TokenExpiresAt, c.LastError))
                .ToListAsync(cancellationToken);

            if (connections.Count == 0)
                return 0;

            var now = DateTime.UtcNow;
            var alerts = new List<Alert>();

            foreach (var connection in connections)
            {
                if (connection.Status == ExternalIntegrationConnectionStatus.NeedsReauthorization)
                    alerts.Add(NeedsReconnect(connection));
                // Reconnecting is already asked for above, and replaces the token anyway.
                else if (TokenExpiryAlert(connection, now) is { } expiry)
                    alerts.Add(expiry);
            }

            alerts.AddRange(await FailedEventAlertsAsync(connections, now, cancellationToken));
            alerts.AddRange(await QuietPageAlertsAsync(connections, now, cancellationToken));

            if (alerts.Count == 0)
                return 0;

            var adminIds = await ActiveAdminIdsAsync(_context, cancellationToken);
            var requests = alerts
                .SelectMany(alert => adminIds.Select(userId => Request(alert, userId)))
                .ToList();

            // One read instead of one per alert per Admin on every sweep while a problem stays open.
            var keys = requests.Select(r => r.DedupKey).ToList();
            var raised = (await _context.Notifications
                    .AsNoTracking()
                    .Where(n => keys.Contains(n.DedupKey))
                    .Select(n => n.DedupKey)
                    .ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);

            var created = 0;
            foreach (var request in requests.Where(r => !raised.Contains(r.DedupKey)))
            {
                // Saved one by one: another instance running this same sweep makes a duplicate a
                // normal outcome, which DispatchAsync absorbs for its own row only.
                if (await _notifications.DispatchAsync(request, cancellationToken))
                    created++;
            }

            return created;
        }

        /// <summary>Every Admin who can still sign in — the people who can reconnect an account.</summary>
        internal static Task<List<int>> ActiveAdminIdsAsync(AppDbContext context, CancellationToken cancellationToken) =>
            context.Users
                .AsNoTracking()
                .Where(u => u.Role.Role_name == LeadRoles.Admin
                            && u.AccountStatus == UserAccountStatus.Active)
                .Select(u => u.UserId)
                .ToListAsync(cancellationToken);

        // ── Conditions ──────────────────────────────────────────────────────────────

        /// <summary>
        /// A connection only leaves NeedsReauthorization by being reconnected, and reconnecting
        /// stamps ConnectedAt — so that is what tells one episode from the next.
        /// </summary>
        private static Alert NeedsReconnect(ConnectionState connection)
        {
            var reason = LeadContactNormalizer.Clean(connection.LastError);
            return new Alert(
                connection.Id,
                connection.Name,
                $"NeedsReconnect:{connection.ConnectedAt.Ticks}",
                $"Meta connection needs reconnecting: {connection.Name}",
                $"Leads from {connection.Name} are waiting and will not reach the CRM until the account is " +
                $"reconnected in {Settings}. None are lost: waiting leads are fetched as soon as it is." +
                (reason is null ? string.Empty : $" Reason: {LeadContactNormalizer.Limit(reason, 500)}"));
        }

        /// <summary>A new sign-in brings a new expiry, which is what makes it a new episode.</summary>
        private Alert? TokenExpiryAlert(ConnectionState connection, DateTime now)
        {
            if (_options.TokenExpiryWarningDays <= 0
                || connection.TokenExpiresAt is not { } expiresAt
                || expiresAt > now.AddDays(_options.TokenExpiryWarningDays))
                return null;

            var date = FormatDate(expiresAt);
            var expired = expiresAt <= now;

            return new Alert(
                connection.Id,
                connection.Name,
                $"TokenExpiry:{expiresAt.Ticks}",
                expired
                    ? $"Meta sign-in expired: {connection.Name}"
                    : $"Meta sign-in expires soon: {connection.Name}",
                expired
                    ? $"The Meta sign-in for {connection.Name} expired on {date}. Leads are still fetched with the " +
                      $"Page tokens, but DAMS cannot find new Pages or lead forms until the account is reconnected in {Settings}."
                    : $"The Meta sign-in for {connection.Name} expires on {date}. Reconnect the account in {Settings} " +
                      "before then so its Pages and lead forms keep syncing.");
        }

        /// <summary>
        /// One alert per connection per business day with failures. A failure nobody retried stays
        /// Failed for good, so an episode keyed on the failures still open would never end — and
        /// would swallow every later one. The day keeps new failures audible without an alert per
        /// event in a burst. Yesterday is read too, so failures just before midnight are not missed.
        /// </summary>
        private async Task<List<Alert>> FailedEventAlertsAsync(
            List<ConnectionState> connections, DateTime now, CancellationToken cancellationToken)
        {
            var since = PakistanTime.StartOfBusinessDateUtc(PakistanTime.ToBusinessDate(now).AddDays(-1));
            var connectionIds = connections.Select(c => c.Id).ToList();

            var failures = await _context.ExternalIntegrationEvents
                .AsNoTracking()
                .Where(e => e.Provider == IntegrationProviders.Meta
                            && e.Status == ExternalIntegrationEventStatus.Failed
                            && e.ExternalIntegrationConnectionId != null
                            && connectionIds.Contains(e.ExternalIntegrationConnectionId.Value)
                            && e.ProcessedAt >= since)
                .Select(e => new { ConnectionId = e.ExternalIntegrationConnectionId!.Value, ProcessedAt = e.ProcessedAt!.Value })
                .ToListAsync(cancellationToken);

            var byId = connections.ToDictionary(c => c.Id);

            return failures
                .GroupBy(f => (f.ConnectionId, Day: PakistanTime.ToBusinessDate(f.ProcessedAt)))
                .Select(group =>
                {
                    var name = byId[group.Key.ConnectionId].Name;
                    var count = group.Count();
                    return new Alert(
                        group.Key.ConnectionId,
                        name,
                        $"EventsFailed:{group.Key.Day.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}",
                        $"Meta lead events failed: {name}",
                        $"{count} lead event{(count == 1 ? "" : "s")} from {name} could not be turned into " +
                        $"{(count == 1 ? "a lead" : "leads")} on {FormatBusinessDate(group.Key.Day)}. " +
                        $"Open {Settings} to see why and retry {(count == 1 ? "it" : "them")}.");
                })
                .ToList();
        }

        /// <summary>
        /// A Page that delivered leads before and has since gone silent for the configured number
        /// of days, which is how a webhook subscription Meta dropped shows up. A Page that never
        /// delivered anything is left out: nothing tells "no campaign yet" from "broken" there.
        /// The newest webhook is the episode, so the next quiet spell after a lead is a new one.
        /// </summary>
        private async Task<List<Alert>> QuietPageAlertsAsync(
            List<ConnectionState> connections, DateTime now, CancellationToken cancellationToken)
        {
            if (_options.QuietPageAlertDays <= 0)
                return [];

            // A connection waiting to be reconnected is already being alerted about.
            var connected = connections
                .Where(c => c.Status == ExternalIntegrationConnectionStatus.Connected)
                .ToDictionary(c => c.Id);
            if (connected.Count == 0)
                return [];

            var connectionIds = connected.Keys.ToList();
            var pages = await _context.ExternalIntegrationResources
                .AsNoTracking()
                .Where(r => connectionIds.Contains(r.ExternalIntegrationConnectionId)
                            && r.ResourceType == ExternalResourceTypes.FacebookPage
                            && r.IsEnabled
                            && r.IsActive)
                .Select(r => new { r.Id, r.ExternalIntegrationConnectionId, r.Name, r.ExternalId })
                .ToListAsync(cancellationToken);
            if (pages.Count == 0)
                return [];

            var pageIds = pages.Select(p => p.Id).ToList();
            var lastReceived = await _context.ExternalIntegrationEvents
                .AsNoTracking()
                // Webhook deliveries only: leads recovered by reconciliation are exactly what a
                // dropped subscription produces, and must not make it look healthy.
                .Where(e => e.ExternalIntegrationResourceId != null
                            && pageIds.Contains(e.ExternalIntegrationResourceId.Value)
                            && e.EventType != MetaLeadBackfillService.BackfillEventType)
                .GroupBy(e => e.ExternalIntegrationResourceId!.Value)
                .Select(g => new { PageId = g.Key, LastReceivedAt = g.Max(e => e.ReceivedAt) })
                .ToDictionaryAsync(x => x.PageId, x => x.LastReceivedAt, cancellationToken);

            var quietSince = now.AddDays(-_options.QuietPageAlertDays);
            var alerts = new List<Alert>();

            foreach (var page in pages)
            {
                if (!lastReceived.TryGetValue(page.Id, out var last) || last >= quietSince)
                    continue;

                var connection = connected[page.ExternalIntegrationConnectionId];
                var pageName = page.Name ?? page.ExternalId;
                alerts.Add(new Alert(
                    connection.Id,
                    connection.Name,
                    $"QuietPage:{page.Id}:{last.Ticks}",
                    $"No Meta leads for {_options.QuietPageAlertDays} days: {pageName}",
                    $"The Page {pageName} on {connection.Name} is enabled, but no lead has arrived from it since " +
                    $"{FormatDate(last)}. If its ads are running, check its lead delivery in {Settings}."));
            }

            return alerts;
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────

        private static NotificationRequest Request(Alert alert, int userId) => new()
        {
            Type = NotificationType.IntegrationAttentionRequired,
            RecipientUserId = userId,
            DedupKey = $"MetaIntegration:{alert.ConnectionId}:{alert.Episode}:{userId}",
            Title = alert.Title,
            Message = alert.Message,
            EntityType = NotificationEntityType.IntegrationConnection,
            EntityId = alert.ConnectionId,
            Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["connectionName"] = alert.ConnectionName
            }
        };

        /// <summary>Dates are read by people in Pakistan, so a UTC instant is shown as their business date.</summary>
        private static string FormatDate(DateTime utc) => FormatBusinessDate(PakistanTime.ToBusinessDate(utc));

        private static string FormatBusinessDate(DateTime businessDate) =>
            businessDate.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }
}
