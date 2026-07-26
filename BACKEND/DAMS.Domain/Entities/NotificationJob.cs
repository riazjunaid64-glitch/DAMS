using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>
    /// An admin-composed send: immediate or scheduled, to a described audience. The job is
    /// the unit of cancellation and of duplicate protection — the audience is resolved once,
    /// when the job is claimed, and every notification it produces carries the job id in its
    /// dedup key, so a worker restart mid-send resumes instead of re-sending.
    /// </summary>
    public class NotificationJob
    {
        public int Id { get; set; }

        public NotificationJobStatus Status { get; set; } = NotificationJobStatus.Scheduled;

        public NotificationType Type { get; set; } = NotificationType.AdminAnnouncement;

        public NotificationCategory Category { get; set; } = NotificationCategory.Announcements;

        public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;

        public string Title { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public string? ActionText { get; set; }

        /// <summary>Site-relative destination, validated on save.</summary>
        public string? ActionUrl { get; set; }

        public NotificationChannel Channels { get; set; } = NotificationChannel.InApp;

        public NotificationAudienceType AudienceType { get; set; }

        /// <summary>Audience parameters as JSON — user ids, a team id, a project id, booking
        /// ids or lead ids depending on <see cref="AudienceType"/>.</summary>
        public string? AudienceJson { get; set; }

        /// <summary>Null means "send now". Always stored in UTC.</summary>
        public DateTime? ScheduledAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public int CreatedByUserId { get; set; }

        public DateTime? ProcessingStartedAt { get; set; }

        public DateTime? CompletedAt { get; set; }

        public DateTime? CancelledAt { get; set; }

        public int? CancelledByUserId { get; set; }

        public int RecipientCount { get; set; }

        public string? FailureReason { get; set; }

        /// <summary>Idempotency token supplied by the composer. Unique, so a double-clicked
        /// Send or a retried request creates one job, not two.</summary>
        public string? RequestKey { get; set; }

        /// <summary>Worker lease, same contract as <see cref="NotificationDelivery"/>.</summary>
        public DateTime? LockedUntil { get; set; }

        public string? LockedBy { get; set; }

        public byte[]? RowVersion { get; set; }
    }
}
