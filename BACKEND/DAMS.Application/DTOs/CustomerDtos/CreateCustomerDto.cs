using System.ComponentModel.DataAnnotations;
using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.CustomerDtos
{
    public class CreateCustomerDto
    {
        [Required]
        [StringLength(200, MinimumLength = 2)]
        public string FullName { get; set; } = string.Empty;

        [StringLength(200)]
        public string? FatherName { get; set; }

        [Required]
        [StringLength(50, MinimumLength = 7)]
        public string Phone { get; set; } = string.Empty;

        [StringLength(50)]
        public string? CNIC { get; set; }

        [EmailAddress]
        [StringLength(200)]
        public string? Email { get; set; }

        [StringLength(500)]
        public string? Address { get; set; }

        [Required]
        public CustomerSource Source { get; set; } = CustomerSource.WalkIn;

        [StringLength(500)]
        public string? SourceNotes { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }
    }
}
