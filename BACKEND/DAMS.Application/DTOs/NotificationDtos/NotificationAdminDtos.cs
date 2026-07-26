using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.NotificationDtos
{
    // ── Settings ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Admin settings as the frontend sees them. Secrets are never present as values — only
    /// a masked hint and a boolean saying whether one is stored.
    /// </summary>
    public class NotificationSettingsDto
    {
        public Dictionary<string, string?> Values { get; set; } = new();

        /// <summary>Key → masked display (e.g. "••••••1a2b"). Only for secret keys.</summary>
        public Dictionary<string, string> Secrets { get; set; } = new();

        public NotificationStatusDto Status { get; set; } = new();
    }

    public class NotificationStatusDto
    {
        public bool EmailEnabled { get; set; }

        public bool EmailConfigured { get; set; }

        public string? EmailConfigurationIssue { get; set; }

        public DateTime? EmailLastTestAt { get; set; }

        public string? EmailLastFailure { get; set; }

        public bool PushEnabled { get; set; }

        public bool PushConfigured { get; set; }

        public string? PushConfigurationIssue { get; set; }

        public DateTime? PushLastTestAt { get; set; }

        public string? PushLastFailure { get; set; }

        public int ActivePushSubscriptions { get; set; }

        public int PendingDeliveries { get; set; }

        public int FailedDeliveries { get; set; }
    }

    public class UpdateNotificationSettingsDto
    {
        public Dictionary<string, string?> Values { get; set; } = new();
    }

    public class SendTestEmailDto
    {
        public string? Recipient { get; set; }
    }

    // ── Templates ───────────────────────────────────────────────────────────────

    public class NotificationTemplateDto
    {
        public int Id { get; set; }

        public NotificationType Type { get; set; }

        public NotificationChannel Channel { get; set; }

        public string Name { get; set; } = string.Empty;

        public NotificationCategory Category { get; set; }

        public string Subject { get; set; } = string.Empty;

        public string? Heading { get; set; }

        public string Body { get; set; } = string.Empty;

        public string? ActionText { get; set; }

        public string? ActionUrl { get; set; }

        public string? Footer { get; set; }

        public string? IconUrl { get; set; }

        public string? BadgeUrl { get; set; }

        public bool IsEnabled { get; set; }

        public int Version { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public string? UpdatedByName { get; set; }

        /// <summary>Variables this template may use — the frontend offers exactly these.</summary>
        public List<string> AvailableVariables { get; set; } = new();

        /// <summary>True when this notification cannot be switched off entirely.</summary>
        public bool IsMandatory { get; set; }
    }

    public class SaveNotificationTemplateDto
    {
        /// <summary>Used by draft preview; save endpoints still take the channel from route.</summary>
        public NotificationChannel? Channel { get; set; }

        public string? Name { get; set; }

        public string Subject { get; set; } = string.Empty;

        public string? Heading { get; set; }

        public string Body { get; set; } = string.Empty;

        public string? ActionText { get; set; }

        public string? ActionUrl { get; set; }

        public string? Footer { get; set; }

        public string? IconUrl { get; set; }

        public string? BadgeUrl { get; set; }

        public bool IsEnabled { get; set; } = true;
    }

    public class NotificationPreviewDto
    {
        public string Subject { get; set; } = string.Empty;

        public string Html { get; set; } = string.Empty;

        public string PlainText { get; set; } = string.Empty;

        public string? PushTitle { get; set; }

        public string? PushBody { get; set; }

        public string? ActionUrl { get; set; }
    }

    // ── Rules ───────────────────────────────────────────────────────────────────

    public class NotificationRuleDto
    {
        public NotificationType Type { get; set; }

        public string Name { get; set; } = string.Empty;

        public NotificationCategory Category { get; set; }

        public NotificationModule Module { get; set; }

        public bool IsEnabled { get; set; }

        public bool InAppEnabled { get; set; }

        public bool EmailEnabled { get; set; }

        public bool PushEnabled { get; set; }

        public NotificationPriority Priority { get; set; }

        public int DelayMinutes { get; set; }

        public int ReminderLeadDays { get; set; }

        public bool RemindOnDueDate { get; set; }

        public bool RepeatWhenOverdue { get; set; }

        public bool EscalateToSupervisors { get; set; }

        /// <summary>Essential notifications: the platform refuses to switch these off.</summary>
        public bool IsMandatory { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }

    public class SaveNotificationRuleDto
    {
        public bool IsEnabled { get; set; } = true;

        public bool InAppEnabled { get; set; } = true;

        public bool EmailEnabled { get; set; }

        public bool PushEnabled { get; set; }

        public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;

        public int DelayMinutes { get; set; }

        public int ReminderLeadDays { get; set; }

        public bool RemindOnDueDate { get; set; } = true;

        public bool RepeatWhenOverdue { get; set; }

        public bool EscalateToSupervisors { get; set; }

        /// <summary>The composer must acknowledge before weakening an essential notification.</summary>
        public bool ConfirmEssentialChange { get; set; }
    }

    // ── Manual and scheduled sends ──────────────────────────────────────────────

    public class NotificationAudienceDto
    {
        public NotificationAudienceType Type { get; set; }

        public List<int> UserIds { get; set; } = new();

        public int? TeamId { get; set; }

        public int? ProjectId { get; set; }

        public List<int> BookingIds { get; set; } = new();

        public List<int> LeadIds { get; set; } = new();
    }

    public class ComposeNotificationDto
    {
        public string Title { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public string? ActionText { get; set; }

        public string? ActionUrl { get; set; }

        public NotificationType Type { get; set; } = NotificationType.AdminAnnouncement;

        public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;

        public bool SendEmail { get; set; }

        public bool SendPush { get; set; }

        public NotificationAudienceDto Audience { get; set; } = new();

        /// <summary>Null or in the past means "send now".</summary>
        public DateTime? ScheduledAt { get; set; }

        /// <summary>Required once the audience passes the large-audience threshold.</summary>
        public bool ConfirmLargeAudience { get; set; }

        /// <summary>Client-generated idempotency token; a repeated submit reuses the job.</summary>
        public string? RequestKey { get; set; }
    }

    public class AudiencePreviewDto
    {
        public int TotalRecipients { get; set; }

        public int WithEmail { get; set; }

        public int WithPushDevices { get; set; }

        public int PushDeviceCount { get; set; }

        /// <summary>Recipients who have muted this category for a chosen channel.</summary>
        public int EmailOptedOut { get; set; }

        public int PushOptedOut { get; set; }

        public bool RequiresConfirmation { get; set; }

        public string Description { get; set; } = string.Empty;

        public List<string> SampleRecipients { get; set; } = new();
    }

    public class NotificationJobDto
    {
        public int Id { get; set; }

        public NotificationJobStatus Status { get; set; }

        public NotificationType Type { get; set; }

        public NotificationCategory Category { get; set; }

        public NotificationPriority Priority { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public NotificationChannel Channels { get; set; }

        public NotificationAudienceType AudienceType { get; set; }

        public string AudienceDescription { get; set; } = string.Empty;

        public DateTime? ScheduledAt { get; set; }

        public DateTime CreatedAt { get; set; }

        public string? CreatedByName { get; set; }

        public DateTime? CompletedAt { get; set; }

        public DateTime? CancelledAt { get; set; }

        public int RecipientCount { get; set; }

        public string? FailureReason { get; set; }

        public string? ActionUrl { get; set; }
    }

    // ── Delivery history ────────────────────────────────────────────────────────

    public class DeliveryHistoryFilterDto
    {
        public NotificationChannel? Channel { get; set; }

        public NotificationDeliveryStatus? Status { get; set; }

        public NotificationType? Type { get; set; }

        public NotificationCategory? Category { get; set; }

        public int? RecipientUserId { get; set; }

        public int? JobId { get; set; }

        public DateTime? From { get; set; }

        public DateTime? To { get; set; }

        public bool FailuresOnly { get; set; }

        public string? Search { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 25;
    }

    public class DeliveryHistoryItemDto
    {
        public int Id { get; set; }

        public int NotificationId { get; set; }

        public NotificationType Type { get; set; }

        public NotificationCategory Category { get; set; }

        public string Title { get; set; } = string.Empty;

        public NotificationChannel Channel { get; set; }

        public NotificationDeliveryStatus Status { get; set; }

        public int? RecipientUserId { get; set; }

        public string? RecipientName { get; set; }

        /// <summary>Masked address or subscription fingerprint — never a full push key.</summary>
        public string? Target { get; set; }

        public NotificationEntityType EntityType { get; set; }

        public int? EntityId { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? ScheduledFor { get; set; }

        public DateTime? ProcessingStartedAt { get; set; }

        public DateTime? SentAt { get; set; }

        public DateTime? DeliveredAt { get; set; }

        public DateTime? FailedAt { get; set; }

        public int AttemptCount { get; set; }

        public DateTime? NextAttemptAt { get; set; }

        public string? ProviderReference { get; set; }

        public string? FailureReason { get; set; }

        public bool IsPermanentFailure { get; set; }

        public bool CanRetry { get; set; }

        public int? JobId { get; set; }

        public string? CreatedByName { get; set; }
    }

    public class DeliveryHistoryPageDto
    {
        public List<DeliveryHistoryItemDto> Items { get; set; } = new();

        public int TotalCount { get; set; }

        public int Page { get; set; }

        public int PageSize { get; set; }
    }

    public class EmailSuppressionDto
    {
        public int Id { get; set; }

        public string Email { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
    }

    public class PushDeviceDto
    {
        public int Id { get; set; }

        public string? DeviceLabel { get; set; }

        /// <summary>Last few characters of the endpoint — enough to tell devices apart,
        /// useless to anyone who intercepts it.</summary>
        public string Fingerprint { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public DateTime LastSeenAt { get; set; }

        public DateTime? LastSuccessAt { get; set; }

        public bool IsActive { get; set; }
    }

    public class NotificationAuditDto
    {
        public int Id { get; set; }

        public string Area { get; set; } = string.Empty;

        public string Action { get; set; } = string.Empty;

        public string? Details { get; set; }

        public string? PerformedByName { get; set; }

        public DateTime OccurredAt { get; set; }
    }
}
