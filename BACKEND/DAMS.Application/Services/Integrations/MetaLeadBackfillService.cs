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
            if (!actor.IsAdmin)
                throw new LeadAuthorizationException("Only an Admin can import Meta leads.");

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

            var result = new MetaLeadImportResultDto();
            await ImportFormsAsync(connection, page, formIds, since, result, cancellationToken);
            return result;
        }

        public async Task<MetaLeadImportResultDto> ReconcileAsync(int connectionId, CancellationToken cancellationToken = default)
        {
            var result = new MetaLeadImportResultDto();
            if (_options.ReconciliationLookbackHours <= 0)
                return result;

            var connection = await _context.ExternalIntegrationConnections
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == connectionId && c.Provider == IntegrationProviders.Meta, cancellationToken);
            if (connection is null || connection.Status != ExternalIntegrationConnectionStatus.Connected)
                return result;

            var since = DateTime.UtcNow.AddHours(-_options.ReconciliationLookbackHours);
            foreach (var page in await EnabledPagesAsync(connectionId, cancellationToken))
            {
                var formIds = await FormIdsAsync(connectionId, [page.ExternalId], cancellationToken);
                await ImportFormsAsync(connection, page, formIds, since, result, cancellationToken);
            }

            return result;
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
            MetaLeadImportResultDto result,
            CancellationToken cancellationToken)
        {
            var token = _protector.TryUnprotect(page.ResourceTokenProtected)
                        ?? _protector.TryUnprotect(connection.AccessTokenProtected);
            if (token is null)
            {
                result.Failed += formIds.Count;
                result.Warning = Combine(result.Warning,
                    $"The stored credential for \"{page.Name ?? page.ExternalId}\" could not be read; reconnect the account.");
                return;
            }

            foreach (var formId in formIds)
            {
                MetaFormLeadPage leads;
                try
                {
                    leads = await _graph.GetFormLeadsAsync(formId, since, token, cancellationToken);
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

                if (leads.Truncated)
                    result.Warning = Combine(result.Warning,
                        $"Form {formId} has more leads in this window than one read allows; import a shorter window for the rest.");

                foreach (var lead in leads.Leads)
                {
                    result.Found++;
                    switch (await RecordAsync(page, formId, lead, cancellationToken))
                    {
                        case Outcome.Queued: result.New++; break;
                        case Outcome.AlreadyInDams: result.AlreadyInDams++; break;
                        default: result.Failed++; break;
                    }
                }
            }
        }

        private enum Outcome { Queued, AlreadyInDams, Unstorable }

        /// <summary>
        /// One event per lead, saved on its own so a clash on one never costs the others. An event
        /// the webhook recorded as Ignored — the Page was off when the lead arrived — is exactly
        /// what this exists to recover, so it is reopened rather than counted as already handled.
        /// </summary>
        private async Task<Outcome> RecordAsync(
            ExternalIntegrationResource page, string formId, MetaLead lead, CancellationToken cancellationToken)
        {
            if (lead.LeadgenId.Length > MetaLeadEventProcessor.MaxExternalIdLength)
                return Outcome.Unstorable;

            var eventKey = MetaWebhookIntakeService.EventKey(page.ExternalIntegrationConnectionId, page.ExternalId, lead.LeadgenId);
            if (eventKey.Length > 300)
                return Outcome.Unstorable;

            var existing = await _context.ExternalIntegrationEvents
                .FirstOrDefaultAsync(e => e.Provider == IntegrationProviders.Meta && e.EventKey == eventKey, cancellationToken);

            if (existing is not null)
            {
                if (existing.Status != ExternalIntegrationEventStatus.Ignored)
                    return Outcome.AlreadyInDams;

                existing.ExternalIntegrationConnectionId = page.ExternalIntegrationConnectionId;
                existing.ExternalIntegrationResourceId = page.Id;
                existing.Status = ExternalIntegrationEventStatus.Pending;
                existing.Attempts = 0;
                existing.AvailableAt = DateTime.UtcNow;
                existing.ProcessedAt = null;
                existing.LastError = null;
                existing.LockedUntil = null;
                existing.LockedBy = null;
                return await SaveAsync(existing, cancellationToken);
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
            return await SaveAsync(added, cancellationToken);
        }

        /// <summary>A concurrent webhook or sweep that saved the same event first is the same lead, already in hand.</summary>
        private async Task<Outcome> SaveAsync(ExternalIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return Outcome.Queued;
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
