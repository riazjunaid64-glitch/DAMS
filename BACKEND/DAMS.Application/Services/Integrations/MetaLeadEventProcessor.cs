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

        /// <summary>The length of every provider-id column a lead, submission or event stores.</summary>
        internal const int MaxExternalIdLength = 200;

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

                try
                {
                    if (await ProcessOneAsync(eventId, cancellationToken))
                        processed++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Even this event's outcome could not be saved. Its attempt is already on
                    // record; it keeps its lease and is claimed again once that lapses, and the
                    // events behind it do not wait.
                    _logger.LogError(ex,
                        "Meta lead event {EventId} could not be recorded; it will be retried when its lease expires.", eventId);
                }
            }

            return processed;
        }

        /// <summary>
        /// Takes a lease on due events. Two workers racing for the same row both write, and the
        /// row version lets exactly one win; the loser simply drops it and moves on.
        /// </summary>
        private async Task<List<int>> ClaimEventsAsync(int batchSize, CancellationToken cancellationToken)
        {
            // A batch is processed serially under one lease taken at claim time. Worst case is
            // every item timing out: batchSize * RequestTimeoutSeconds must comfortably fit
            // inside the lease, or a still-in-progress item's lease can look expired and a
            // second worker reclaims — and starts double-fetching — a row nobody actually
            // abandoned. Halving the arithmetic ceiling leaves headroom for everything that
            // is not the Graph call itself (DB round trips, GC, scheduling).
            var leaseSeconds = Math.Max(1, _options.LeaseMinutes * 60);
            var worstCasePerItemSeconds = Math.Max(1, _options.RequestTimeoutSeconds * 2);
            var safeForLease = Math.Max(1, leaseSeconds / worstCasePerItemSeconds);

            var take = Math.Clamp(batchSize, 1, Math.Min(200, safeForLease));

            for (var attempt = 0; attempt < 3; attempt++)
            {
                var now = DateTime.UtcNow;

                var due = await _context.ExternalIntegrationEvents
                    .Where(e => e.Provider == IntegrationProviders.Meta
                                && Claimable.Contains(e.Status)
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

            // Only an event abandoned mid-attempt gets here with none left: every attempt that
            // records an outcome already fails the event on its last one. Without this, one
            // that keeps killing the worker, or keeps failing to save its outcome, would be
            // reclaimed at every lease expiry forever.
            if (integrationEvent.Attempts >= _options.MaxAttempts)
                return await FailAsync(integrationEvent,
                    $"Processing did not complete in {integrationEvent.Attempts} attempts.", cancellationToken);

            // Counted before the lead is fetched, not with the outcome: an attempt whose outcome
            // is never saved — the worker died, or the database failed just then — still counts,
            // and brings the event that much closer to giving up.
            integrationEvent.Attempts++;
            await _context.SaveChangesAsync(cancellationToken);

            try
            {
                var lead = await _graph.GetLeadAsync(leadgenId, token, cancellationToken);
                return await IngestAtomicallyAsync(eventId, lead, cancellationToken);
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

                // The transaction rolled the failed write back, but whatever it added is still
                // tracked, so saving the retry state on this context would replay that write and
                // fail with it. The outcome is saved on its own, against a fresh copy of the event.
                _context.ChangeTracker.Clear();

                var fresh = await _context.ExternalIntegrationEvents
                    .FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);
                if (fresh is null)
                    return false;

                // SQL Server may have committed the transaction even when its acknowledgement
                // was lost. Never turn that completed event back into retry work.
                if (fresh.Status == ExternalIntegrationEventStatus.Processed)
                    return true;

                await RetryOrFailAsync(fresh, ex.Message, cancellationToken);
                return false;
            }
        }

        /// <summary>
        /// Creates the lead, its submission and the event's outcome in one transaction. The
        /// lead is saved in two steps (its reference needs the generated id), and without this
        /// a failure between them left a half-made lead that a retry took as already ingested.
        ///
        /// The Graph call has already happened, so no transaction is held open across it. Each
        /// run starts from a clean context and a fresh copy of the event: the execution
        /// strategy re-runs this after a transient failure, and nothing staged by the attempt
        /// that rolled back may ride along into the next.
        /// </summary>
        private Task<bool> IngestAtomicallyAsync(int eventId, MetaLead lead, CancellationToken cancellationToken) =>
            _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();

                await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

                var integrationEvent = await _context.ExternalIntegrationEvents
                    .Include(e => e.Connection)
                    .Include(e => e.Resource)
                    .FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);

                // A retry can follow a lost commit acknowledgement. Re-read inside the new
                // transaction before doing any lead or notification work again.
                if (integrationEvent?.Status == ExternalIntegrationEventStatus.Processed)
                    return true;

                if (integrationEvent?.Connection is null || integrationEvent.Resource is null)
                    return false;

                var ingested = await IngestAsync(
                    integrationEvent, integrationEvent.Connection, integrationEvent.Resource, lead, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return ingested;
            });

        /// <summary>True only when the event became (or enriched) a lead.</summary>
        private async Task<bool> IngestAsync(
            ExternalIntegrationEvent integrationEvent,
            ExternalIntegrationConnection connection,
            ExternalIntegrationResource resource,
            MetaLead lead,
            CancellationToken cancellationToken)
        {
            // The provider's id is what makes ingestion idempotent, so it can be neither cut
            // short nor dropped. One too long to store can never succeed; retrying is pointless.
            if (lead.LeadgenId.Length > MaxExternalIdLength)
                return await FailAsync(integrationEvent, "Meta returned a lead id longer than DAMS can store.", cancellationToken);

            var mapped = MetaLeadFieldMapper.Map(lead.FieldData);
            var platform = ResolvePlatform(lead, resource);

            // Every value below is provider-controlled and bounded to the Lead column it lands
            // in, so no answer can fail the write. Nothing is lost: the submission keeps every
            // answer and the raw response verbatim.
            var dto = new LeadIntakeDto
            {
                // Meta forms do not guarantee a name, and Lead.FirstName is a required column —
                // some value has to go here. It must not read as if Meta actually submitted it,
                // the same principle that keeps this integration from inventing a phone number,
                // so it is built from the provider's own id rather than a plain "Meta lead" that
                // would be indistinguishable from a real answer once it is on screen.
                FirstName = string.IsNullOrWhiteSpace(mapped.FirstName)
                    ? $"Meta lead ({UnnamedLeadSuffix(lead.LeadgenId)})"
                    : LeadContactNormalizer.Limit(mapped.FirstName, 100),
                LastName = LeadContactNormalizer.LimitOrNull(mapped.LastName, 100),
                Phone = FitOrNull(mapped.Phone, 50),
                WhatsappNumber = FitOrNull(mapped.WhatsappNumber, 50),
                Email = FitOrNull(mapped.Email, 200),
                City = LeadContactNormalizer.LimitOrNull(mapped.City, 100),
                SourceCode = ResolveSourceCode(platform),
                SourceDetails = BuildSourceDetails(lead, resource),
                CampaignName = LeadContactNormalizer.LimitOrNull(lead.CampaignName, 200),
                CampaignReference = FitOrNull(lead.CampaignId, MaxExternalIdLength),
                AdReference = FitOrNull(lead.AdId, MaxExternalIdLength),
                ExternalProvider = IntegrationProviders.Meta,
                ExternalLeadId = lead.LeadgenId,
                ExternalFormReference = FitOrNull(lead.FormId, MaxExternalIdLength),
                ExternalSubmittedAt = lead.CreatedTime,
                // A summary only: the complete provider response is stored on the submission,
                // which has no length limit, rather than squeezed into this 4000-char column.
                IntegrationPayload = LeadContactNormalizer.LimitOrNull(mapped.ToFieldDataJson(), 4000),
                // A repeat enquiry enriches the existing lead instead of being rejected — the
                // same rule every external channel already follows.
                AllowDuplicate = true
            };

            var result = await _leads.IngestAsync(
                dto, actor: null, trustedExternal: true, cancellationToken: cancellationToken);

            if (result.Lead is null)
                return await FailAsync(integrationEvent, result.Message, cancellationToken);

            await StampAttributionAsync(connection, resource, lead, platform, mapped, cancellationToken);

            integrationEvent.Status = ExternalIntegrationEventStatus.Processed;
            integrationEvent.ProcessedAt = DateTime.UtcNow;
            integrationEvent.LeadId = result.Lead.Id;
            integrationEvent.LastError = null;
            ReleaseLease(integrationEvent);

            await _context.SaveChangesAsync(cancellationToken);
            return true;
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
            // Bounded like the lead's own columns; RawPayloadJson below keeps the originals.
            submission.CampaignExternalId = FitOrNull(lead.CampaignId, MaxExternalIdLength);
            submission.CampaignName = LeadContactNormalizer.LimitOrNull(lead.CampaignName, 300);
            submission.AdSetExternalId = FitOrNull(lead.AdSetId, MaxExternalIdLength);
            submission.AdSetName = LeadContactNormalizer.LimitOrNull(lead.AdSetName, 300);
            submission.AdExternalId = FitOrNull(lead.AdId, MaxExternalIdLength);
            submission.AdName = LeadContactNormalizer.LimitOrNull(lead.AdName, 300);
            // Neither of these comes back on the lead itself from Graph — the lead endpoint
            // returns a form id but not its name, and no ad-account id at all — so they are
            // filled in, best-effort, from whatever resource sync has already discovered.
            // Absent here just means "not yet synced", never something that should block
            // ingestion, which is why this is a lookup and not a required field.
            submission.ExternalFormName = await LookUpResourceNameAsync(
                ExternalResourceTypes.LeadForm, lead.FormId, cancellationToken);
            submission.AdAccountExternalId = await LookUpParentExternalIdAsync(
                ExternalResourceTypes.Campaign, lead.CampaignId, cancellationToken);
            submission.RawPayloadJson = lead.RawJson;
            submission.FieldDataJson = mapped.ToFieldDataJson();
        }

        private async Task<string?> LookUpResourceNameAsync(
            string resourceType, string? externalId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(externalId))
                return null;

            return await _context.ExternalIntegrationResources
                .Where(r => r.Provider == IntegrationProviders.Meta
                            && r.ResourceType == resourceType
                            && r.ExternalId == externalId)
                .Select(r => r.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        private async Task<string?> LookUpParentExternalIdAsync(
            string resourceType, string? externalId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(externalId))
                return null;

            return await _context.ExternalIntegrationResources
                .Where(r => r.Provider == IntegrationProviders.Meta
                            && r.ResourceType == resourceType
                            && r.ExternalId == externalId)
                .Select(r => r.ParentExternalId)
                .FirstOrDefaultAsync(cancellationToken);
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

        /// <summary>The last few digits of a leadgen id — enough to tell two unnamed leads apart without echoing the whole id.</summary>
        private static string UnnamedLeadSuffix(string leadgenId) =>
            leadgenId.Length <= 6 ? leadgenId : leadgenId[^6..];

        /// <summary>
        /// For ids and contact values, which are dropped rather than cut when they do not fit:
        /// a shortened id or phone number is a different, wrong value, not a shorter right one.
        /// </summary>
        private static string? FitOrNull(string? value, int maxLength) =>
            value is null || value.Length <= maxLength ? value : null;

        private static string ResolveSourceCode(string? platform) => platform switch
        {
            IntegrationSourceCodes.Instagram => IntegrationSourceCodes.Instagram,
            IntegrationSourceCodes.Facebook => IntegrationSourceCodes.Facebook,
            _ => IntegrationSourceCodes.Meta
        };

        /// <summary>
        /// Each part is bounded before joining, so the four together fit the 500-char column: a
        /// long campaign name clamped only at the end would crowd the ad and form out entirely.
        /// The full values are on the submission.
        /// </summary>
        private static string BuildSourceDetails(MetaLead lead, ExternalIntegrationResource resource)
        {
            const int partLength = 110;
            var parts = new List<string>
            {
                $"Meta page: {LeadContactNormalizer.Limit(resource.Name ?? resource.ExternalId, partLength)}"
            };

            if (!string.IsNullOrWhiteSpace(lead.CampaignName))
                parts.Add($"campaign: {LeadContactNormalizer.Limit(lead.CampaignName, partLength)}");
            if (!string.IsNullOrWhiteSpace(lead.AdName))
                parts.Add($"ad: {LeadContactNormalizer.Limit(lead.AdName, partLength)}");
            if (!string.IsNullOrWhiteSpace(lead.FormId))
                parts.Add($"form: {LeadContactNormalizer.Limit(lead.FormName ?? lead.FormId, partLength)}");

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
            var cutoff = DateTime.UtcNow.AddDays(-1);

            return await _context.ExternalIntegrationOAuthStates
                .Where(s => s.ExpiresAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken);
        }

        private static readonly ExternalIntegrationEventStatus[] Finished =
        [
            ExternalIntegrationEventStatus.Processed,
            ExternalIntegrationEventStatus.Ignored,
            ExternalIntegrationEventStatus.Failed
        ];

        public async Task<int> PruneOldEventsAsync(CancellationToken cancellationToken = default)
        {
            if (_options.EventRetentionDays <= 0)
                return 0;

            var cutoff = DateTime.UtcNow.AddDays(-_options.EventRetentionDays);

            // Only rows that already reached a final state and are older than the cutoff. A
            // Lead this event produced is untouched — LeadId is a nullable, non-cascading
            // reference on the event, never the other way around, so pruning the webhook's
            // audit trail can never take the lead or its submission history down with it.
            return await _context.ExternalIntegrationEvents
                .Where(e => e.Provider == IntegrationProviders.Meta
                            && Finished.Contains(e.Status)
                            && e.ReceivedAt < cutoff)
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
