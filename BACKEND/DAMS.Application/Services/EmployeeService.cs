using DAMS.Application.DTOs.EmployeeDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public class EmployeeService : IEmployeeService
    {
        private readonly AppDbContext _context;

        public EmployeeService(AppDbContext context)
        {
            _context = context;
        }

        // ─── Employee CRUD ───────────────────────────────────────────────────────

        public async Task<EmployeeResponseDto> CreateEmployeeAsync(CreateEmployeeDto dto)
        {
            if (dto.Salary < 0)
                throw new Exception("Salary cannot be negative.");

            var phone = dto.Phone.Trim();
            if (phone.Length > 50)
                throw new Exception("Phone cannot exceed 50 characters.");

            if (dto.JoinDate.Year < 1900)
                throw new Exception("Join date is not valid.");

            var employee = new Employee
            {
                FullName   = dto.FullName.Trim(),
                JobTitle   = dto.JobTitle.Trim(),
                Department = dto.Department.Trim(),
                Phone      = phone,
                Email      = dto.Email?.Trim(),
                Address    = dto.Address?.Trim(),
                Salary     = dto.Salary,
                JoinDate   = dto.JoinDate,
                Status     = dto.Status
            };

            _context.Employees.Add(employee);
            await _context.SaveChangesAsync();
            return MapEmployee(employee);
        }

        public async Task<EmployeeResponseDto?> GetEmployeeByIdAsync(int id)
        {
            var e = await _context.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            return e == null ? null : MapEmployee(e);
        }

        public async Task<List<EmployeeResponseDto>> GetAllEmployeesAsync(string? department = null, string? status = null)
        {
            var query = _context.Employees.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(department))
                query = query.Where(e => e.Department.ToLower() == department.ToLower());

            if (!string.IsNullOrWhiteSpace(status) &&
                Enum.TryParse<EmployeeStatus>(status, ignoreCase: true, out var parsedStatus))
                query = query.Where(e => e.Status == parsedStatus);

            var list = await query.OrderBy(e => e.FullName).ToListAsync();
            return list.Select(MapEmployee).ToList();
        }

        public async Task<EmployeeResponseDto> UpdateEmployeeAsync(int id, UpdateEmployeeDto dto)
        {
            var employee = await _context.Employees.FindAsync(id)
                ?? throw new Exception("Employee not found.");

            if (dto.FullName   != null) employee.FullName   = dto.FullName.Trim();
            if (dto.JobTitle   != null) employee.JobTitle   = dto.JobTitle.Trim();
            if (dto.Department != null) employee.Department = dto.Department.Trim();
            if (dto.Phone      != null) employee.Phone      = dto.Phone.Trim();
            if (dto.Email      != null) employee.Email      = dto.Email.Trim();
            if (dto.Address    != null) employee.Address    = dto.Address.Trim();
            if (dto.Salary     != null) employee.Salary     = dto.Salary.Value;
            if (dto.Status     != null) employee.Status     = dto.Status.Value;

            employee.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return MapEmployee(employee);
        }

        public async Task DeleteEmployeeAsync(int id)
        {
            var employee = await _context.Employees.FindAsync(id)
                ?? throw new Exception("Employee not found.");

            _context.Employees.Remove(employee);
            await _context.SaveChangesAsync();
        }

        // ─── Attendance ──────────────────────────────────────────────────────────

        public async Task<AttendanceResponseDto> RecordAttendanceAsync(int employeeId, RecordAttendanceDto dto)
        {
            var employeeExists = await _context.Employees.AnyAsync(e => e.Id == employeeId);
            if (!employeeExists) throw new Exception("Employee not found.");

            var dateOnly = dto.Date.Date;

            var existing = await _context.EmployeeAttendances
                .FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.Date == dateOnly);

            if (existing != null)
            {
                existing.Status       = dto.Status;
                existing.CheckInTime  = dto.CheckInTime;
                existing.CheckOutTime = dto.CheckOutTime;
                existing.Notes        = dto.Notes?.Trim();
                await _context.SaveChangesAsync();
                return await MapAttendanceAsync(existing);
            }

            var attendance = new EmployeeAttendance
            {
                EmployeeId    = employeeId,
                Date          = dateOnly,
                Status        = dto.Status,
                CheckInTime   = dto.CheckInTime,
                CheckOutTime  = dto.CheckOutTime,
                Notes         = dto.Notes?.Trim()
            };

            _context.EmployeeAttendances.Add(attendance);
            await _context.SaveChangesAsync();
            return await MapAttendanceAsync(attendance);
        }

        public async Task<List<AttendanceResponseDto>> GetAttendanceAsync(int employeeId, DateTime? from = null, DateTime? to = null)
        {
            var query = _context.EmployeeAttendances
                .AsNoTracking()
                .Include(a => a.Employee)
                .Where(a => a.EmployeeId == employeeId);

            if (from.HasValue) query = query.Where(a => a.Date >= from.Value.Date);
            if (to.HasValue)   query = query.Where(a => a.Date <= to.Value.Date);

            var list = await query.OrderByDescending(a => a.Date).ToListAsync();
            return list.Select(a => new AttendanceResponseDto
            {
                Id           = a.Id,
                EmployeeId   = a.EmployeeId,
                EmployeeName = a.Employee.FullName,
                Date         = a.Date,
                Status       = a.Status,
                CheckInTime  = a.CheckInTime,
                CheckOutTime = a.CheckOutTime,
                Notes        = a.Notes
            }).ToList();
        }

        public async Task<Dictionary<string, int>> GetAttendanceSummaryAsync(int employeeId, int month, int year)
        {
            var records = await _context.EmployeeAttendances
                .AsNoTracking()
                .Where(a => a.EmployeeId == employeeId
                         && a.Date.Month == month
                         && a.Date.Year  == year)
                .ToListAsync();

            return new Dictionary<string, int>
            {
                ["Present"] = records.Count(a => a.Status == AttendanceStatus.Present),
                ["Absent"]  = records.Count(a => a.Status == AttendanceStatus.Absent),
                ["Late"]    = records.Count(a => a.Status == AttendanceStatus.Late),
                ["HalfDay"] = records.Count(a => a.Status == AttendanceStatus.HalfDay),
                ["Leave"]   = records.Count(a => a.Status == AttendanceStatus.Leave),
                ["Total"]   = records.Count
            };
        }

        // ─── Tasks ───────────────────────────────────────────────────────────────

        public async Task<TaskResponseDto> AssignTaskAsync(AssignTaskDto dto)
        {
            var employeeExists = await _context.Employees.AnyAsync(e => e.Id == dto.EmployeeId);
            if (!employeeExists) throw new Exception("Employee not found.");

            if (dto.ProjectId.HasValue)
            {
                var projectExists = await _context.Projects.AnyAsync(p => p.Id == dto.ProjectId.Value);
                if (!projectExists) throw new Exception("Project not found.");
            }

            var task = new EmployeeTask
            {
                EmployeeId  = dto.EmployeeId,
                ProjectId   = dto.ProjectId,
                Title       = dto.Title.Trim(),
                Description = dto.Description?.Trim(),
                Priority    = dto.Priority,
                Status      = EmployeeTaskStatus.Pending,
                DueDate     = dto.DueDate
            };

            _context.EmployeeTasks.Add(task);
            await _context.SaveChangesAsync();
            return await MapTaskAsync(task.Id);
        }

        public async Task<TaskResponseDto> UpdateTaskStatusAsync(int taskId, UpdateTaskStatusDto dto)
        {
            var task = await _context.EmployeeTasks.FindAsync(taskId)
                ?? throw new Exception("Task not found.");

            task.Status    = dto.Status;
            task.UpdatedAt = DateTime.UtcNow;

            if (dto.Status == EmployeeTaskStatus.Completed)
                task.CompletedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return await MapTaskAsync(task.Id);
        }

        public async Task<List<TaskResponseDto>> GetTasksByEmployeeAsync(int employeeId)
        {
            return await _context.EmployeeTasks
                .AsNoTracking()
                .Where(t => t.EmployeeId == employeeId)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new TaskResponseDto
                {
                    Id           = t.Id,
                    EmployeeId   = t.EmployeeId,
                    EmployeeName = t.Employee.FullName,
                    ProjectId    = t.ProjectId,
                    ProjectName  = t.Project != null ? t.Project.ProjectName : null,
                    Title        = t.Title,
                    Description  = t.Description,
                    Priority     = t.Priority,
                    Status       = t.Status,
                    DueDate      = t.DueDate,
                    CompletedAt  = t.CompletedAt,
                    CreatedAt    = t.CreatedAt
                })
                .ToListAsync();
        }

        public async Task<List<TaskResponseDto>> GetTasksByProjectAsync(int projectId)
        {
            return await _context.EmployeeTasks
                .AsNoTracking()
                .Where(t => t.ProjectId == projectId)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new TaskResponseDto
                {
                    Id           = t.Id,
                    EmployeeId   = t.EmployeeId,
                    EmployeeName = t.Employee.FullName,
                    ProjectId    = t.ProjectId,
                    ProjectName  = t.Project != null ? t.Project.ProjectName : null,
                    Title        = t.Title,
                    Description  = t.Description,
                    Priority     = t.Priority,
                    Status       = t.Status,
                    DueDate      = t.DueDate,
                    CompletedAt  = t.CompletedAt,
                    CreatedAt    = t.CreatedAt
                })
                .ToListAsync();
        }

        // ─── Salary ──────────────────────────────────────────────────────────────

        public async Task<SalaryResponseDto> GenerateSalaryAsync(int employeeId, GenerateSalaryDto dto, int? adminUserId)
        {
            var employee = await _context.Employees.FindAsync(employeeId)
                ?? throw new Exception("Employee not found.");

            if (dto.Amount <= 0)
                throw new Exception("Salary amount must be greater than zero.");

            var payDate = dto.PayDate.Date;

            var alreadyPaid = await _context.EmployeeSalaries
                .AnyAsync(s => s.EmployeeId == employeeId && s.PayMonth == payDate.Month && s.PayYear == payDate.Year);
            if (alreadyPaid)
                throw new Exception($"Salary for {payDate:MMMM yyyy} has already been recorded for this employee.");

            var projectInfo = await ResolveEmployeeProjectAsync(employeeId);

            var expense = new Expense
            {
                ProjectId = projectInfo.ProjectId,
                Amount = dto.Amount,
                Category = "Salary",
                Description = $"Salary — {employee.FullName} ({payDate:MMMM yyyy})",
                Vendor = employee.FullName,
                Date = payDate,
                CreatedByUserId = adminUserId,
                CreatedAt = DateTime.UtcNow
            };
            _context.Expenses.Add(expense);
            await _context.SaveChangesAsync();

            var salary = new EmployeeSalary
            {
                EmployeeId = employeeId,
                Amount = dto.Amount,
                PayDate = payDate,
                PayMonth = payDate.Month,
                PayYear = payDate.Year,
                ProjectId = projectInfo.ProjectId,
                ProjectName = projectInfo.ProjectName,
                ExpenseId = expense.Id,
                Notes = dto.Notes?.Trim(),
                CreatedByUserId = adminUserId,
                CreatedAt = DateTime.UtcNow
            };

            _context.EmployeeSalaries.Add(salary);
            await _context.SaveChangesAsync();

            return MapSalary(salary, employee);
        }

        public async Task<SalaryResponseDto> UpdateSalaryAsync(int salaryId, UpdateSalaryDto dto)
        {
            var salary = await _context.EmployeeSalaries
                .Include(s => s.Employee)
                .FirstOrDefaultAsync(s => s.Id == salaryId)
                ?? throw new Exception("Salary record not found.");

            if (dto.Amount.HasValue)
            {
                if (dto.Amount.Value <= 0)
                    throw new Exception("Salary amount must be greater than zero.");
                salary.Amount = dto.Amount.Value;
            }

            if (dto.PayDate.HasValue)
            {
                var payDate = dto.PayDate.Value.Date;
                salary.PayDate = payDate;
                salary.PayMonth = payDate.Month;
                salary.PayYear = payDate.Year;
            }

            if (dto.Notes != null)
                salary.Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();

            if (salary.ExpenseId.HasValue)
            {
                var expense = await _context.Expenses.FindAsync(salary.ExpenseId.Value);
                if (expense != null)
                {
                    expense.Amount = salary.Amount;
                    expense.Date = salary.PayDate;
                    expense.Description = $"Salary — {salary.Employee.FullName} ({salary.PayDate:MMMM yyyy})";
                }
            }

            await _context.SaveChangesAsync();
            return MapSalary(salary, salary.Employee);
        }

        public async Task<List<SalaryResponseDto>> GetSalariesAsync(int employeeId)
        {
            var list = await _context.EmployeeSalaries
                .AsNoTracking()
                .Include(s => s.Employee)
                .Where(s => s.EmployeeId == employeeId)
                .OrderByDescending(s => s.PayDate)
                .ToListAsync();

            return list.Select(s => MapSalary(s, s.Employee)).ToList();
        }

        public async Task<List<SalaryMonthSummaryDto>> GetSalaryMonthSummariesAsync(int employeeId)
        {
            var records = await _context.EmployeeSalaries
                .AsNoTracking()
                .Where(s => s.EmployeeId == employeeId)
                .ToListAsync();

            return records
                .GroupBy(s => new { s.PayYear, s.PayMonth })
                .Select(g => new SalaryMonthSummaryDto
                {
                    Year = g.Key.PayYear,
                    Month = g.Key.PayMonth,
                    MonthLabel = new DateTime(g.Key.PayYear, g.Key.PayMonth, 1).ToString("MMMM yyyy"),
                    Count = g.Count(),
                    TotalAmount = g.Sum(s => s.Amount)
                })
                .OrderByDescending(s => s.Year)
                .ThenByDescending(s => s.Month)
                .ToList();
        }

        public async Task<List<SalaryResponseDto>> GetSalariesByMonthAsync(int employeeId, int month, int year)
        {
            var list = await _context.EmployeeSalaries
                .AsNoTracking()
                .Include(s => s.Employee)
                .Where(s => s.EmployeeId == employeeId && s.PayMonth == month && s.PayYear == year)
                .OrderByDescending(s => s.PayDate)
                .ToListAsync();

            return list.Select(s => MapSalary(s, s.Employee)).ToList();
        }

        public async Task<SalaryResponseDto?> GetSalaryByIdAsync(int salaryId)
        {
            var salary = await _context.EmployeeSalaries
                .AsNoTracking()
                .Include(s => s.Employee)
                .FirstOrDefaultAsync(s => s.Id == salaryId);

            return salary == null ? null : MapSalary(salary, salary.Employee);
        }

        public async Task<List<AttendanceBatchItemDto>> GetAttendanceBatchAsync(DateTime date)
        {
            var dateOnly = date.Date;
            var employees = await _context.Employees
                .AsNoTracking()
                .Where(e => e.Status == EmployeeStatus.Active)
                .OrderBy(e => e.FullName)
                .ToListAsync();

            var attendances = await _context.EmployeeAttendances
                .AsNoTracking()
                .Where(a => a.Date == dateOnly)
                .ToListAsync();

            var map = attendances.ToDictionary(a => a.EmployeeId);

            return employees.Select(e =>
            {
                map.TryGetValue(e.Id, out var att);
                return new AttendanceBatchItemDto
                {
                    EmployeeId = e.Id,
                    EmployeeName = e.FullName,
                    Department = e.Department,
                    JobTitle = e.JobTitle,
                    AttendanceId = att?.Id,
                    Status = att?.Status,
                    Notes = att?.Notes
                };
            }).ToList();
        }

        public async Task<List<SalaryBatchItemDto>> GetSalaryBatchAsync(int month, int year)
        {
            var employees = await _context.Employees
                .AsNoTracking()
                .Where(e => e.Status != EmployeeStatus.Terminated)
                .OrderBy(e => e.FullName)
                .ToListAsync();

            var salaries = await _context.EmployeeSalaries
                .AsNoTracking()
                .Where(s => s.PayMonth == month && s.PayYear == year)
                .ToListAsync();

            var paidMap = salaries
                .GroupBy(s => s.EmployeeId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.PayDate).First());

            // Batch-load the most recent project task for every unpaid employee in one query
            var unpaidIds = employees
                .Where(e => !paidMap.ContainsKey(e.Id))
                .Select(e => e.Id)
                .ToList();

            var projectByEmployee = new Dictionary<int, string?>();
            if (unpaidIds.Count > 0)
            {
                var tasks = await _context.EmployeeTasks
                    .AsNoTracking()
                    .Include(t => t.Project)
                    .Where(t => unpaidIds.Contains(t.EmployeeId) && t.ProjectId != null)
                    .OrderByDescending(t => t.CreatedAt)
                    .ToListAsync();

                foreach (var t in tasks)
                {
                    if (!projectByEmployee.ContainsKey(t.EmployeeId))
                        projectByEmployee[t.EmployeeId] = t.Project?.ProjectName;
                }
            }

            var result = new List<SalaryBatchItemDto>();
            foreach (var e in employees)
            {
                paidMap.TryGetValue(e.Id, out var paid);
                string? projectName = paid?.ProjectName;
                if (paid == null)
                    projectByEmployee.TryGetValue(e.Id, out projectName);

                result.Add(new SalaryBatchItemDto
                {
                    EmployeeId = e.Id,
                    EmployeeName = e.FullName,
                    Department = e.Department,
                    JobTitle = e.JobTitle,
                    BaseSalary = e.Salary,
                    IsPaid = paid != null,
                    SalaryRecordId = paid?.Id,
                    PaidAmount = paid?.Amount,
                    PayDate = paid?.PayDate,
                    ProjectName = projectName
                });
            }

            return result;
        }

        private async Task<(int? ProjectId, string? ProjectName)> ResolveEmployeeProjectAsync(int employeeId)
        {
            var task = await _context.EmployeeTasks
                .AsNoTracking()
                .Include(t => t.Project)
                .Where(t => t.EmployeeId == employeeId && t.ProjectId != null)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();

            if (task?.Project == null)
                return (null, null);

            return (task.ProjectId, task.Project.ProjectName);
        }

        private static SalaryResponseDto MapSalary(EmployeeSalary s, Employee e) => new()
        {
            Id = s.Id,
            EmployeeId = s.EmployeeId,
            EmployeeName = e.FullName,
            JobTitle = e.JobTitle,
            Department = e.Department,
            Amount = s.Amount,
            PayDate = s.PayDate,
            PayMonth = s.PayMonth,
            PayYear = s.PayYear,
            ProjectId = s.ProjectId,
            ProjectName = s.ProjectName,
            ExpenseId = s.ExpenseId,
            Notes = s.Notes,
            CreatedAt = s.CreatedAt
        };

        // ─── Mapping helpers ─────────────────────────────────────────────────────

        private static EmployeeResponseDto MapEmployee(Employee e) => new()
        {
            Id         = e.Id,
            FullName   = e.FullName,
            JobTitle   = e.JobTitle,
            Department = e.Department,
            Phone      = e.Phone,
            Email      = e.Email,
            Address    = e.Address,
            Salary     = e.Salary,
            JoinDate   = e.JoinDate,
            Status     = e.Status,
            CreatedAt  = e.CreatedAt,
            UpdatedAt  = e.UpdatedAt
        };

        private async Task<AttendanceResponseDto> MapAttendanceAsync(EmployeeAttendance a)
        {
            var name = await _context.Employees
                .AsNoTracking()
                .Where(e => e.Id == a.EmployeeId)
                .Select(e => e.FullName)
                .FirstOrDefaultAsync() ?? string.Empty;

            return new AttendanceResponseDto
            {
                Id           = a.Id,
                EmployeeId   = a.EmployeeId,
                EmployeeName = name,
                Date         = a.Date,
                Status       = a.Status,
                CheckInTime  = a.CheckInTime,
                CheckOutTime = a.CheckOutTime,
                Notes        = a.Notes
            };
        }

        private async Task<TaskResponseDto> MapTaskAsync(int taskId)
        {
            var task = await _context.EmployeeTasks
                .AsNoTracking()
                .Where(t => t.Id == taskId)
                .Select(t => new TaskResponseDto
                {
                    Id           = t.Id,
                    EmployeeId   = t.EmployeeId,
                    EmployeeName = t.Employee.FullName,
                    ProjectId    = t.ProjectId,
                    ProjectName  = t.Project != null ? t.Project.ProjectName : null,
                    Title        = t.Title,
                    Description  = t.Description,
                    Priority     = t.Priority,
                    Status       = t.Status,
                    DueDate      = t.DueDate,
                    CompletedAt  = t.CompletedAt,
                    CreatedAt    = t.CreatedAt
                })
                .FirstOrDefaultAsync();

            return task ?? throw new Exception("Task not found.");
        }
    }
}
