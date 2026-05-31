using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.CustomerDtos
{
    public class CustomerResponseDto
    {
        public int Id { get; set; }

        public string FullName { get; set; } = string.Empty;

        public string Phone { get; set; } = string.Empty;

        public string? CNIC { get; set; }

        public string? Email { get; set; }

        public string? Address { get; set; }

        public CustomerSource Source { get; set; }

        public string? SourceNotes { get; set; }

        public CustomerStatus Status { get; set; }

        public int? UserId { get; set; }

        public string? Notes { get; set; }

        public int BookingsCount { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }
}
