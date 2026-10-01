using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    public class Customer
    {
        public int Id { get; set; }

        public string FullName { get; set; } = string.Empty;

        // Father's / husband's name (S/o, W/o) shown on official payment receipts.
        public string? FatherName { get; set; }

        public string Phone { get; set; } = string.Empty;

        // National number used to recognise the same subscriber however the phone was written
        // (0300…, +92…, 92…, 0092…). Lookups match this. The display Phone is left as entered.
        public string? NormalizedPhone { get; set; }

        public string? CNIC { get; set; }

        public string? Email { get; set; }

        public string? Address { get; set; }

        // Additional personal details captured on the official Application Form.
        public DateTime? DateOfBirth { get; set; }

        public string? Nationality { get; set; }

        public string? Occupation { get; set; }

        public string? Whatsapp { get; set; }

        public CustomerSource Source { get; set; } = CustomerSource.Other;

        public string? SourceNotes { get; set; }

        public CustomerStatus Status { get; set; } = CustomerStatus.Active;

        // Why, when and by whom the customer is blocked. All null while Active: unblocking clears
        // them here and keeps the history in CustomerStatusLog.
        public string? BlockedReason { get; set; }

        public DateTime? BlockedAt { get; set; }

        public int? BlockedByUserId { get; set; }

        // Optional link to a login account. A customer may exist without one.
        public int? UserId { get; set; }

        public string? Notes { get; set; }

        public int? CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        // Navigation
        public User? User { get; set; }

        public ICollection<Booking> Bookings { get; set; } = new List<Booking>();

        public ICollection<CustomerDocumentRequirement> DocumentRequirements { get; set; } = new List<CustomerDocumentRequirement>();
    }
}
