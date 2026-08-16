using System.Text.Json;
using DAMS.Application.Common;
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
    /// Turns a verified webhook body into durable work and nothing more.
    ///
    /// Meta expects a fast 200 and disables a subscription that keeps failing, so this does the
    /// least possible: parse enough to identify the page, write a row, return. Fetching the
    /// lead — the slow, failure-prone part — belongs to the background worker, where a retry
    /// costs nothing and Meta is not waiting.
    /// </summary>
    public sealed class MetaWebhookIntakeService : IMetaWebhookIntakeService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<MetaWebhookIntakeService> _logger;

        public MetaWebhookIntakeService(AppDbContext context, ILogger<MetaWebhookIntakeService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<int> RecordAsync(string rawBody, CancellationToken cancellationToken = default)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(rawBody);
            }
            catch (JsonException ex)
            {
                // A signed body that is not JSON is Meta changing something unannounced, not an
                // attack — the signature already passed. Nothing to record.
                _logger.LogWarning(ex, "A signed Meta webhook body could not be parsed as JSON.");
                return 0;
            }

            using (document)
            {
                var root = document.RootElement;
                if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
                    return 0;

                var recorded = 0;
                // Keys already handled by this call. A lead appearing twice in one delivery
                // would otherwise be attempted twice; each attempt is now saved and resolved
                // individually, so this is only an optimisation, not a correctness requirement.
                var pending = new HashSet<string>(StringComparer.Ordinal);

                foreach (var entry in entries.EnumerateArray())
                {
                    if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var change in changes.EnumerateArray())
                    {
                        var field = ReadString(change, "field") ?? "unknown";
                        if (!change.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Object)
                            continue;

                        var pageId = ReadString(value, "page_id") ?? ReadString(entry, "id");
                        var leadgenId = ReadString(value, "leadgen_id");

                        if (string.IsNullOrWhiteSpace(leadgenId))
                            continue;

                        if (await RecordOneAsync(field, pageId, leadgenId, value.GetRawText(), pending, cancellationToken))
                            recorded++;
                    }
                }

                return recorded;
            }
        }

        /// <summary>
        /// Saves exactly one event and commits it immediately, so a duplicate or a failure
        /// elsewhere in the delivery can never take an unrelated event down with it — a single
        /// shared SaveChanges for the whole batch would otherwise roll back everything the
        /// moment any one row collided with the unique index.
        /// </summary>
        private async Task<bool> RecordOneAsync(
            string field,
            string? pageId,
            string leadgenId,
            string payloadJson,
            HashSet<string> pending,
            CancellationToken cancellationToken)
        {
            // Resolve which connection owns this page. Enabling a page is exclusive to one
            // connection (MetaIntegrationService.SetResourceEnabledAsync refuses a second), so
            // this should only ever find one enabled candidate; the ordering is a defensive
            // tie-break for pre-existing data, not the mechanism that prevents ambiguity. The
            // unique (Provider, ExternalLeadId) index on submissions is a second backstop that
            // stops one lead being created twice regardless.
            var resource = pageId is null
                ? null
                : await _context.ExternalIntegrationResources
                    .Where(r => r.Provider == IntegrationProviders.Meta
                                && r.ResourceType == ExternalResourceTypes.FacebookPage
                                && r.ExternalId == pageId)
                    .OrderByDescending(r => r.IsEnabled)
                    .ThenByDescending(r => r.ExternalIntegrationConnectionId)
                    .FirstOrDefaultAsync(cancellationToken);

            var eventKey = $"{resource?.ExternalIntegrationConnectionId ?? 0}:{pageId ?? "unknown"}:{leadgenId}";

            if (!pending.Add(eventKey))
                return false;

            // A cheap fast path: avoids an exception in the common case. Under real
            // concurrency two requests can both pass this check before either commits, so it
            // is not what makes duplication impossible — the unique index and the catch below
            // are.
            var alreadyRecorded = await _context.ExternalIntegrationEvents
                .AnyAsync(e => e.Provider == IntegrationProviders.Meta && e.EventKey == eventKey, cancellationToken);

            if (alreadyRecorded)
                return false;

            var isDeliverable = resource is { IsEnabled: true, IsActive: true };

            var integrationEvent = new ExternalIntegrationEvent
            {
                Provider = IntegrationProviders.Meta,
                ExternalIntegrationConnectionId = resource?.ExternalIntegrationConnectionId,
                ExternalIntegrationResourceId = resource?.Id,
                EventType = field,
                EventKey = eventKey,
                ResourceExternalId = pageId,
                RawPayloadJson = payloadJson,
                ReceivedAt = DateTime.UtcNow,
                // Recorded either way. A lead arriving from a page nobody enabled is not turned
                // into a lead, but it is not thrown away silently either — an admin can see it
                // and understand why nothing happened.
                Status = isDeliverable
                    ? ExternalIntegrationEventStatus.Pending
                    : ExternalIntegrationEventStatus.Ignored,
                ProcessedAt = isDeliverable ? null : DateTime.UtcNow,
                LastError = isDeliverable
                    ? null
                    : resource is null
                        ? "No connected Meta page matches this webhook."
                        : "This page is not enabled for lead delivery in DAMS.",
                AvailableAt = DateTime.UtcNow
            };

            _context.ExternalIntegrationEvents.Add(integrationEvent);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException ex) when (IsDuplicateEventKey(ex))
            {
                // Another request (a concurrent delivery, or Meta redelivering while the first
                // is still in flight) committed this exact event first. That is success, not
                // failure — the event exists — so this attempt is dropped and the rest of the
                // batch continues rather than the whole delivery being lost.
                _context.Entry(integrationEvent).State = EntityState.Detached;
                return false;
            }
        }

        private static bool IsDuplicateEventKey(DbUpdateException ex) =>
            ex.InnerException is SqlException { Number: 2601 or 2627 } sql
            && sql.Message.Contains("IX_ExternalIntegrationEvents_Provider_EventKey", StringComparison.OrdinalIgnoreCase);

        private static string? ReadString(JsonElement element, string property) =>
            element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
                ? value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number => value.GetRawText(),
                    _ => null
                }
                : null;
    }
}
