using System.Text.Json;
using DAMS.Application.Common;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace DAMS.Application.Services.Notifications
{
    /// <summary>
    /// The single entry point every DAMS module uses to raise a notification.
    ///
    /// It does three things and deliberately no more: decide which channels this event may
    /// use (admin rule ∩ what the module allows ∩ what is configured ∩ what the recipient
    /// agreed to), write one permanent notification, and open one delivery row per channel.
    /// It never calls a provider, so a business operation can never be slowed down or made to
    /// fail by an email server.
    /// </summary>
    public sealed class NotificationDispatcher : INotificationDispatcher, IDisposable
    {
        private readonly AppDbContext _context;
        private readonly NotificationSettingsStore _settings;
        private readonly INotificationRealtimeBroker _realtime;
        private readonly TimeProvider _clock;
        private readonly NotificationEligibilityPolicy _eligibility;
        private readonly ILogger<NotificationDispatcher> _logger;

        private Dictionary<NotificationType, NotificationRule>? _rules;
        private Dictionary<(int UserId, NotificationCategory Category), NotificationPreference>? _preferences;
        private bool? _emailGloballyOn;
        private bool? _pushGloballyOn;
        private bool? _schemaAvailable;

        /// <summary>Users to nudge once the caller's own SaveChanges succeeds.</summary>
        private readonly HashSet<int> _pendingRealtime = new();

        public NotificationDispatcher(
            AppDbContext context,
            NotificationSettingsStore settings,
            INotificationRealtimeBroker realtime,
            TimeProvider clock,
            NotificationEligibilityPolicy eligibility,
            ILogger<NotificationDispatcher> logger)
        {
            _context = context;
            _settings = settings;
            _realtime = realtime;
            // The same clock the delivery worker reads, so "due now" means the same instant on
            // both sides and a notification is never queued a fraction ahead of its sweep.
            _clock = clock;
            _eligibility = eligibility;
            _logger = logger;

            // Live updates are only ever announced after the write that produced them has
            // actually been committed, and the announcement carries no content — the client
            // re-reads its own inbox, so the stream can never become a way around access rules.
            _context.SavedChanges += OnSavedChanges;
        }

        public async Task<bool> QueueAsync(NotificationRequest request, CancellationToken cancellationToken = default)
        {
            // A notification needs somebody to reach: a login, or failing that an address.
            // Anything else is a caller mistake and is dropped rather than stored unreachable.
            var hasUser = request.RecipientUserId is > 0;
            var hasAddress = SmtpEmailSender.IsValidAddress(request.RecipientEmail);
            if (!hasUser && !hasAddress)
                return false;

            if (!await NotificationSchemaExistsAsync(cancellationToken))
                return false;

            // This is the creation-time security boundary. Producers resolve recipients from
            // business relationships, and the central policy independently verifies that the
            // current database role and related record permit this event.
            if (!await _eligibility.CanQueueAsync(request, cancellationToken))
            {
                _logger.LogWarning(
                    "Rejected notification recipient. Type {Type}, user {RecipientUserId}, contact-only {ContactOnly}, entity {EntityType}/{EntityId}.",
                    request.Type, request.RecipientUserId, request.RecipientUserId == null,
                    request.EntityType, request.EntityId);
                return false;
            }

            var key = LeadContactNormalizer.Limit(request.DedupKey, 200);

            // Two guards: rows staged in this unit of work but not yet saved (one scan raising
            // several alerts), and rows an earlier run already committed.
            if (_context.ChangeTracker.Entries<Notification>()
                    .Any(e => e.State == EntityState.Added && e.Entity.DedupKey == key))
                return false;

            if (await _context.Notifications.AnyAsync(n => n.DedupKey == key, cancellationToken))
                return false;

            var notification = await BuildAsync(request, key, cancellationToken);
            if (notification == null)
                return false;

            _context.Notifications.Add(notification);
            if (request.RecipientUserId is > 0)
                _pendingRealtime.Add(request.RecipientUserId.Value);
            return true;
        }

        public async Task<int> QueueManyAsync(IEnumerable<NotificationRequest> requests, CancellationToken cancellationToken = default)
        {
            var created = 0;
            foreach (var request in requests)
            {
                if (await QueueAsync(request, cancellationToken))
                    created++;
            }
            return created;
        }

        public async Task<bool> DispatchAsync(NotificationRequest request, CancellationToken cancellationToken = default)
        {
            if (!await QueueAsync(request, cancellationToken))
                return false;

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException ex) when (IsDuplicateKey(ex))
            {
                // Another request or another instance won the race. That is the correct
                // outcome, not an error: exactly one notification exists.
                DetachStaged(LeadContactNormalizer.Limit(request.DedupKey, 200));
                return false;
            }
        }

        /// <summary>
        /// Builds the notification and its delivery rows. Returns null when the admin has
        /// switched the whole notification type off.
        /// </summary>
        private async Task<Notification?> BuildAsync(NotificationRequest request, string dedupKey, CancellationToken cancellationToken)
        {
            var definition = NotificationCatalog.GetRequired(request.Type);
            var rule = await GetRuleAsync(request.Type, cancellationToken);

            if (rule is { IsEnabled: false } && !definition.IsMandatory)
                return null;

            var priority = request.Priority ?? rule?.Priority ?? definition.Priority;
            var title = Clean(request.Title) ?? definition.Name;
            var message = Clean(request.Message) ?? string.Empty;

            var deepLink = NotificationLink.Sanitize(request.DeepLink)
                           ?? NotificationLink.ForEntity(request.EntityType, request.EntityId, request.SecondaryEntityId);

            // Anything the caller passed, plus the rendered title/message so a template can
            // fall back to them, plus the deep link every template's action button uses.
            var data = new Dictionary<string, string?>(request.Data, StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = title,
                ["message"] = message
            };

            var channels = await ResolveChannelsAsync(request, definition, rule, cancellationToken);

            var now = _clock.GetUtcNow().UtcDateTime;
            var availableAt = request.AvailableAt ?? now;
            if (rule is { DelayMinutes: > 0 })
                availableAt = availableAt.AddMinutes(rule.DelayMinutes);

            var notification = new Notification
            {
                Category = definition.Category,
                Type = request.Type,
                Priority = priority,
                Module = definition.Module,
                Title = LeadContactNormalizer.Limit(title, 200),
                Message = LeadContactNormalizer.Limit(message, 2000),
                EntityType = request.EntityType,
                EntityId = request.EntityId,
                DeepLink = deepLink,
                RecipientUserId = request.RecipientUserId is > 0 ? request.RecipientUserId : null,
                RecipientEmail = request.RecipientUserId is > 0
                    ? null
                    : LeadContactNormalizer.LimitOrNull(request.RecipientEmail?.Trim(), 200),
                RecipientName = LeadContactNormalizer.LimitOrNull(request.RecipientName, 200),
                CreatedByUserId = request.CreatedByUserId,
                CreatedAt = now,
                ExpiresAt = request.ExpiresAt,
                Channels = channels.Where(c => c.Status == NotificationDeliveryStatus.Pending)
                                   .Aggregate(NotificationChannel.None, (acc, c) => acc | c.Channel),
                DedupKey = dedupKey,
                DataJson = Serialize(data),
                NotificationJobId = request.JobId,
                IsEscalation = request.IsEscalation
            };

            foreach (var channel in channels)
            {
                notification.Deliveries.Add(new NotificationDelivery
                {
                    Channel = channel.Channel,
                    Status = channel.Status,
                    FailureReason = channel.Reason,
                    IsPermanentFailure = channel.Status == NotificationDeliveryStatus.Skipped,
                    AvailableAt = availableAt,
                    // A skipped channel is a finished story; record when it was decided.
                    FailedAt = channel.Status == NotificationDeliveryStatus.Skipped ? now : null
                });
            }

            return notification;
        }

        private sealed record ChannelPlan(NotificationChannel Channel, NotificationDeliveryStatus Status, string? Reason);

        /// <summary>
        /// Channel resolution, in order of authority: what the module allows, what the admin
        /// rule enables, whether the channel is configured at all, and finally the recipient's
        /// own preference. Preferences are enforced here — the backend, not the frontend.
        /// </summary>
        private async Task<List<ChannelPlan>> ResolveChannelsAsync(
            NotificationRequest request,
            NotificationDefinition definition,
            NotificationRule? rule,
            CancellationToken cancellationToken)
        {
            var allowed = rule == null
                ? definition.DefaultChannels
                : (rule.InAppEnabled ? NotificationChannel.InApp : NotificationChannel.None)
                  | (rule.EmailEnabled ? NotificationChannel.Email : NotificationChannel.None)
                  | (rule.PushEnabled ? NotificationChannel.WebPush : NotificationChannel.None);

            if (request.ChannelMask.HasValue)
                allowed &= request.ChannelMask.Value;

            var plans = new List<ChannelPlan>();
            var hasUser = request.RecipientUserId is > 0;

            // The inbox is always available for anything that reaches this far: a person must
            // be able to find out what happened on a record they are accountable for. A
            // contact-only recipient has no inbox to put it in.
            if (hasUser && (allowed.HasFlag(NotificationChannel.InApp) || allowed == NotificationChannel.None))
                plans.Add(new ChannelPlan(NotificationChannel.InApp, NotificationDeliveryStatus.Pending, null));

            var mandatory = definition.IsMandatory || NotificationCatalog.IsMandatoryCategory(definition.Category);
            var preference = mandatory || !hasUser
                ? null
                : await GetPreferenceAsync(request.RecipientUserId!.Value, definition.Category, cancellationToken);

            if (allowed.HasFlag(NotificationChannel.Email))
            {
                _emailGloballyOn ??= await _settings.GetBoolAsync(NotificationSettingKeys.EmailEnabled, false, cancellationToken);
                if (_emailGloballyOn.Value)
                {
                    plans.Add(preference is { EmailEnabled: false }
                        ? new ChannelPlan(NotificationChannel.Email, NotificationDeliveryStatus.Skipped,
                            "The recipient has turned off email for this category.")
                        : new ChannelPlan(NotificationChannel.Email, NotificationDeliveryStatus.Pending, null));
                }
            }

            if (hasUser && allowed.HasFlag(NotificationChannel.WebPush))
            {
                _pushGloballyOn ??= await _settings.GetBoolAsync(NotificationSettingKeys.PushEnabled, false, cancellationToken);
                if (_pushGloballyOn.Value)
                {
                    plans.Add(preference is { PushEnabled: false }
                        ? new ChannelPlan(NotificationChannel.WebPush, NotificationDeliveryStatus.Skipped,
                            "The recipient has turned off browser push for this category.")
                        : new ChannelPlan(NotificationChannel.WebPush, NotificationDeliveryStatus.Pending, null));
                }
            }

            return plans;
        }

        private async Task<NotificationRule?> GetRuleAsync(NotificationType type, CancellationToken cancellationToken)
        {
            _rules ??= await _context.NotificationRules
                .AsNoTracking()
                .ToDictionaryAsync(r => r.Type, cancellationToken);

            return _rules.TryGetValue(type, out var rule) ? rule : null;
        }

        private async Task<NotificationPreference?> GetPreferenceAsync(
            int userId, NotificationCategory category, CancellationToken cancellationToken)
        {
            // One scan notifies the same handful of people about many records; load each
            // person's whole preference set once.
            _preferences ??= new Dictionary<(int, NotificationCategory), NotificationPreference>();

            if (_preferences.TryGetValue((userId, category), out var cached))
                return cached;

            var rows = await _context.NotificationPreferences
                .AsNoTracking()
                .Where(p => p.UserId == userId)
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
                _preferences[(row.UserId, row.Category)] = row;

            return _preferences.TryGetValue((userId, category), out var found) ? found : null;
        }

        private static string? Clean(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string? Serialize(IReadOnlyDictionary<string, string?> data)
        {
            if (data.Count == 0)
                return null;

            var json = JsonSerializer.Serialize(data);
            // The column is bounded; an oversized payload loses its variables rather than
            // failing the business operation that raised it.
            return json.Length <= 4000 ? json : null;
        }

        private async Task<bool> NotificationSchemaExistsAsync(CancellationToken cancellationToken)
        {
            if (_schemaAvailable.HasValue)
                return _schemaAvailable.Value;

            if (!_context.Database.IsRelational())
                return (_schemaAvailable = true).Value;

            var connection = _context.Database.GetDbConnection();
            await _context.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                await using var command = connection.CreateCommand();
                // A caller (e.g. lead conversion) may already have an EF transaction open on
                // this connection. SQL Server rejects a command on a connection with a pending
                // local transaction unless the command is enlisted in it.
                command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
                command.CommandText = """
                    SELECT CASE WHEN
                        OBJECT_ID(N'[dbo].[Notifications]', N'U') IS NOT NULL AND
                        OBJECT_ID(N'[dbo].[NotificationDeliveries]', N'U') IS NOT NULL AND
                        OBJECT_ID(N'[dbo].[NotificationRules]', N'U') IS NOT NULL AND
                        OBJECT_ID(N'[dbo].[NotificationPreferences]', N'U') IS NOT NULL AND
                        OBJECT_ID(N'[dbo].[NotificationSettings]', N'U') IS NOT NULL
                    THEN 1 ELSE 0 END
                    """;

                var result = await command.ExecuteScalarAsync(cancellationToken);
                _schemaAvailable = Convert.ToInt32(result) == 1;
                return _schemaAvailable.Value;
            }
            finally
            {
                await _context.Database.CloseConnectionAsync();
            }
        }

        /// <summary>Removes rows staged for a dispatch that lost a uniqueness race, so the
        /// DbContext is reusable by the rest of the request.</summary>
        private void DetachStaged(string dedupKey)
        {
            foreach (var entry in _context.ChangeTracker.Entries<Notification>()
                         .Where(e => e.State == EntityState.Added && e.Entity.DedupKey == dedupKey)
                         .ToList())
            {
                foreach (var delivery in entry.Entity.Deliveries.ToList())
                    _context.Entry(delivery).State = EntityState.Detached;

                entry.State = EntityState.Detached;
            }
        }

        internal static bool IsDuplicateKey(DbUpdateException ex) =>
            ex.InnerException is SqlException { Number: 2601 or 2627 }
            // The in-memory provider surfaces a duplicate key as a plain update exception.
            || ex.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The DbContext is pooled, so the handler must come off again when this scope ends;
        /// otherwise a later request that rents the same context could inherit it.
        /// </summary>
        public void Dispose()
        {
            _context.SavedChanges -= OnSavedChanges;
            _pendingRealtime.Clear();
        }

        private void OnSavedChanges(object? sender, SavedChangesEventArgs e)
        {
            if (_pendingRealtime.Count == 0)
                return;

            var recipients = _pendingRealtime.ToArray();
            _pendingRealtime.Clear();

            foreach (var userId in recipients)
            {
                // Fire and forget: a live nudge is a convenience, never a delivery guarantee.
                _ = _realtime.PublishAsync(userId, "notification", new { refresh = true });
            }
        }
    }
}
