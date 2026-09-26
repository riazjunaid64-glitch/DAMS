using DAMS.Application.Common;
using DAMS.Application.DTOs.IntegrationDtos;
using DAMS.Domain.Enums;

namespace DAMS.Application.Interfaces
{
    /// <summary>
    /// Admin-facing operations for the Meta connection: starting and completing OAuth,
    /// listing what was discovered, choosing what is live, and disconnecting.
    /// </summary>
    public interface IMetaIntegrationService
    {
        Task<MetaConnectStartDto> StartConnectAsync(
            LeadUserContext actor, string? returnPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Consumes the OAuth state and exchanges the code. Returns the CRM path to redirect
        /// to, already carrying a success or error indicator — never a credential.
        /// </summary>
        Task<string> CompleteCallbackAsync(
            string? code, string? state, string? error, CancellationToken cancellationToken = default);

        Task<List<MetaConnectionDto>> GetConnectionsAsync(CancellationToken cancellationToken = default);

        Task<List<MetaResourceGroupDto>> GetResourcesAsync(int connectionId, CancellationToken cancellationToken = default);

        Task<MetaResourceDto> SetResourceEnabledAsync(
            int connectionId, int resourceId, bool isEnabled, CancellationToken cancellationToken = default);

        /// <summary>A discovered lead form's questions and what an administrator has mapped them to.</summary>
        Task<LeadFormMappingDto> GetLeadFormMappingAsync(string formExternalId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Links a lead form to a project and its answers to lead fields, for submissions that
        /// arrive from now on. An empty mapping removes it.
        /// </summary>
        Task<LeadFormMappingDto> SaveLeadFormMappingAsync(
            string formExternalId, SaveLeadFormMappingDto dto, LeadUserContext actor,
            CancellationToken cancellationToken = default);

        Task<List<MetaEventDto>> GetEventsAsync(
            int connectionId, int take, ExternalIntegrationEventStatus? status = null,
            CancellationToken cancellationToken = default);

        Task<MetaEventDto> RetryEventAsync(
            int connectionId, int eventId, LeadUserContext actor, CancellationToken cancellationToken = default);

        Task DisconnectAsync(int connectionId, LeadUserContext actor, CancellationToken cancellationToken = default);
    }

    /// <summary>Discovers and refreshes the assets inside a connection.</summary>
    public interface IMetaResourceSyncService
    {
        Task<MetaSyncResultDto> SyncConnectionAsync(int connectionId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Same as <see cref="SyncConnectionAsync"/>, but under the same per-connection lease the
        /// background sweep uses — for an admin-triggered "Sync now" that must not run
        /// concurrently with either the background worker or another admin's own click.
        /// </summary>
        Task<MetaSyncResultDto> SyncNowAsync(int connectionId, CancellationToken cancellationToken = default);

        /// <summary>Syncs every connection whose last sync is older than the configured interval.</summary>
        Task<int> SyncDueConnectionsAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Recovers leads the webhook never delivered by reading each lead form's own edge, and hands
    /// every one to the normal event processor as a <c>leadgen_backfill</c> event.
    /// </summary>
    public interface IMetaLeadBackfillService
    {
        /// <summary>An Admin's "import leads since…" for one Page or one form, at most 90 days back.</summary>
        Task<MetaLeadImportResultDto> ImportAsync(
            int connectionId, ImportMetaLeadsDto dto, LeadUserContext actor, CancellationToken cancellationToken = default);

        /// <summary>
        /// Looks back MetaIntegration:ReconciliationLookbackHours on every enabled Page's forms.
        /// Never throws for a Meta failure: what went wrong comes back as the result's warning.
        /// </summary>
        Task<MetaLeadImportResultDto> ReconcileAsync(int connectionId, CancellationToken cancellationToken = default);
    }

    /// <summary>Tells every Admin, through the notification platform, when Meta lead capture needs a person.</summary>
    public interface IMetaIntegrationAlertService
    {
        /// <summary>
        /// Raises the alert each connection's current state calls for — needs reconnecting, sign-in
        /// about to expire, lead events failed, and (when configured) a Page gone quiet. Safe to run
        /// as often as wanted: each alert is raised once per Admin. Returns how many were created.
        /// </summary>
        Task<int> RaiseAlertsAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>Accepts a verified webhook payload and records it for later processing.</summary>
    public interface IMetaWebhookIntakeService
    {
        /// <summary>Returns how many events were newly persisted; replays add none.</summary>
        Task<int> RecordAsync(string rawBody, CancellationToken cancellationToken = default);
    }

    /// <summary>Drains the durable event inbox, turning provider events into DAMS leads.</summary>
    public interface IMetaLeadEventProcessor
    {
        Task<int> ProcessPendingEventsAsync(int batchSize, CancellationToken cancellationToken = default);

        /// <summary>Removes expired OAuth states.</summary>
        Task<int> PruneOAuthStatesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes finished (Processed/Ignored/Failed) events older than
        /// MetaIntegration:EventRetentionDays. A no-op while that setting is 0 — events are kept
        /// forever by default, since how long a raw webhook payload should be kept is a data
        /// retention decision for the business, not something to default silently.
        /// </summary>
        Task<int> PruneOldEventsAsync(CancellationToken cancellationToken = default);
    }
}
