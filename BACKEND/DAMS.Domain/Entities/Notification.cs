using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>
    /// The one permanent record of "this person was told this thing". Every DAMS module
    /// writes this shape; channels only ever read it. <see cref="DedupKey"/> is unique in
    /// the database, so a repeated business event, a background retry or a second worker can
    /// all try to raise the same notification and exactly one row survives.
    /// </summary>
    public class Notification
    {
        public int Id { get; set; }

        public NotificationCategory Category { get; set; }

        public NotificationType Type { get; set; }

        public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;

        public NotificationModule Module { get; set; } = NotificationModule.System;

        public string Title { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        /// <summary>The DAMS record this is about, if any. Never trusted for authorisation
        /// on its own — the owning module re-checks access when the link is followed.</summary>
        public NotificationEntityType EntityType { get; set; } = NotificationEntityType.None;

        public int? EntityId { get; set; }

        /// <summary>Site-relative path (always starts with "/"). Absolute URLs are rejected
        /// on write so a notification can never become an open redirect.</summary>
        public string? DeepLink { get; set; }

        /// <summary>
        /// The DAMS login this belongs to. Null for a contact-only notification: a customer
        /// with no account still has to receive their payment receipt, so the platform can
        /// address a person by email alone. Such a notification has no inbox, by definition.
        /// </summary>
        public int? RecipientUserId { get; set; }

        /// <summary>Address used when there is no login. Never set for account-backed rows —
        /// those always resolve the current address at send time.</summary>
        public string? RecipientEmail { get; set; }

        public string? RecipientName { get; set; }

        public int? CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>After this instant the notification stops being surfaced in the inbox.</summary>
        public DateTime? ExpiresAt { get; set; }

        public bool IsRead { get; set; }

        public DateTime? SeenAt { get; set; }

        public DateTime? ReadAt { get; set; }

        public bool IsArchived { get; set; }

        public DateTime? ArchivedAt { get; set; }

        /// <summary>The channels this notification was created for, after rules and the
        /// recipient's preferences were applied.</summary>
        public NotificationChannel Channels { get; set; } = NotificationChannel.InApp;

        /// <summary>Stable identity of the underlying business event for this recipient.</summary>
        public string DedupKey { get; set; } = string.Empty;

        /// <summary>Template variables captured at creation, as JSON. Kept so a delayed or
        /// retried send renders exactly what the event meant at the time.</summary>
        public string? DataJson { get; set; }

        /// <summary>Set when an admin broadcast produced this row.</summary>
        public int? NotificationJobId { get; set; }

        /// <summary>Escalation alerts are shown to supervisors distinctly from their own work.</summary>
        public bool IsEscalation { get; set; }

        public User? Recipient { get; set; }

        public NotificationJob? Job { get; set; }

        public ICollection<NotificationDelivery> Deliveries { get; set; } = new List<NotificationDelivery>();
    }
}
