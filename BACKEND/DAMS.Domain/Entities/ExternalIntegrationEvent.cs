using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>
    /// Durable inbox for provider webhooks. The webhook endpoint does nothing but verify the
    /// signature and write one of these; every expensive step — fetching the lead, mapping it,
    /// creating it — happens later in the background worker.
    ///
    /// <see cref="EventKey"/> is unique per provider, so however many times a provider retries
    /// the same delivery, there is only ever one unit of work.
    /// </summary>
    public class ExternalIntegrationEvent
    {
        public int Id { get; set; }

        public string Provider { get; set; } = string.Empty;

        /// <summary>Null when the payload arrived for a page no connection owns.</summary>
        public int? ExternalIntegrationConnectionId { get; set; }

        public int? ExternalIntegrationResourceId { get; set; }

        public string EventType { get; set; } = string.Empty;

        /// <summary>Connection + resource + provider event id, combined. The deduplication key.</summary>
        public string EventKey { get; set; } = string.Empty;

        /// <summary>The provider id off the wire, kept even when it resolves to nothing.</summary>
        public string? ResourceExternalId { get; set; }

        public string RawPayloadJson { get; set; } = string.Empty;

        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

        public ExternalIntegrationEventStatus Status { get; set; } = ExternalIntegrationEventStatus.Pending;

        public int Attempts { get; set; }

        /// <summary>Not eligible for processing before this instant — how backoff is expressed.</summary>
        public DateTime AvailableAt { get; set; } = DateTime.UtcNow;

        public DateTime? LockedUntil { get; set; }

        public string? LockedBy { get; set; }

        public DateTime? ProcessedAt { get; set; }

        /// <summary>The lead this event produced, once it has produced one.</summary>
        public int? LeadId { get; set; }

        /// <summary>Sanitised message only — credentials are scrubbed before anything is stored here.</summary>
        public string? LastError { get; set; }

        /// <summary>Number of operator-requested recovery attempts.</summary>
        public int RetryCount { get; set; }

        /// <summary>When an operator last moved this event back to the processing queue.</summary>
        public DateTime? LastRetriedAt { get; set; }

        /// <summary>The DAMS user who last requested recovery.</summary>
        public int? LastRetriedByUserId { get; set; }

        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public ExternalIntegrationConnection? Connection { get; set; }

        public ExternalIntegrationResource? Resource { get; set; }
    }
}
