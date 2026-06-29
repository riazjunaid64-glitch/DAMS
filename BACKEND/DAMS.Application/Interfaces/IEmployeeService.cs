using DAMS.Application.DTOs.EmployeeDtos;
using DAMS.Domain.Enums;

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
        Task<List<AttendanceResponseDto>> GetAttendanceHistoryAsync(DateTime from, DateTime to, AttendanceStatus? status = null, int? employeeId = null);
        Task<Dictionary<string, int>> GetAttendanceSummaryAsync(int employeeId, int month, int year);

        // Tasks
        Task<TaskResponseDto> AssignTaskAsync(AssignTaskDto dto);
        Task<TaskResponseDto> UpdateTaskStatusAsync(int taskId, UpdateTaskStatusDto dto);
        Task<List<TaskResponseDto>> GetTasksByEmployeeAsync(int employeeId);
        Task<List<TaskResponseDto>> GetTasksByProjectAsync(int projectId);

        // Salary
        Task<SalaryResponseDto> GenerateSalaryAsync(int employeeId, GenerateSalaryDto dto, int? adminUserId);
        Task<SalaryResponseDto> UpdateSalaryAsync(int salaryId, UpdateSalaryDto dto);
        Task<List<SalaryResponseDto>> GetSalariesAsync(int employeeId);
        Task<List<SalaryMonthSummaryDto>> GetSalaryMonthSummariesAsync(int employeeId);
        Task<List<SalaryResponseDto>> GetSalariesByMonthAsync(int employeeId, int month, int year);
        Task<List<SalaryResponseDto>> GetSalaryHistoryAsync(DateTime from, DateTime to, int? employeeId = null);
        Task<SalaryResponseDto?> GetSalaryByIdAsync(int salaryId);
        Task<List<AttendanceBatchItemDto>> GetAttendanceBatchAsync(DateTime date);
        Task<List<SalaryBatchItemDto>> GetSalaryBatchAsync(int month, int year);
    }
}
