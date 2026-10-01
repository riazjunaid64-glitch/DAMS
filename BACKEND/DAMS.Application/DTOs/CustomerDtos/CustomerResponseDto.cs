using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.CustomerDtos
{
    public class CustomerResponseDto
    {
        public int Id { get; set; }

        public string FullName { get; set; } = string.Empty;

        public string? FatherName { get; set; }

        public string Phone { get; set; } = string.Empty;

        public string? CNIC { get; set; }

        public string? Email { get; set; }

        public string? Address { get; set; }

        public DateTime? DateOfBirth { get; set; }

        public string? Nationality { get; set; }

        public string? Occupation { get; set; }

        public string? Whatsapp { get; set; }

        public CustomerSource Source { get; set; }

        public string? SourceNotes { get; set; }

        public CustomerStatus Status { get; set; }

        /// <summary>Why the customer is blocked; null while Active.</summary>
        public string? BlockedReason { get; set; }

        public string? BlockedByName { get; set; }

        public DateTime? BlockedAt { get; set; }

        public int? UserId { get; set; }

        public string? Notes { get; set; }

        public int BookingsCount { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        /// <summary>Required documents still Needed — the number the page header badge shows.</summary>
        public int DocumentsNeeded { get; set; }
    }
}
