using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.NotificationDtos
{
    /// <summary>One row in a user's private inbox.</summary>
    public class NotificationDto
    {
        public int Id { get; set; }

        public NotificationCategory Category { get; set; }

        public NotificationType Type { get; set; }

        public NotificationPriority Priority { get; set; }

        public NotificationModule Module { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public NotificationEntityType EntityType { get; set; }

        public int? EntityId { get; set; }

        /// <summary>Always site-relative.</summary>
        public string? DeepLink { get; set; }

        public bool IsRead { get; set; }

        public bool IsEscalation { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? ReadAt { get; set; }

        public DateTime? ExpiresAt { get; set; }
    }

    public class NotificationPageDto
    {
        public List<NotificationDto> Items { get; set; } = new();

        public int TotalCount { get; set; }

        public int UnreadCount { get; set; }

        public int Page { get; set; }

        public int PageSize { get; set; }
    }

    public class NotificationFilterDto
    {
        public NotificationCategory? Category { get; set; }

        public bool UnreadOnly { get; set; }

        public bool IncludeArchived { get; set; }

        public string? Search { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 20;
    }

    public class NotificationSummaryDto
    {
        public int UnreadCount { get; set; }

        /// <summary>Unread count per category, for the filter chips.</summary>
        public Dictionary<string, int> UnreadByCategory { get; set; } = new();

        public List<NotificationDto> Recent { get; set; } = new();
    }

    /// <summary>What the client needs to decide whether to offer the push opt-in.</summary>
    public class PushConfigDto
    {
        public bool Enabled { get; set; }

        /// <summary>VAPID public key (base64url). Public by design; the private half never leaves the server.</summary>
        public string? PublicKey { get; set; }

        public string DisplayName { get; set; } = "DAMS";

        public string? IconUrl { get; set; }

        public string? BadgeUrl { get; set; }

        /// <summary>True when this login already has at least one live subscription.</summary>
        public bool HasActiveSubscription { get; set; }

        public int DeviceCount { get; set; }
    }

    public class RegisterPushSubscriptionDto
    {
        public string Endpoint { get; set; } = string.Empty;

        public string P256dh { get; set; } = string.Empty;

        public string Auth { get; set; } = string.Empty;

        /// <summary>Free-text browser label, e.g. "Chrome on Windows". Trimmed and truncated.</summary>
        public string? DeviceLabel { get; set; }
    }

    public class UnregisterPushSubscriptionDto
    {
        public string Endpoint { get; set; } = string.Empty;
    }

    public class NotificationPreferenceDto
    {
        public NotificationCategory Category { get; set; }

        public string Label { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public bool EmailEnabled { get; set; }

        public bool PushEnabled { get; set; }

        /// <summary>Mandatory categories are shown but cannot be switched off.</summary>
        public bool IsMandatory { get; set; }

        /// <summary>False when an admin has switched the whole channel off for this category.</summary>
        public bool EmailAvailable { get; set; }

        public bool PushAvailable { get; set; }
    }

    public class UpdateNotificationPreferencesDto
    {
        public List<UpdateNotificationPreferenceItemDto> Items { get; set; } = new();
    }

    public class UpdateNotificationPreferenceItemDto
    {
        public NotificationCategory Category { get; set; }

        public bool EmailEnabled { get; set; }

        public bool PushEnabled { get; set; }
    }
}
