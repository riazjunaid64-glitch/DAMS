using System.Security.Claims;
using DAMS.Application.Common;
using DAMS.Application.DTOs.NotificationDtos;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;

namespace DAMS.Application.Interfaces
{
    /// <summary>
    /// The only way a DAMS module creates a notification. It resolves rules, preferences and
    /// templates, writes the permanent record and opens one delivery row per enabled channel.
    /// It never talks to a provider, so calling it can never make a business operation slow
    /// or fragile.
    /// </summary>
    public interface INotificationDispatcher
    {
        /// <summary>
        /// Stages a notification in the current DbContext without saving, so it commits with
        /// whatever transaction the caller is already in. Returns false when an identical
        /// notification already exists.
        /// </summary>
        Task<bool> QueueAsync(NotificationRequest request, CancellationToken cancellationToken = default);

        Task<int> QueueManyAsync(IEnumerable<NotificationRequest> requests, CancellationToken cancellationToken = default);

        /// <summary>
        /// Stages and saves in one step, for callers whose business transaction has already
        /// committed. A unique-key clash from a concurrent caller is treated as "already
        /// created" rather than an error.
        /// </summary>
        Task<bool> DispatchAsync(NotificationRequest request, CancellationToken cancellationToken = default);
    }

    /// <summary>A user's own inbox. Every query is scoped to the caller.</summary>
    public interface INotificationInboxService
    {
        Task<NotificationPageDto> GetAsync(NotificationUserContext ctx, NotificationFilterDto filter, CancellationToken cancellationToken = default);

        Task<NotificationSummaryDto> GetSummaryAsync(NotificationUserContext ctx, int take, CancellationToken cancellationToken = default);

        Task<int> GetUnreadCountAsync(NotificationUserContext ctx, CancellationToken cancellationToken = default);

        Task<NotificationDto> MarkReadAsync(int notificationId, NotificationUserContext ctx, CancellationToken cancellationToken = default);

        Task<int> MarkAllReadAsync(NotificationUserContext ctx, NotificationCategory? category, CancellationToken cancellationToken = default);

        Task ArchiveAsync(int notificationId, NotificationUserContext ctx, CancellationToken cancellationToken = default);

        /// <summary>Resolves where a notification should open, re-checking access at click time.</summary>
        Task<NotificationOpenResult> OpenAsync(int notificationId, NotificationUserContext ctx, CancellationToken cancellationToken = default);
    }

    public sealed class NotificationOpenResult
    {
        public required bool Allowed { get; init; }

        public string? DeepLink { get; init; }

        public required string Message { get; init; }

        public NotificationDto? Notification { get; init; }
    }

    /// <summary>Per-user channel preferences, enforced server-side.</summary>
    public interface INotificationPreferenceService
    {
        Task<NotificationCapabilitiesDto> GetCapabilitiesAsync(NotificationUserContext ctx, CancellationToken cancellationToken = default);

        Task<List<NotificationPreferenceDto>> GetAsync(NotificationUserContext ctx, CancellationToken cancellationToken = default);

        Task<List<NotificationPreferenceDto>> UpdateAsync(NotificationUserContext ctx, UpdateNotificationPreferencesDto dto, CancellationToken cancellationToken = default);
    }

    /// <summary>Browser push subscriptions: one per browser, many per user.</summary>
    public interface IPushSubscriptionService
    {
        Task<PushConfigDto> GetConfigAsync(NotificationUserContext ctx, CancellationToken cancellationToken = default);

        Task RegisterAsync(NotificationUserContext ctx, RegisterPushSubscriptionDto dto, CancellationToken cancellationToken = default);

        Task UnregisterAsync(NotificationUserContext ctx, string endpoint, CancellationToken cancellationToken = default);

        /// <summary>Drops every subscription for this login. Called on logout so a shared
        /// browser stops receiving the previous user's messages.</summary>
        Task<int> UnregisterAllAsync(int userId, CancellationToken cancellationToken = default);

        Task<List<PushDeviceDto>> GetMyDevicesAsync(NotificationUserContext ctx, CancellationToken cancellationToken = default);

        Task<int> SendTestAsync(NotificationUserContext ctx, CancellationToken cancellationToken = default);
    }

    /// <summary>Admin settings, templates and rules.</summary>
    public interface INotificationConfigurationService
    {
        Task<NotificationSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);

        Task<NotificationSettingsDto> UpdateSettingsAsync(UpdateNotificationSettingsDto dto, NotificationUserContext ctx, CancellationToken cancellationToken = default);

        Task<string> SendTestEmailAsync(SendTestEmailDto dto, NotificationUserContext ctx, CancellationToken cancellationToken = default);

        /// <summary>Creates a VAPID key pair and stores both halves. Returns only the public
        /// key; the private half never leaves the server.</summary>
        Task<string> GeneratePushKeysAsync(NotificationUserContext ctx, CancellationToken cancellationToken = default);

        Task<List<NotificationTemplateDto>> GetTemplatesAsync(CancellationToken cancellationToken = default);

        Task<NotificationTemplateDto> GetTemplateAsync(NotificationType type, NotificationChannel channel, CancellationToken cancellationToken = default);

        Task<NotificationTemplateDto> SaveTemplateAsync(NotificationType type, NotificationChannel channel, SaveNotificationTemplateDto dto, NotificationUserContext ctx, CancellationToken cancellationToken = default);

        Task<NotificationPreviewDto> PreviewTemplateAsync(NotificationType type, SaveNotificationTemplateDto? draft, CancellationToken cancellationToken = default);

        Task<List<NotificationRuleDto>> GetRulesAsync(CancellationToken cancellationToken = default);

        Task<NotificationRuleDto> SaveRuleAsync(NotificationType type, SaveNotificationRuleDto dto, NotificationUserContext ctx, CancellationToken cancellationToken = default);

        Task<List<NotificationAuditDto>> GetAuditAsync(int take, CancellationToken cancellationToken = default);
    }

    /// <summary>Manual and scheduled admin sends, plus delivery history.</summary>
    public interface INotificationAdminService
    {
        Task<AudiencePreviewDto> PreviewAudienceAsync(ComposeNotificationDto dto, CancellationToken cancellationToken = default);

        Task<NotificationJobDto> ComposeAsync(ComposeNotificationDto dto, NotificationUserContext ctx, CancellationToken cancellationToken = default);

        Task<List<NotificationJobDto>> GetJobsAsync(int take, CancellationToken cancellationToken = default);

        Task<NotificationJobDto> CancelJobAsync(int jobId, NotificationUserContext ctx, CancellationToken cancellationToken = default);

        Task<DeliveryHistoryPageDto> GetDeliveryHistoryAsync(DeliveryHistoryFilterDto filter, CancellationToken cancellationToken = default);

        Task<DeliveryHistoryItemDto> RetryDeliveryAsync(int deliveryId, NotificationUserContext ctx, CancellationToken cancellationToken = default);

        Task<List<EmailSuppressionDto>> GetSuppressionsAsync(CancellationToken cancellationToken = default);

        Task RemoveSuppressionAsync(int id, NotificationUserContext ctx, CancellationToken cancellationToken = default);
    }

    /// <summary>Turns an audience description into a concrete set of recipients.</summary>
    public interface INotificationRecipientResolver
    {
        Task<List<int>> ResolveAsync(NotificationAudienceType type, NotificationAudienceSelection selection, CancellationToken cancellationToken = default);

        Task<List<NotificationRecipientTarget>> ResolveTargetsAsync(
            NotificationAudienceType type,
            NotificationAudienceSelection selection,
            CancellationToken cancellationToken = default);

        string Describe(NotificationAudienceType type, NotificationAudienceSelection selection);
    }

    public sealed record NotificationRecipientTarget(int? UserId, string? Email, string? Name);

    public sealed class NotificationAudienceSelection
    {
        public IReadOnlyList<int> UserIds { get; init; } = Array.Empty<int>();

        public int? TeamId { get; init; }

        public int? ProjectId { get; init; }

        public IReadOnlyList<int> BookingIds { get; init; } = Array.Empty<int>();

        public IReadOnlyList<int> LeadIds { get; init; } = Array.Empty<int>();
    }

    /// <summary>
    /// One delivery channel. Adding WhatsApp, SMS or mobile push later means adding one more
    /// implementation and one more enum value — no business module and no stored notification
    /// changes shape.
    /// </summary>
    public interface INotificationChannelSender
    {
        NotificationChannel Channel { get; }

        Task<ChannelSendResult> SendAsync(NotificationDelivery delivery, Notification notification, CancellationToken cancellationToken = default);
    }

    public sealed class ChannelSendResult
    {
        public required NotificationDeliveryStatus Status { get; init; }

        public string? Target { get; init; }

        public string? ProviderReference { get; init; }

        public string? FailureReason { get; init; }

        /// <summary>True when retrying could never succeed (no address, gone subscription).</summary>
        public bool IsPermanent { get; init; }

        public static ChannelSendResult Sent(string? target, string? reference = null) =>
            new() { Status = NotificationDeliveryStatus.Sent, Target = target, ProviderReference = reference };

        public static ChannelSendResult Unavailable(string reason) =>
            new() { Status = NotificationDeliveryStatus.Unavailable, FailureReason = reason, IsPermanent = true };

        public static ChannelSendResult Skipped(string reason) =>
            new() { Status = NotificationDeliveryStatus.Skipped, FailureReason = reason, IsPermanent = true };

        public static ChannelSendResult PermanentFailure(string reason, string? target = null) =>
            new() { Status = NotificationDeliveryStatus.Failed, FailureReason = reason, Target = target, IsPermanent = true };

        public static ChannelSendResult TransientFailure(string reason, string? target = null) =>
            new() { Status = NotificationDeliveryStatus.Failed, FailureReason = reason, Target = target, IsPermanent = false };

        public static ChannelSendResult Bounced(string reason, string? target = null) =>
            new() { Status = NotificationDeliveryStatus.Bounced, FailureReason = reason, Target = target, IsPermanent = true };
    }

    /// <summary>The transactional email transport. Swappable without touching any workflow.</summary>
    public interface IEmailSender
    {
        string ProviderName { get; }

        Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
    }

    public sealed class EmailMessage
    {
        public required string To { get; init; }

        public string? ToName { get; init; }

        public required string Subject { get; init; }

        public required string HtmlBody { get; init; }

        public required string TextBody { get; init; }

        public IReadOnlyList<EmailAttachment> Attachments { get; init; } = Array.Empty<EmailAttachment>();
    }

    public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

    public sealed class EmailSendResult
    {
        public required bool Success { get; init; }

        public string? ProviderReference { get; init; }

        public string? Error { get; init; }

        /// <summary>The address is bad — suppress it rather than retry.</summary>
        public bool IsHardBounce { get; init; }

        /// <summary>Configuration or address problems that retrying will not fix.</summary>
        public bool IsPermanent { get; init; }
    }

    /// <summary>Web push transport (RFC 8291 payload encryption, RFC 8292 VAPID auth).</summary>
    public interface IWebPushSender
    {
        Task<WebPushResult> SendAsync(WebPushTarget target, string payloadJson, WebPushCredentials credentials, CancellationToken cancellationToken = default);
    }

    public sealed record WebPushTarget(string Endpoint, string P256dh, string Auth);

    public sealed record WebPushCredentials(string PublicKey, string PrivateKey, string Subject);

    public sealed class WebPushResult
    {
        public required bool Success { get; init; }

        public int StatusCode { get; init; }

        public string? Error { get; init; }

        /// <summary>404/410: the browser threw the subscription away. Deactivate it.</summary>
        public bool SubscriptionGone { get; init; }
    }

    /// <summary>
    /// Pushes fresh notifications to inbox streams held open by online users. In-process by
    /// design: the permanent inbox is the source of truth, so a missed live event costs a
    /// refresh, never a notification.
    /// </summary>
    public interface INotificationRealtimeBroker
    {
        IAsyncEnumerable<string> SubscribeAsync(int userId, CancellationToken cancellationToken);

        Task PublishAsync(int userId, string eventName, object payload);

        int ConnectionCount { get; }
    }

    /// <summary>Business hooks. Modules call these; they never touch channels directly.</summary>
    public interface INotificationEventService
    {
        /// <summary>Raises the receipt notification for one payment. Idempotent per payment
        /// and recipient, safe to call after the payment transaction has committed.</summary>
        Task NotifyPaymentRecordedAsync(int paymentId, CancellationToken cancellationToken = default);

        Task NotifyBookingStatusAsync(int bookingId, NotificationType type, string? reason, int? actorUserId, CancellationToken cancellationToken = default);

        Task NotifyBookingRequestReceivedAsync(int bookingRequestId, CancellationToken cancellationToken = default);

        /// <summary>A rejected enquiry never becomes a booking, so it is notified against the
        /// request itself rather than through the booking path.</summary>
        Task NotifyBookingRequestRejectedAsync(int bookingRequestId, string? reason, int? actorUserId, CancellationToken cancellationToken = default);

        Task NotifyEmployeeTaskAssignedAsync(int taskId, int? actorUserId, CancellationToken cancellationToken = default);

        Task NotifyProjectUpdatedAsync(int projectId, string summary, int? actorUserId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Finds payments that were committed but never notified — the window between a
        /// payment's commit and its notification — and closes it. Idempotent.
        /// </summary>
        Task<int> ReconcilePaymentReceiptsAsync(int maxRows, CancellationToken cancellationToken = default);

        /// <summary>Recovers post-commit booking, enquiry and employee-task events that were
        /// missed because the process stopped between the business commit and dispatch.</summary>
        Task<int> ReconcileBusinessEventsAsync(int maxRows, CancellationToken cancellationToken = default);

        /// <summary>Raises due/overdue installment reminders according to the admin rule.</summary>
        Task<int> RunInstallmentRemindersAsync(int maxRows, CancellationToken cancellationToken = default);
    }

    /// <summary>Runs one sweep of the delivery queue. Safe to run on many instances at once.</summary>
    public interface INotificationDeliveryProcessor
    {
        Task<int> ProcessDueDeliveriesAsync(int batchSize, CancellationToken cancellationToken = default);

        Task<int> ProcessScheduledJobsAsync(int batchSize, CancellationToken cancellationToken = default);

        /// <summary>Drops inbox rows past their expiry or retention window.</summary>
        Task<int> PruneAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>Resolves the caller once per request, exactly like the lead workspace does.</summary>
    public interface INotificationUserContextResolver
    {
        Task<NotificationUserContext> ResolveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
    }
}
