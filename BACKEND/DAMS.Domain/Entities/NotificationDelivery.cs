using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>
    /// One notification on one channel. Unique per (notification, channel), which is what
    /// stops a retry of a failed email from re-sending a push that already worked, and what
    /// makes a duplicated provider callback a no-op.
    /// </summary>
    public class NotificationDelivery
    {
        public int Id { get; set; }

        public int NotificationId { get; set; }

        public NotificationChannel Channel { get; set; }

        public NotificationDeliveryStatus Status { get; set; } = NotificationDeliveryStatus.Pending;

        /// <summary>Earliest instant a worker may pick this up. Drives both scheduling and
        /// the exponential back-off between retries.</summary>
        public DateTime AvailableAt { get; set; } = DateTime.UtcNow;

        public int AttemptCount { get; set; }

        public DateTime? LastAttemptAt { get; set; }

        public DateTime? ProcessingStartedAt { get; set; }

        public DateTime? SentAt { get; set; }

        /// <summary>Only set from a verified provider signal, never from a successful send.</summary>
        public DateTime? DeliveredAt { get; set; }

        public DateTime? FailedAt { get; set; }

        /// <summary>Where the message went (masked address / subscription fingerprint). Safe
        /// to show an admin; never the full push key material.</summary>
        public string? Target { get; set; }

        /// <summary>Provider-side identifier, when the provider gives one.</summary>
        public string? ProviderReference { get; set; }

        public string? FailureReason { get; set; }

        /// <summary>True once retrying is pointless (bad address, gone subscription, template
        /// disabled). Permanent failures stay visible to admins and stop consuming attempts.</summary>
        public bool IsPermanentFailure { get; set; }

        /// <summary>Worker lease. A crashed worker's rows become claimable again once this
        /// passes, which is what makes restart recovery automatic.</summary>
        public DateTime? LockedUntil { get; set; }

        public string? LockedBy { get; set; }

        /// <summary>Optimistic concurrency: two workers cannot claim the same row.</summary>
        public byte[]? RowVersion { get; set; }

        public Notification Notification { get; set; } = null!;
    }
}
