using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>
    /// The editable wording for one notification type on one channel. Absent or disabled,
    /// the platform falls back to the built-in copy in the notification catalog, so an admin
    /// cannot accidentally silence an event by deleting its template.
    /// </summary>
    public class NotificationTemplate
    {
        public int Id { get; set; }

        public NotificationType Type { get; set; }

        public NotificationChannel Channel { get; set; }

        public string Name { get; set; } = string.Empty;

        public NotificationCategory Category { get; set; }

        // Email fields (Subject doubles as the push title, Body as the push message).
        public string Subject { get; set; } = string.Empty;

        public string? Heading { get; set; }

        public string Body { get; set; } = string.Empty;

        public string? ActionText { get; set; }

        /// <summary>Site-relative destination. Validated on save; absolute URLs are rejected.</summary>
        public string? ActionUrl { get; set; }

        public string? Footer { get; set; }

        /// <summary>Push only: overrides the configured default icon/badge.</summary>
        public string? IconUrl { get; set; }

        public string? BadgeUrl { get; set; }

        public bool IsEnabled { get; set; } = true;

        public int Version { get; set; } = 1;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public int? UpdatedByUserId { get; set; }
    }
}
