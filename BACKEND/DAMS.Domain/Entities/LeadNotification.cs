using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>
    /// An in-app alert for one user about one lead. <see cref="DedupKey"/> is unique, so a
    /// repeating background scan can raise the same alert as often as it likes without ever
    /// producing a second copy.
    /// </summary>
    public class LeadNotification
    {
        public int Id { get; set; }

        public int LeadId { get; set; }

        public int RecipientUserId { get; set; }

        public LeadNotificationType Type { get; set; }

        public string Title { get; set; } = string.Empty;

        public string? Body { get; set; }

        // Type + lead + recipient + a time bucket where the alert may legitimately repeat.
        public string DedupKey { get; set; } = string.Empty;

        public bool IsRead { get; set; }

        public DateTime? ReadAt { get; set; }

        public bool IsEscalation { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Lead Lead { get; set; } = null!;

        public User Recipient { get; set; } = null!;
    }
}
