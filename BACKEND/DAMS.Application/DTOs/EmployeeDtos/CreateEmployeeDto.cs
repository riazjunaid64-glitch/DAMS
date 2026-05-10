using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.EmployeeDtos
{
    public class CreateEmployeeDto
    {
        public string FullName { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Address { get; set; }
        public decimal Salary { get; set; }
        public DateTime JoinDate { get; set; }
        public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;
    }
}
