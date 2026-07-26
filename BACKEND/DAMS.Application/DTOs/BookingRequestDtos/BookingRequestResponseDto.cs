using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.BookingRequestDtos
{
    public class BookingRequestResponseDto
    {
        public int Id { get; set; }

        public int UnitId { get; set; }

        public string UnitNumber { get; set; } = string.Empty;

        public string UnitType { get; set; } = string.Empty;

        public decimal UnitPrice { get; set; }

        public int ProjectId { get; set; }

        public string ProjectName { get; set; } = string.Empty;

        public string ProjectLocation { get; set; } = string.Empty;

        public int? UserId { get; set; }

        public string FullName { get; set; } = string.Empty;

        public string Phone { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string CNIC { get; set; } = string.Empty;

        public string Address { get; set; } = string.Empty;

        public string? Notes { get; set; }

        public BookingRequestStatus Status { get; set; }

        public DateTime RequestedAt { get; set; }

        public DateTime? ReviewedAt { get; set; }

        public int? ReviewedByUserId { get; set; }

        public string? ReviewedByName { get; set; }

        public string? RejectionReason { get; set; }

        /// <summary>The lead this website enquiry feeds. Null only for un-backfilled history.</summary>
        public int? LeadId { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
