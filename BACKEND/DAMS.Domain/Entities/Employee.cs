using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    public class Employee
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

        public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public ICollection<EmployeeAttendance> Attendances { get; set; } = new List<EmployeeAttendance>();

        public ICollection<EmployeeTask> Tasks { get; set; } = new List<EmployeeTask>();

        public ICollection<EmployeeSalary> Salaries { get; set; } = new List<EmployeeSalary>();
    }
}
