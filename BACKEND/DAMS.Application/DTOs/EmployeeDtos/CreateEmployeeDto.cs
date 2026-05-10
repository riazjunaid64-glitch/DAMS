using System.ComponentModel.DataAnnotations;
using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.EmployeeDtos
{
    public class CreateEmployeeDto
    {
        [Required(ErrorMessage = "Full name is required.")]
        [MaxLength(200)]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Job title is required.")]
        [MaxLength(100)]
        public string JobTitle { get; set; } = string.Empty;

        [Required(ErrorMessage = "Department is required.")]
        [MaxLength(100)]
        public string Department { get; set; } = string.Empty;

        [Required(ErrorMessage = "Phone is required.")]
        [MaxLength(50, ErrorMessage = "Phone cannot exceed 50 characters.")]
        public string Phone { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? Email { get; set; }

        [MaxLength(500)]
        public string? Address { get; set; }

        public decimal Salary { get; set; }

        [Required]
        public DateTime JoinDate { get; set; }

        public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;
    }
}
