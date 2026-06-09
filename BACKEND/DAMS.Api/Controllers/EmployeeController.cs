using DAMS.Application.DTOs.EmployeeDtos;
using DAMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DAMS.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class EmployeeController : ControllerBase
    {
        private readonly IEmployeeService _employeeService;

        public EmployeeController(IEmployeeService employeeService)
        {
            _employeeService = employeeService;
        }

        // ─── Employee CRUD ───────────────────────────────────────────────────────

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateEmployeeDto dto)
        {
            try
            {
                var result = await _employeeService.CreateEmployeeAsync(dto);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(
            [FromQuery] string? department = null,
            [FromQuery] string? status = null)
        {
            var result = await _employeeService.GetAllEmployeesAsync(department, status);
            return Ok(result);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var result = await _employeeService.GetEmployeeByIdAsync(id);
            if (result == null) return NotFound();
            return Ok(result);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateEmployeeDto dto)
        {
            try
            {
                var result = await _employeeService.UpdateEmployeeAsync(id, dto);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                await _employeeService.DeleteEmployeeAsync(id);
                return Ok(new { message = "Employee deleted successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // ─── Attendance ──────────────────────────────────────────────────────────

        [HttpPost("{employeeId:int}/attendance")]
        public async Task<IActionResult> RecordAttendance(int employeeId, [FromBody] RecordAttendanceDto dto)
        {
            try
            {
                var result = await _employeeService.RecordAttendanceAsync(employeeId, dto);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("{employeeId:int}/attendance")]
        public async Task<IActionResult> GetAttendance(
            int employeeId,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null)
        {
            var result = await _employeeService.GetAttendanceAsync(employeeId, from, to);
            return Ok(result);
        }

        [HttpGet("{employeeId:int}/attendance/summary")]
        public async Task<IActionResult> GetAttendanceSummary(
            int employeeId,
            [FromQuery] int month,
            [FromQuery] int year)
        {
            if (month < 1 || month > 12)
                return BadRequest(new { message = "Month must be between 1 and 12." });

            var result = await _employeeService.GetAttendanceSummaryAsync(employeeId, month, year);
            return Ok(result);
        }

        [HttpGet("attendance/batch")]
        public async Task<IActionResult> GetAttendanceBatch([FromQuery] DateTime? date = null)
        {
            var result = await _employeeService.GetAttendanceBatchAsync(date ?? DateTime.UtcNow);
            return Ok(result);
        }

        // ─── Tasks ───────────────────────────────────────────────────────────────

        [HttpPost("tasks")]
        public async Task<IActionResult> AssignTask([FromBody] AssignTaskDto dto)
        {
            try
            {
                var result = await _employeeService.AssignTaskAsync(dto);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("tasks/{taskId:int}/status")]
        public async Task<IActionResult> UpdateTaskStatus(int taskId, [FromBody] UpdateTaskStatusDto dto)
        {
            try
            {
                var result = await _employeeService.UpdateTaskStatusAsync(taskId, dto);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("{employeeId:int}/tasks")]
        public async Task<IActionResult> GetTasksByEmployee(int employeeId)
        {
            var result = await _employeeService.GetTasksByEmployeeAsync(employeeId);
            return Ok(result);
        }

        [HttpGet("tasks/project/{projectId:int}")]
        public async Task<IActionResult> GetTasksByProject(int projectId)
        {
            var result = await _employeeService.GetTasksByProjectAsync(projectId);
            return Ok(result);
        }

        // ─── Salary ──────────────────────────────────────────────────────────────

        [HttpGet("salary/batch")]
        public async Task<IActionResult> GetSalaryBatch(
            [FromQuery] int month,
            [FromQuery] int year)
        {
            if (month < 1 || month > 12)
                return BadRequest(new { message = "Month must be between 1 and 12." });

            var result = await _employeeService.GetSalaryBatchAsync(month, year);
            return Ok(result);
        }

        [HttpGet("salary/{salaryId:int}")]
        public async Task<IActionResult> GetSalaryById(int salaryId)
        {
            var result = await _employeeService.GetSalaryByIdAsync(salaryId);
            if (result == null) return NotFound();
            return Ok(result);
        }

        [HttpPost("{employeeId:int}/salary")]
        public async Task<IActionResult> GenerateSalary(int employeeId, [FromBody] GenerateSalaryDto dto)
        {
            try
            {
                var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                int? adminUserId = int.TryParse(userIdClaim, out var uid) ? uid : null;
                var result = await _employeeService.GenerateSalaryAsync(employeeId, dto, adminUserId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("salary/{salaryId:int}")]
        public async Task<IActionResult> UpdateSalary(int salaryId, [FromBody] UpdateSalaryDto dto)
        {
            try
            {
                var result = await _employeeService.UpdateSalaryAsync(salaryId, dto);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("{employeeId:int}/salary")]
        public async Task<IActionResult> GetSalaries(int employeeId)
        {
            var result = await _employeeService.GetSalariesAsync(employeeId);
            return Ok(result);
        }

        [HttpGet("{employeeId:int}/salary/summary")]
        public async Task<IActionResult> GetSalaryMonthSummaries(int employeeId)
        {
            var result = await _employeeService.GetSalaryMonthSummariesAsync(employeeId);
            return Ok(result);
        }

        [HttpGet("{employeeId:int}/salary/month")]
        public async Task<IActionResult> GetSalariesByMonth(
            int employeeId,
            [FromQuery] int month,
            [FromQuery] int year)
        {
            if (month < 1 || month > 12)
                return BadRequest(new { message = "Month must be between 1 and 12." });

            var result = await _employeeService.GetSalariesByMonthAsync(employeeId, month, year);
            return Ok(result);
        }
    }
}
