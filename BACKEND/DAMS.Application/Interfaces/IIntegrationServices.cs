using DAMS.Application.Common;
using DAMS.Application.DTOs.IntegrationDtos;

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

        Task<List<MetaEventDto>> GetEventsAsync(int connectionId, int take, CancellationToken cancellationToken = default);

        Task DisconnectAsync(int connectionId, LeadUserContext actor, CancellationToken cancellationToken = default);
    }

    /// <summary>Discovers and refreshes the assets inside a connection.</summary>
    public interface IMetaResourceSyncService
    {
        Task<MetaSyncResultDto> SyncConnectionAsync(int connectionId, CancellationToken cancellationToken = default);

        /// <summary>Syncs every connection whose last sync is older than the configured interval.</summary>
        Task<int> SyncDueConnectionsAsync(CancellationToken cancellationToken = default);
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
