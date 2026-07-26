using DAMS.Domain.Enums;

namespace DAMS.Application.Common
{
    /// <summary>
    /// What a business module hands to the notification platform. It describes the event and
    /// who it concerns — never how it should be delivered. Channels, wording, timing and
    /// suppression are all decided inside the platform from admin rules and the recipient's
    /// preferences, which is what lets a new channel be added later without any business
    /// module changing.
    /// </summary>
    public sealed class NotificationRequest
    {
        public required NotificationType Type { get; init; }

        /// <summary>The recipient's login. Null only for a contact-only send, which then
        /// requires <see cref="RecipientEmail"/>.</summary>
        public int? RecipientUserId { get; init; }

        /// <summary>Address for a recipient who has no DAMS login (a customer created by an
        /// admin, for instance). Such a notification is email-only and has no inbox.</summary>
        public string? RecipientEmail { get; init; }

        public string? RecipientName { get; init; }

        /// <summary>
        /// Stable identity of this event for this recipient. Two attempts with the same key
        /// produce one notification, whatever caused the repeat.
        /// </summary>
        public required string DedupKey { get; init; }

        /// <summary>Overrides the catalog's default title. Leave null to render the template.</summary>
        public string? Title { get; init; }

        public string? Message { get; init; }

        public NotificationEntityType EntityType { get; init; } = NotificationEntityType.None;

        public int? EntityId { get; init; }

        /// <summary>Parent record id where a link needs both (payment→booking, follow-up→lead).</summary>
        public int? SecondaryEntityId { get; init; }

        /// <summary>Site-relative destination. Falls back to the catalog route for the entity.</summary>
        public string? DeepLink { get; init; }

        public NotificationPriority? Priority { get; init; }

        public int? CreatedByUserId { get; init; }

        public DateTime? ExpiresAt { get; init; }

        /// <summary>Hold the first send attempt until this instant (reminders, digests).</summary>
        public DateTime? AvailableAt { get; init; }

        public bool IsEscalation { get; init; }

        public int? JobId { get; init; }

        /// <summary>Template variables. Values are always encoded before they reach a template.</summary>
        public IReadOnlyDictionary<string, string?> Data { get; init; } =
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Restricts the channels this event may use, on top of the admin rule. Used by the
        /// admin composer, which chooses channels explicitly.
        /// </summary>
        public NotificationChannel? ChannelMask { get; init; }
    }
}
