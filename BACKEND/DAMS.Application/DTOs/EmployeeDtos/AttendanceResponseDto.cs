using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.EmployeeDtos
{
    public class AttendanceResponseDto
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public AttendanceStatus Status { get; set; }
        public TimeSpan? CheckInTime { get; set; }
        public TimeSpan? CheckOutTime { get; set; }
        public string? Notes { get; set; }
    }
}
