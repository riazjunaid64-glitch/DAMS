using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    public class BookingRequest
    {
        public int Id { get; set; }

        public int UnitId { get; set; }

        public int? UserId { get; set; }

        public string FullName { get; set; } = string.Empty;

        public string Phone { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string CNIC { get; set; } = string.Empty;

        public string Address { get; set; } = string.Empty;

        public string? Notes { get; set; }

        public BookingRequestStatus Status { get; set; } = BookingRequestStatus.Pending;

        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ReviewedAt { get; set; }

        public int? ReviewedByUserId { get; set; }

        public string? RejectionReason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public Unit Unit { get; set; } = null!;

        public User? User { get; set; }

        public User? ReviewedBy { get; set; }
    }
}
