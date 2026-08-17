using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>
    /// One connected provider account — a Meta login today, a Google Ads login tomorrow.
    /// Credentials live here and nowhere else, always encrypted, and are never projected
    /// into a DTO, a log line or an exception message.
    ///
    /// A disconnected connection is kept forever: leads captured through it still point at
    /// it for attribution, so the row is disabled rather than deleted.
    /// </summary>
    public class ExternalIntegrationConnection
    {
        public int Id { get; set; }

        /// <summary>Free-text provider key ("meta"), never an enum — a new provider must not need a migration.</summary>
        public string Provider { get; set; } = string.Empty;

        /// <summary>The provider's own id for the authorising user or business, when it exposes one.</summary>
        public string? ExternalAccountId { get; set; }

        public string DisplayName { get; set; } = string.Empty;

        public ExternalIntegrationConnectionStatus Status { get; set; } = ExternalIntegrationConnectionStatus.Connected;

        /// <summary>Long-lived access token, protected at rest. Never plain text.</summary>
        public string? AccessTokenProtected { get; set; }

        public DateTime? TokenExpiresAt { get; set; }

        /// <summary>Scopes the provider actually granted, which may be fewer than those requested.</summary>
        public string? GrantedScopesJson { get; set; }

        public int? ConnectedByUserId { get; set; }

        public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;

        public DateTime? LastValidatedAt { get; set; }

        /// <summary>Null means the first resource discovery has not run yet.</summary>
        public DateTime? LastSyncedAt { get; set; }

        /// <summary>
        /// A short lease taken by whichever background worker is currently running this
        /// connection's resource sync, so a second instance polling the same "due" connection
        /// list does not run a concurrent, duplicate sync against it. Released the moment that
        /// sync finishes — success or failure — so a failed sync is retried on the very next
        /// tick rather than waiting out the lease.
        /// </summary>
        public DateTime? SyncLockedUntil { get; set; }

        public string? SyncLockedBy { get; set; }

        public DateTime? LastErrorAt { get; set; }

        /// <summary>Sanitised message only — credentials are scrubbed before anything is stored here.</summary>
        public string? LastError { get; set; }

        public DateTime? DisconnectedAt { get; set; }

        public int? DisconnectedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public ICollection<ExternalIntegrationResource> Resources { get; set; } = new List<ExternalIntegrationResource>();
    }
}
