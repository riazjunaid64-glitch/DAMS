using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.EmployeeDtos
{
    public class AttendanceBatchItemDto
    {
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public int? AttendanceId { get; set; }
        public AttendanceStatus? Status { get; set; }
        public string? Notes { get; set; }
    }
}
