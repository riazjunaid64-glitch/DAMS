using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>
    /// A recorded exchange with the customer. Future channel integrations (WhatsApp
    /// Business, telephony, email) write into this same table using
    /// <see cref="ExternalMessageId"/> for idempotency.
    /// </summary>
    public class LeadCommunication
    {
        public int Id { get; set; }

        public int LeadId { get; set; }

        public LeadCommunicationChannel Channel { get; set; }

        public LeadCommunicationDirection Direction { get; set; }

        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

        public int? EmployeeId { get; set; }

        public int? RecordedByUserId { get; set; }

        public string Summary { get; set; } = string.Empty;

        public string? CustomerResponse { get; set; }

        public string? NextAction { get; set; }

        public DateTime? NextActionAt { get; set; }

        /// <summary>False when the customer could not be reached (a contact attempt).</summary>
        public bool Connected { get; set; } = true;

        // Set only when the record came from an external channel integration.
        public string? ExternalProvider { get; set; }

        public string? ExternalMessageId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Lead Lead { get; set; } = null!;

        public Employee? Employee { get; set; }

        public ICollection<LeadDocument> Attachments { get; set; } = new List<LeadDocument>();
    }
}
