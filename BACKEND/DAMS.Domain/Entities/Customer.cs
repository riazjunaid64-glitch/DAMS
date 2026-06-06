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

        public string? CNIC { get; set; }

        public string? Email { get; set; }

        public string? Address { get; set; }

        public CustomerSource Source { get; set; } = CustomerSource.Other;

        public string? SourceNotes { get; set; }

        public CustomerStatus Status { get; set; } = CustomerStatus.Active;

        // Optional link to a login account. A customer may exist without one.
        public int? UserId { get; set; }

        public string? Notes { get; set; }

        public int? CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        // Navigation
        public User? User { get; set; }

        public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
    }
}
