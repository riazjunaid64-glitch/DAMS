using DAMS.Application.DTOs.EmployeeDtos;

namespace DAMS.Application.Interfaces
{
    public interface IEmployeeService
    {
        // Employee CRUD
        Task<EmployeeResponseDto> CreateEmployeeAsync(CreateEmployeeDto dto);
        Task<EmployeeResponseDto?> GetEmployeeByIdAsync(int id);
        Task<List<EmployeeResponseDto>> GetAllEmployeesAsync(string? department = null, string? status = null);
        Task<EmployeeResponseDto> UpdateEmployeeAsync(int id, UpdateEmployeeDto dto);
        Task DeleteEmployeeAsync(int id);

        // Attendance
        Task<AttendanceResponseDto> RecordAttendanceAsync(int employeeId, RecordAttendanceDto dto);
        Task<List<AttendanceResponseDto>> GetAttendanceAsync(int employeeId, DateTime? from = null, DateTime? to = null);
        Task<Dictionary<string, int>> GetAttendanceSummaryAsync(int employeeId, int month, int year);

        // Tasks
        Task<TaskResponseDto> AssignTaskAsync(AssignTaskDto dto);
        Task<TaskResponseDto> UpdateTaskStatusAsync(int taskId, UpdateTaskStatusDto dto);
        Task<List<TaskResponseDto>> GetTasksByEmployeeAsync(int employeeId);
        Task<List<TaskResponseDto>> GetTasksByProjectAsync(int projectId);
    }
}
