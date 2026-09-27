using System.Text.Json;
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
    /// Recovers Meta leads the webhook never delivered: those submitted before a Page was enabled,
    /// during an outage longer than Meta's 36 hours of retries, or while a subscription was broken.
    ///
    /// It creates no lead itself. Each lead becomes an event under exactly the key the webhook
    /// would have given it, so a lead the webhook already delivered is recognised rather than
    /// queued twice, and everything after that — the fetch, duplicates, holds, attribution,
    /// notifications — is the event processor's, unchanged.
    /// </summary>
    public sealed class MetaLeadBackfillService : IMetaLeadBackfillService
    {
        /// <summary>Tells a recovered lead apart from a delivered one in the event list; processed identically.</summary>
        public const string BackfillEventType = "leadgen_backfill";

        /// <summary>
        /// Extra times reconciliation may put one Failed event back on the queue after its own
        /// attempts are used up. Three rounds, then it stays Failed for a person to retry.
        /// </summary>
        public const int MaxAutomaticRequeues = 3;

        /// <summary>
        /// Leads newer than this are left to the webhook, which normally delivers within seconds.
        /// Reading right up to now would queue a lead just before its webhook arrives, and that
        /// webhook would then be dropped as a duplicate, never counting as a delivery.
        /// </summary>
        public const int WebhookGraceMinutes = 15;

        /// <summary>A window with more leads than one read allows is halved until it fits or is this short.</summary>
        private static readonly TimeSpan SmallestWindow = TimeSpan.FromHours(1);

        private readonly AppDbContext _context;
        private readonly IMetaGraphClient _graph;
        private readonly IIntegrationSecretProtector _protector;
        private readonly MetaIntegrationOptions _options;
        private readonly ILogger<MetaLeadBackfillService> _logger;

        public MetaLeadBackfillService(
            AppDbContext context,
            IMetaGraphClient graph,
            IIntegrationSecretProtector protector,
            MetaIntegrationOptions options,
            ILogger<MetaLeadBackfillService> logger)
        {
            _context = context;
            _graph = graph;
            _protector = protector;
            _options = options;
            _logger = logger;
        }

        public async Task<MetaLeadImportResultDto> ImportAsync(
            int connectionId, ImportMetaLeadsDto dto, LeadUserContext actor, CancellationToken cancellationToken = default)
        {
            if (!actor.IsAdmin && !actor.IsManager)
                throw new LeadAuthorizationException("Only an Admin or Sales Manager can import Meta leads.");

            var hasPage = dto.ResourceId.HasValue;
            var hasForm = !string.IsNullOrWhiteSpace(dto.FormExternalId);
            if (hasPage == hasForm)
                throw new InvalidOperationException("Choose either a Page or one lead form to import.");

            // Meta keeps a lead readable for 90 days, so anything earlier would silently come back
            // empty and read as "there were none".
            var since = PakistanTime.StartOfBusinessDateUtc(dto.Since.ToDateTime(TimeOnly.MinValue));
            var now = DateTime.UtcNow;
            if (since > now)
                throw new InvalidOperationException("Choose a date that is not in the future.");
            if (since < now.AddDays(-MetaIntegrationOptions.MaxImportDays))
                throw new InvalidOperationException(
                    $"Meta keeps leads for {MetaIntegrationOptions.MaxImportDays} days, so an import can go back at most that far.");

            var connection = await _context.ExternalIntegrationConnections
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == connectionId && c.Provider == IntegrationProviders.Meta, cancellationToken)
                ?? throw new LeadNotFoundException("That Meta connection no longer exists.");
            if (connection.Status == ExternalIntegrationConnectionStatus.Disconnected)
                throw new InvalidOperationException("Reconnect this Meta account before importing its leads.");

            var pages = await EnabledPagesAsync(connectionId, cancellationToken);
            ExternalIntegrationResource page;
            List<string> formIds;

            if (hasPage)
            {
                page = pages.FirstOrDefault(p => p.Id == dto.ResourceId)
                       ?? throw new InvalidOperationException(
                           "Enable this Page for lead delivery before importing its leads: an import from a Page that is off would be ignored.");
                formIds = await FormIdsAsync(connectionId, [page.ExternalId], cancellationToken);
                if (formIds.Count == 0)
                    throw new InvalidOperationException("No lead forms are on file for this Page yet. Run Sync now first.");
            }
            else
            {
                var formId = dto.FormExternalId!.Trim();
                var form = await _context.ExternalIntegrationResources
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.ExternalIntegrationConnectionId == connectionId
                                              && r.ResourceType == ExternalResourceTypes.LeadForm
                                              && r.ExternalId == formId, cancellationToken)
                    ?? throw new LeadNotFoundException("That lead form does not belong to this connection.");
                page = pages.FirstOrDefault(p => p.ExternalId == form.ParentExternalId)
                       ?? throw new InvalidOperationException(
                           "Enable this form's Page for lead delivery before importing its leads.");
                formIds = [form.ExternalId];
            }

            var run = new Run();
            await ImportFormsAsync(connection, page, formIds, since, WindowEnd(now), run, cancellationToken);
            return run.Result;
        }

        public async Task<MetaLeadImportResultDto> ReconcileAsync(int connectionId, CancellationToken cancellationToken = default)
        {
            var run = new Run();
            if (_options.ReconciliationLookbackHours <= 0)
                return run.Result;

            var connection = await _context.ExternalIntegrationConnections
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == connectionId && c.Provider == IntegrationProviders.Meta, cancellationToken);
            if (connection is null || connection.Status != ExternalIntegrationConnectionStatus.Connected)
                return run.Result;

            var now = DateTime.UtcNow;
            // The form scan below only asks Meta for the lookback window (48 hours by default).
            // A lead that failed on Monday is no longer in that window by Wednesday, so recovery
            // cannot depend on Meta returning it. The event already stores the lead id; the
            // processor asks Meta for that id again. Up to three rounds, and only while the lead
            // is still inside the 90 days Meta keeps it.
            await RequeueStoredTransientFailuresAsync(connectionId, cancellationToken);

            var since = now.AddHours(-_options.ReconciliationLookbackHours);
            foreach (var page in await EnabledPagesAsync(connectionId, cancellationToken))
            {
                var formIds = await FormIdsAsync(connectionId, [page.ExternalId], cancellationToken);
                await ImportFormsAsync(connection, page, formIds, since, WindowEnd(now), run, cancellationToken);
            }

            await RecordReconciliationAsync(connectionId, run, now, cancellationToken);
            return run.Result;
        }

        /// <summary>
        /// Kept on the connection because the scheduled sync has nobody to show its result to:
        /// MetaIntegrationAlertService reads it and tells every Admin when reconciliation keeps
        /// failing, or finds leads the webhook never delivered.
        /// </summary>
        private async Task RecordReconciliationAsync(int connectionId, Run run, DateTime now, CancellationToken cancellationToken)
        {
            var connection = await _context.ExternalIntegrationConnections
                .FirstOrDefaultAsync(c => c.Id == connectionId, cancellationToken);
            if (connection is null)
                return;

            var error = MetaCredentialScrubber.ScrubAndLimit(run.Result.Warning, 1000);
            connection.ReconciledAt = now;
            connection.ReconciliationError = error;
            connection.ReconciliationFailingSince = error is null ? null : connection.ReconciliationFailingSince ?? now;
            connection.ReconciliationMissedLeads = run.NeverDelivered;

            if (error is not null)
                _logger.LogWarning("Reconciling Meta leads for connection {ConnectionId} was incomplete: {Reason}", connectionId, error);
            if (run.NeverDelivered > 0)
                _logger.LogWarning(
                    "Reconciliation found {Count} Meta lead(s) the webhook never delivered for connection {ConnectionId}.",
                    run.NeverDelivered, connectionId);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Changed meanwhile — reconnected or disconnected. The next reconciliation records
                // its own outcome; a stale copy left tracked would fail the next save in this scope.
                _context.Entry(connection).State = EntityState.Detached;
            }
        }

        private static DateTime WindowEnd(DateTime now) => now.AddMinutes(-WebhookGraceMinutes);

        /// <summary>One import or reconciliation's counts, plus what only reconciliation keeps.</summary>
        private sealed class Run
        {
            public MetaLeadImportResultDto Result { get; } = new();

            /// <summary>Queued under a key the webhook never recorded, as opposed to reopened after it was Ignored.</summary>
            public int NeverDelivered { get; set; }
        }

        private Task<List<ExternalIntegrationResource>> EnabledPagesAsync(int connectionId, CancellationToken cancellationToken) =>
            _context.ExternalIntegrationResources
                .AsNoTracking()
                .Where(r => r.ExternalIntegrationConnectionId == connectionId
                            && r.ResourceType == ExternalResourceTypes.FacebookPage
                            && r.IsEnabled
                            && r.IsActive)
                .ToListAsync(cancellationToken);

        private Task<List<string>> FormIdsAsync(int connectionId, List<string> pageIds, CancellationToken cancellationToken) =>
            _context.ExternalIntegrationResources
                .AsNoTracking()
                .Where(r => r.ExternalIntegrationConnectionId == connectionId
                            && r.ResourceType == ExternalResourceTypes.LeadForm
                            && r.IsActive
                            && r.ParentExternalId != null
                            && pageIds.Contains(r.ParentExternalId))
                .Select(r => r.ExternalId)
                .ToListAsync(cancellationToken);

        /// <summary>
        /// Reads each form with the Page's own token — the one leads are fetched with — and never
        /// changes the connection's status: a refusal here is reported, and the event processor
        /// remains what decides whether the Page token itself still works.
        /// </summary>
        private async Task ImportFormsAsync(
            ExternalIntegrationConnection connection,
            ExternalIntegrationResource page,
            List<string> formIds,
            DateTime since,
            DateTime until,
            Run run,
            CancellationToken cancellationToken)
        {
            var result = run.Result;
            var token = _protector.TryUnprotect(page.ResourceTokenProtected)
                        ?? _protector.TryUnprotect(connection.AccessTokenProtected);
            if (token is null)
            {
                result.Failed += formIds.Count;
                result.Warning = Combine(result.Warning,
                    $"The stored credential for \"{page.Name ?? page.ExternalId}\" could not be read; reconnect the account.");
                _logger.LogWarning(
                    "The stored credential for Meta Page {PageId} on connection {ConnectionId} could not be read, so its leads were not read.",
                    page.ExternalId, connection.Id);
                return;
            }

            foreach (var formId in formIds)
            {
                List<MetaLead> leads;
                try
                {
                    leads = await ReadWindowAsync(formId, since, until, token, result, cancellationToken);
                }
                catch (MetaGraphException ex)
                {
                    result.Failed++;
                    result.Warning = Combine(result.Warning, ex is MetaAuthorizationException
                        ? $"Meta did not allow reading the leads of form {formId}: {ex.Message}"
                        : $"The leads of form {formId} could not be read: {ex.Message}");
                    _logger.LogWarning(ex, "Reading the leads of Meta form {FormId} failed.", formId);
                    continue;
                }

                var unqueued = 0;
                foreach (var lead in leads)
                {
                    result.Found++;
                    Outcome outcome;
                    try
                    {
                        outcome = await RecordAsync(page, formId, lead, cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // One lead the database would not take must not cost the rest, nor the
                        // counts of what was already queued.
                        _logger.LogError(ex, "Queuing Meta lead {LeadgenId} of form {FormId} failed.", lead.LeadgenId, formId);
                        DetachUnsavedEvents();
                        unqueued++;
                        outcome = Outcome.Failed;
                    }

                    switch (outcome)
                    {
                        case Outcome.Queued or Outcome.Reopened:
                            result.New++;
                            if (outcome == Outcome.Queued)
                                run.NeverDelivered++;
                            if (lead.CreatedTime is { } submittedAt
                                && MetaLeadEventProcessor.IsOlderThanAlertCutoff(submittedAt, _options))
                                result.AddedWithoutAlert++;
                            break;
                        case Outcome.AlreadyInDams: result.AlreadyInDams++; break;
                        case Outcome.PreviouslyFailed: result.PreviouslyFailed++; break;
                        default: result.Failed++; break;
                    }
                }

                if (unqueued > 0)
                    result.Warning = Combine(result.Warning,
                        $"{unqueued} lead(s) of form {formId} could not be queued; run the import again to retry them.");
            }
        }

        /// <summary>
        /// A form's leads submitted after <paramref name="since"/> and before <paramref name="until"/>.
        /// A window holding more than one read allows (MaxGraphPages) is halved and each half read
        /// on its own, rather than trusting the order Meta returns them in, so no part of the window
        /// is skipped. Only an hour with more leads than one read allows is reported as incomplete.
        /// </summary>
        private async Task<List<MetaLead>> ReadWindowAsync(
            string formId, DateTime since, DateTime until, string token, MetaLeadImportResultDto result,
            CancellationToken cancellationToken)
        {
            since = WholeSeconds(since);
            until = WholeSeconds(until);
            if (since >= until)
                return [];

            var read = await _graph.GetFormLeadsAsync(formId, since, until, token, cancellationToken);
            if (!read.Truncated)
                return read.Leads;

            if (until - since <= SmallestWindow)
            {
                result.Warning = Combine(result.Warning,
                    $"Form {formId} received more leads within one hour on {PakistanTime.ToBusinessDate(since):d MMM yyyy} " +
                    "than one read allows; some of them could not be read.");
                return read.Leads;
            }

            // Meta filters to the second, strictly on both sides: the older half runs to just past
            // the middle second and the newer half starts after it, so each lead is read once.
            var middle = WholeSeconds(since + (until - since) / 2);
            var older = await ReadWindowAsync(formId, since, middle.AddSeconds(1), token, result, cancellationToken);
            var newer = await ReadWindowAsync(formId, middle, until, token, result, cancellationToken);
            return [.. older, .. newer];
        }

        private static DateTime WholeSeconds(DateTime value) =>
            new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, value.Kind);

        /// <summary>What a failed save left tracked would otherwise ride along with the next lead's save.</summary>
        private void DetachUnsavedEvents()
        {
            foreach (var entry in _context.ChangeTracker.Entries<ExternalIntegrationEvent>()
                         .Where(e => e.State is EntityState.Added or EntityState.Modified)
                         .ToList())
                entry.State = EntityState.Detached;
        }

        private enum Outcome { Queued, Reopened, AlreadyInDams, PreviouslyFailed, Failed }

        /// <summary>
        /// One event per lead, saved on its own so a clash on one never costs the others. An event
        /// the webhook recorded as Ignored — the Page was off when the lead arrived — is exactly
        /// what this exists to recover, so it is reopened rather than counted as already handled,
        /// and becomes a recovery: an old one is then added without a "new lead" alert. A Failed
        /// event is not in DAMS either, but retrying it is not this scan's job: the form read only
        /// covers the lookback window, and a lead outside it would be missed. Reconciliation
        /// requeues transient failures from the events DAMS already stored
        /// (<see cref="RequeueStoredTransientFailuresAsync"/>). Here a Failed event is counted
        /// apart, never as already in DAMS, and left as it is.
        /// </summary>
        private async Task<Outcome> RecordAsync(
            ExternalIntegrationResource page, string formId, MetaLead lead, CancellationToken cancellationToken)
        {
            if (lead.LeadgenId.Length > MetaLeadEventProcessor.MaxExternalIdLength)
                return Outcome.Failed;

            var eventKey = MetaWebhookIntakeService.EventKey(page.ExternalIntegrationConnectionId, page.ExternalId, lead.LeadgenId);
            if (eventKey.Length > 300)
                return Outcome.Failed;

            var existing = await _context.ExternalIntegrationEvents
                .FirstOrDefaultAsync(e => e.Provider == IntegrationProviders.Meta && e.EventKey == eventKey, cancellationToken);

            if (existing is not null)
            {
                if (existing.Status == ExternalIntegrationEventStatus.Failed)
                    return Outcome.PreviouslyFailed;
                if (existing.Status != ExternalIntegrationEventStatus.Ignored)
                    return Outcome.AlreadyInDams;

                existing.EventType = BackfillEventType;
                existing.ExternalIntegrationConnectionId = page.ExternalIntegrationConnectionId;
                existing.ExternalIntegrationResourceId = page.Id;
                existing.Status = ExternalIntegrationEventStatus.Pending;
                existing.Attempts = 0;
                existing.AvailableAt = DateTime.UtcNow;
                existing.ProcessedAt = null;
                existing.LastError = null;
                existing.LockedUntil = null;
                existing.LockedBy = null;
                return await SaveAsync(existing, Outcome.Reopened, cancellationToken);
            }

            // Arrived some other way — through another connection before this one owned the Page.
            if (await _context.LeadExternalSubmissions.AnyAsync(
                    s => s.Provider == IntegrationProviders.Meta && s.ExternalLeadId == lead.LeadgenId, cancellationToken))
                return Outcome.AlreadyInDams;

            var added = new ExternalIntegrationEvent
            {
                Provider = IntegrationProviders.Meta,
                ExternalIntegrationConnectionId = page.ExternalIntegrationConnectionId,
                ExternalIntegrationResourceId = page.Id,
                EventType = BackfillEventType,
                EventKey = eventKey,
                ResourceExternalId = page.ExternalId,
                // The webhook's own shape, so the processor reads the leadgen and ad ids the same way.
                RawPayloadJson = JsonSerializer.Serialize(new
                {
                    leadgen_id = lead.LeadgenId,
                    page_id = page.ExternalId,
                    form_id = lead.FormId ?? formId,
                    ad_id = lead.AdId,
                    created_time = lead.CreatedTime is { } created
                        ? new DateTimeOffset(DateTime.SpecifyKind(created, DateTimeKind.Utc)).ToUnixTimeSeconds()
                        : (long?)null
                }),
                ReceivedAt = DateTime.UtcNow,
                Status = ExternalIntegrationEventStatus.Pending,
                AvailableAt = DateTime.UtcNow
            };
            _context.ExternalIntegrationEvents.Add(added);
            return await SaveAsync(added, Outcome.Queued, cancellationToken);
        }

        /// <summary>
        /// Puts transient failures back on the queue from the rows DAMS already has. One save
        /// each, so a row someone else changed does not take the rest of the round with it.
        /// A Page that is off or gone is left Failed: the processor would only ignore it, and
        /// Retry now is what tells an operator why it is still there.
        /// </summary>
        private async Task RequeueStoredTransientFailuresAsync(int connectionId, CancellationToken cancellationToken)
        {
            var oldest = DateTime.UtcNow.AddDays(-MetaIntegrationOptions.MaxImportDays);
            var failed = await _context.ExternalIntegrationEvents
                .Include(e => e.Resource)
                .Where(e => e.ExternalIntegrationConnectionId == connectionId
                            && e.Provider == IntegrationProviders.Meta
                            && e.Status == ExternalIntegrationEventStatus.Failed
                            && e.FailureWasTransient
                            && e.RequeueCount < MaxAutomaticRequeues
                            && e.ReceivedAt >= oldest)
                .ToListAsync(cancellationToken);

            foreach (var existing in failed)
            {
                if (existing.Resource is not { IsEnabled: true, IsActive: true })
                    continue;
                if (!CanRequeue(existing, SubmittedAt(existing)))
                    continue;

                Requeue(existing);
                try
                {
                    await _context.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Someone else moved this row. Leave it; the next reconciliation reads it again.
                    _context.Entry(existing).State = EntityState.Detached;
                }
            }
        }

        /// <summary>
        /// Unix <c>created_time</c> on the stored webhook value, when Meta sent one. That is the
        /// lead's age. Absent, <see cref="CanRequeue"/> uses when the event arrived.
        /// </summary>
        private static DateTime? SubmittedAt(ExternalIntegrationEvent existing)
        {
            if (string.IsNullOrWhiteSpace(existing.RawPayloadJson))
                return null;

            try
            {
                using var document = JsonDocument.Parse(existing.RawPayloadJson);
                if (!document.RootElement.TryGetProperty("created_time", out var value))
                    return null;

                var unix = value.ValueKind switch
                {
                    JsonValueKind.Number when value.TryGetInt64(out var seconds) => seconds,
                    JsonValueKind.String when long.TryParse(value.GetString(), out var seconds) => seconds,
                    _ => 0L
                };
                return unix > 0 ? DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// A transient failure, still inside the extra rounds, and a lead Meta can still return.
        /// The lead's own submission time is what "younger than 90 days" means; when Meta did not
        /// send one, the event's arrival is the only age we have.
        /// </summary>
        private static bool CanRequeue(ExternalIntegrationEvent existing, DateTime? submittedAt)
        {
            if (!existing.FailureWasTransient || existing.RequeueCount >= MaxAutomaticRequeues)
                return false;

            var ageFrom = submittedAt ?? existing.ReceivedAt;
            return ageFrom >= DateTime.UtcNow.AddDays(-MetaIntegrationOptions.MaxImportDays);
        }

        /// <summary>
        /// Back on the queue with a fresh set of attempts. This is not an operator retry: it does
        /// not write a retry audit or increment <see cref="ExternalIntegrationEvent.RetryCount"/>.
        /// </summary>
        private static void Requeue(ExternalIntegrationEvent existing)
        {
            existing.Status = ExternalIntegrationEventStatus.Pending;
            existing.Attempts = 0;
            existing.AvailableAt = DateTime.UtcNow;
            existing.ProcessedAt = null;
            existing.LastError = null;
            existing.LockedUntil = null;
            existing.LockedBy = null;
            existing.FailureWasTransient = false;
            existing.RequeueCount++;
        }

        /// <summary>A concurrent webhook or sweep that saved the same event first is the same lead, already in hand.</summary>
        private async Task<Outcome> SaveAsync(
            ExternalIntegrationEvent integrationEvent, Outcome saved, CancellationToken cancellationToken)
        {
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return saved;
            }
            catch (DbUpdateConcurrencyException)
            {
                _context.Entry(integrationEvent).State = EntityState.Detached;
                return Outcome.AlreadyInDams;
            }
            catch (DbUpdateException ex) when (MetaWebhookIntakeService.IsDuplicateEventKey(ex))
            {
                _context.Entry(integrationEvent).State = EntityState.Detached;
                return Outcome.AlreadyInDams;
            }
        }

        private static string Combine(string? existing, string addition) =>
            string.IsNullOrWhiteSpace(existing) ? addition : $"{existing} {addition}";
    }
}
