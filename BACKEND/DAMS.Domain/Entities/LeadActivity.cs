using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>
    /// One append-only entry in a lead's history. Rows are only ever inserted — nothing in
    /// the application updates or deletes them, so the audit trail survives conversion and
    /// closure.
    /// </summary>
    public class LeadActivity
    {
        public int Id { get; set; }

        public int LeadId { get; set; }

        public LeadActivityType Type { get; set; }

        public string Summary { get; set; } = string.Empty;

        public string? Notes { get; set; }

        // Set when the event happened over a customer-facing channel.
        public LeadCommunicationChannel? Channel { get; set; }

        public string? PreviousValue { get; set; }

        public string? NewValue { get; set; }

        // Related records, so the timeline can link straight to what it describes.
        public int? CommunicationId { get; set; }

        public int? FollowUpId { get; set; }

        public int? SiteVisitId { get; set; }

        public int? DocumentId { get; set; }

        public int? CommentId { get; set; }

        public int? BookingId { get; set; }

        public int? CustomerId { get; set; }

        // Null when the system (a scheduled scan) produced the entry rather than a person.
        public int? PerformedByUserId { get; set; }

        public string? PerformedByName { get; set; }

        public bool IsSystemGenerated { get; set; }

        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

        public Lead Lead { get; set; } = null!;
    }
}
