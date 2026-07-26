using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.EmployeeDtos
{
    public class UpdateEmployeeDto
    {
        public string? FullName { get; set; }
        public string? JobTitle { get; set; }
        public string? Department { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public decimal? Salary { get; set; }
        public EmployeeStatus? Status { get; set; }

        /// <summary>Login account link. Send -1 to clear it.</summary>
        public int? UserId { get; set; }

        /// <summary>Team membership. Send -1 to clear it.</summary>
        public int? TeamId { get; set; }
    }
}
