using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.EmployeeDtos
{
    public class EmployeeResponseDto
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Address { get; set; }
        public decimal Salary { get; set; }
        public DateTime JoinDate { get; set; }
        public EmployeeStatus Status { get; set; }
        public int? UserId { get; set; }
        public int? TeamId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
