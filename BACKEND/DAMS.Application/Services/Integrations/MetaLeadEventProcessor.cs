using DAMS.Application.Common;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DAMS.Application.Services.Integrations
{
    /// <summary>
    /// Drains the webhook inbox: claims an event under a lease, fetches the real lead from
    /// Meta, and hands it to the ordinary ingestion path.
    ///
    /// It deliberately does not create a Lead itself. Everything DAMS knows about duplicates,
    /// enrichment, assignment and notifications lives behind ILeadService.IngestAsync, and a
    /// second way in would drift from it within a release or two.
    /// </summary>
    public sealed class MetaLeadEventProcessor : IMetaLeadEventProcessor
    {
        /// <summary>
        /// Processing is claimable so an event abandoned by a killed worker is picked up once
        /// its lease expires, rather than being stuck forever.
        /// </summary>
        private static readonly ExternalIntegrationEventStatus[] Claimable =
        [
            ExternalIntegrationEventStatus.Pending,
            ExternalIntegrationEventStatus.Retry,
            ExternalIntegrationEventStatus.Processing
        ];

        private static readonly string WorkerId = $"{Environment.MachineName}:{Environment.ProcessId}";

        private readonly AppDbContext _context;
        private readonly IMetaGraphClient _graph;
        private readonly IIntegrationSecretProtector _protector;
        private readonly ILeadService _leads;
        private readonly MetaIntegrationOptions _options;
        private readonly ILogger<MetaLeadEventProcessor> _logger;

        public MetaLeadEventProcessor(
            AppDbContext context,
            IMetaGraphClient graph,
            IIntegrationSecretProtector protector,
            ILeadService leads,
            MetaIntegrationOptions options,
            ILogger<MetaLeadEventProcessor> logger)
        {
            _context = context;
            _graph = graph;
            _protector = protector;
            _leads = leads;
            _options = options;
            _logger = logger;
        }

        public async Task<int> ProcessPendingEventsAsync(int batchSize, CancellationToken cancellationToken = default)
        {
            var claimed = await ClaimEventsAsync(batchSize, cancellationToken);

            var processed = 0;
            foreach (var eventId in claimed)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                if (await ProcessOneAsync(eventId, cancellationToken))
                    processed++;
            }

            return processed;
        }

        /// <summary>
        /// Takes a lease on due events. Two workers racing for the same row both write, and the
        /// row version lets exactly one win; the loser simply drops it and moves on.
        /// </summary>
        private async Task<List<int>> ClaimEventsAsync(int batchSize, CancellationToken cancellationToken)
        {
            var take = Math.Clamp(batchSize, 1, 200);

            for (var attempt = 0; attempt < 3; attempt++)
            {
                var now = DateTime.UtcNow;

                var due = await _context.ExternalIntegrationEvents
                    .Where(e => Claimable.Contains(e.Status)
                                && e.AvailableAt <= now
                                && (e.LockedUntil == null || e.LockedUntil < now))
                    .OrderBy(e => e.AvailableAt)
                    .ThenBy(e => e.Id)
                    .Take(take)
                    .ToListAsync(cancellationToken);

                if (due.Count == 0)
                    return [];

                foreach (var item in due)
                {
                    item.Status = ExternalIntegrationEventStatus.Processing;
                    item.LockedUntil = now.AddMinutes(_options.LeaseMinutes);
                    item.LockedBy = WorkerId;
                }

                try
                {
                    await _context.SaveChangesAsync(cancellationToken);
                    return due.Select(e => e.Id).ToList();
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    foreach (var entry in ex.Entries)
                        entry.State = EntityState.Detached;

                    _context.ChangeTracker.Clear();
                }
            }

            _logger.LogWarning("Could not claim Meta lead events after three attempts; another worker is holding them.");
            return [];
        }

        private async Task<bool> ProcessOneAsync(int eventId, CancellationToken cancellationToken)
        {
            _context.ChangeTracker.Clear();

            var integrationEvent = await _context.ExternalIntegrationEvents
                .Include(e => e.Connection)
                .Include(e => e.Resource)
                .FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);

            if (integrationEvent is null)
                return false;

            var connection = integrationEvent.Connection;
            var resource = integrationEvent.Resource;

            // An event for a page nobody enabled, or one belonging to a connection that has
            // since been disconnected, is closed out honestly instead of becoming a lead.
            if (connection is null || connection.Status == ExternalIntegrationConnectionStatus.Disconnected)
                return await IgnoreAsync(integrationEvent, "This Meta connection is disconnected.", cancellationToken);

            if (resource is null || !resource.IsEnabled || !resource.IsActive)
                return await IgnoreAsync(integrationEvent, "This page is not enabled for lead delivery in DAMS.", cancellationToken);

            if (connection.Status == ExternalIntegrationConnectionStatus.NeedsReauthorization)
            {
                // Park rather than fail: nothing will work until an admin reconnects, and when
                // they do, this event should still be waiting. The attempt is not counted,
                // otherwise a long reauthorization window would silently exhaust the retries.
                await DeferAsync(integrationEvent, TimeSpan.FromHours(_options.AuthRetryDelayHours),
                    "Waiting for this Meta connection to be reauthorized.", cancellationToken);
                return false;
            }

            var leadgenId = ReadLeadgenId(integrationEvent.RawPayloadJson);
            if (leadgenId is null)
                return await FailAsync(integrationEvent, "The webhook payload contained no leadgen id.", cancellationToken);

            var token = _protector.TryUnprotect(resource.ResourceTokenProtected)
                        ?? _protector.TryUnprotect(connection.AccessTokenProtected);

            if (token is null)
            {
                // A credential we cannot decrypt is indistinguishable from one Meta rejected:
                // in both cases only reconnecting fixes it.
                await MarkNeedsReauthorizationAsync(connection, "The stored credential could not be read.", cancellationToken);
                await DeferAsync(integrationEvent, TimeSpan.FromHours(_options.AuthRetryDelayHours),
                    "The stored credential could not be read.", cancellationToken);
                return false;
            }

            try
            {
                integrationEvent.Attempts++;

                var lead = await _graph.GetLeadAsync(leadgenId, token, cancellationToken);
                await IngestAsync(integrationEvent, connection, resource, lead, cancellationToken);
                return true;
            }
            catch (MetaAuthorizationException ex)
            {
                // Retrying cannot help, so the connection is flagged and the event waits for a
                // human rather than burning its attempts against a dead token.
                integrationEvent.Attempts--;
                await MarkNeedsReauthorizationAsync(connection, ex.Message, cancellationToken);
                await DeferAsync(integrationEvent, TimeSpan.FromHours(_options.AuthRetryDelayHours), ex.Message, cancellationToken);
                return false;
            }
            catch (MetaTransientException ex)
            {
                await RetryOrFailAsync(integrationEvent, ex.Message, cancellationToken);
                return false;
            }
            catch (MetaGraphException ex)
            {
                return await FailAsync(integrationEvent, ex.Message, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Processing Meta lead event {EventId} failed unexpectedly.", integrationEvent.Id);
                await RetryOrFailAsync(integrationEvent, ex.Message, cancellationToken);
                return false;
            }
        }

        private async Task IngestAsync(
            ExternalIntegrationEvent integrationEvent,
            ExternalIntegrationConnection connection,
            ExternalIntegrationResource resource,
            MetaLead lead,
            CancellationToken cancellationToken)
        {
            var mapped = MetaLeadFieldMapper.Map(lead.FieldData);
            var platform = ResolvePlatform(lead, resource);

            var dto = new LeadIntakeDto
            {
                // Meta forms do not guarantee a name; the lead is still worth having, and the
                // provider reference identifies it either way.
                FirstName = string.IsNullOrWhiteSpace(mapped.FirstName) ? "Meta lead" : mapped.FirstName,
                LastName = mapped.LastName,
                Phone = mapped.Phone,
                WhatsappNumber = mapped.WhatsappNumber,
                Email = mapped.Email,
                City = mapped.City,
                SourceCode = ResolveSourceCode(platform),
                SourceDetails = BuildSourceDetails(lead, resource),
                CampaignName = lead.CampaignName,
                CampaignReference = lead.CampaignId,
                AdReference = lead.AdId,
                ExternalProvider = IntegrationProviders.Meta,
                ExternalLeadId = lead.LeadgenId,
                ExternalFormReference = lead.FormId,
                ExternalSubmittedAt = lead.CreatedTime,
                // A summary only: the complete provider response is stored on the submission,
                // which has no length limit, rather than squeezed into this 4000-char column.
                IntegrationPayload = LeadContactNormalizer.LimitOrNull(mapped.ToFieldDataJson(), 4000),
                // A repeat enquiry enriches the existing lead instead of being rejected — the
                // same rule every external channel already follows.
                AllowDuplicate = true
            };

            var result = await _leads.IngestAsync(dto, actor: null, cancellationToken);

            if (result.Lead is null)
            {
                await FailAsync(integrationEvent, result.Message, cancellationToken);
                return;
            }

            await StampAttributionAsync(connection, resource, lead, platform, mapped, cancellationToken);

            integrationEvent.Status = ExternalIntegrationEventStatus.Processed;
            integrationEvent.ProcessedAt = DateTime.UtcNow;
            integrationEvent.LeadId = result.Lead.Id;
            integrationEvent.LastError = null;
            ReleaseLease(integrationEvent);

            await _context.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// Writes the campaign/ad/form trail and every form answer onto the submission receipt.
        ///
        /// Only ever onto a receipt created by this very ingestion. A submission is immutable
        /// history: if this event was a replay, the existing row already says what that enquiry
        /// said and must not be rewritten.
        /// </summary>
        private async Task StampAttributionAsync(
            ExternalIntegrationConnection connection,
            ExternalIntegrationResource resource,
            MetaLead lead,
            string? platform,
            MappedMetaFields mapped,
            CancellationToken cancellationToken)
        {
            var submission = await _context.LeadExternalSubmissions
                .FirstOrDefaultAsync(s => s.Provider == IntegrationProviders.Meta
                                          && s.ExternalLeadId == lead.LeadgenId, cancellationToken);

            if (submission is null || submission.ExternalIntegrationConnectionId != null)
                return;

            submission.ExternalIntegrationConnectionId = connection.Id;
            submission.Platform = platform;
            submission.PageExternalId = resource.ExternalId;
            submission.PageName = resource.Name;
            submission.CampaignExternalId = lead.CampaignId;
            submission.CampaignName = lead.CampaignName;
            submission.AdSetExternalId = lead.AdSetId;
            submission.AdSetName = lead.AdSetName;
            submission.AdExternalId = lead.AdId;
            submission.AdName = lead.AdName;
            submission.ExternalFormName = lead.FormName;
            submission.RawPayloadJson = lead.RawJson;
            submission.FieldDataJson = mapped.ToFieldDataJson();
        }

        /// <summary>
        /// Facebook or Instagram only when Meta actually says so.
        ///
        /// Every lead-ad webhook arrives through a Page, so treating that as evidence of
        /// Facebook would quietly relabel every Instagram lead and corrupt channel reporting.
        /// When the platform is genuinely unknown, the honest answer is "Meta".
        /// </summary>
        private static string? ResolvePlatform(MetaLead lead, ExternalIntegrationResource resource)
        {
            var stated = lead.Platform?.Trim().ToLowerInvariant();

            return stated switch
            {
                "ig" or "instagram" => IntegrationSourceCodes.Instagram,
                "fb" or "facebook" => IntegrationSourceCodes.Facebook,
                _ => resource.ResourceType == ExternalResourceTypes.InstagramAccount
                    ? IntegrationSourceCodes.Instagram
                    : null
            };
        }

        private static string ResolveSourceCode(string? platform) => platform switch
        {
            IntegrationSourceCodes.Instagram => IntegrationSourceCodes.Instagram,
            IntegrationSourceCodes.Facebook => IntegrationSourceCodes.Facebook,
            _ => IntegrationSourceCodes.Meta
        };

        private static string BuildSourceDetails(MetaLead lead, ExternalIntegrationResource resource)
        {
            var parts = new List<string> { $"Meta page: {resource.Name ?? resource.ExternalId}" };

            if (!string.IsNullOrWhiteSpace(lead.CampaignName))
                parts.Add($"campaign: {lead.CampaignName}");
            if (!string.IsNullOrWhiteSpace(lead.AdName))
                parts.Add($"ad: {lead.AdName}");
            if (!string.IsNullOrWhiteSpace(lead.FormId))
                parts.Add($"form: {lead.FormName ?? lead.FormId}");

            return LeadContactNormalizer.Limit(string.Join(", ", parts), 500);
        }

        // ── Outcomes ────────────────────────────────────────────────────────────────

        private async Task<bool> IgnoreAsync(
            ExternalIntegrationEvent integrationEvent, string reason, CancellationToken cancellationToken)
        {
            integrationEvent.Status = ExternalIntegrationEventStatus.Ignored;
            integrationEvent.ProcessedAt = DateTime.UtcNow;
            integrationEvent.LastError = MetaCredentialScrubber.ScrubAndLimit(reason, 1000);
            ReleaseLease(integrationEvent);

            await _context.SaveChangesAsync(cancellationToken);
            return false;
        }

        private async Task<bool> FailAsync(
            ExternalIntegrationEvent integrationEvent, string reason, CancellationToken cancellationToken)
        {
            integrationEvent.Status = ExternalIntegrationEventStatus.Failed;
            integrationEvent.ProcessedAt = DateTime.UtcNow;
            integrationEvent.LastError = MetaCredentialScrubber.ScrubAndLimit(reason, 1000);
            ReleaseLease(integrationEvent);

            await _context.SaveChangesAsync(cancellationToken);
            return false;
        }

        private async Task DeferAsync(
            ExternalIntegrationEvent integrationEvent, TimeSpan delay, string reason, CancellationToken cancellationToken)
        {
            integrationEvent.Status = ExternalIntegrationEventStatus.Retry;
            integrationEvent.AvailableAt = DateTime.UtcNow.Add(delay);
            integrationEvent.LastError = MetaCredentialScrubber.ScrubAndLimit(reason, 1000);
            ReleaseLease(integrationEvent);

            await _context.SaveChangesAsync(cancellationToken);
        }

        /// <summary>Backs off exponentially, then gives up rather than retrying forever.</summary>
        private async Task RetryOrFailAsync(
            ExternalIntegrationEvent integrationEvent, string reason, CancellationToken cancellationToken)
        {
            if (integrationEvent.Attempts >= _options.MaxAttempts)
            {
                await FailAsync(integrationEvent, reason, cancellationToken);
                return;
            }

            var exponent = Math.Max(0, integrationEvent.Attempts - 1);
            var seconds = _options.BaseRetryDelaySeconds * Math.Pow(2, exponent);
            var capped = Math.Min(seconds, _options.MaxRetryDelayMinutes * 60d);

            await DeferAsync(integrationEvent, TimeSpan.FromSeconds(capped), reason, cancellationToken);
        }

        private static void ReleaseLease(ExternalIntegrationEvent integrationEvent)
        {
            integrationEvent.LockedUntil = null;
            integrationEvent.LockedBy = null;
        }

        private async Task MarkNeedsReauthorizationAsync(
            ExternalIntegrationConnection connection, string reason, CancellationToken cancellationToken)
        {
            if (connection.Status == ExternalIntegrationConnectionStatus.NeedsReauthorization)
                return;

            connection.Status = ExternalIntegrationConnectionStatus.NeedsReauthorization;
            connection.LastErrorAt = DateTime.UtcNow;
            connection.LastError = MetaCredentialScrubber.ScrubAndLimit(reason, 1000);
            connection.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task<int> PruneOAuthStatesAsync(CancellationToken cancellationToken = default)
        {
            // Only the short-lived OAuth states are cleaned up. Events are never pruned: they
            // are the record of what a provider sent and why DAMS did or did not act on it.
            var cutoff = DateTime.UtcNow.AddDays(-1);

            return await _context.ExternalIntegrationOAuthStates
                .Where(s => s.ExpiresAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken);
        }

        private static string? ReadLeadgenId(string payloadJson)
        {
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(payloadJson);
                return document.RootElement.TryGetProperty("leadgen_id", out var value)
                    ? value.ValueKind switch
                    {
                        System.Text.Json.JsonValueKind.String => value.GetString(),
                        System.Text.Json.JsonValueKind.Number => value.GetRawText(),
                        _ => null
                    }
                    : null;
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }
    }
}
