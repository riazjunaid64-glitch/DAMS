using DAMS.Application.Common;
using DAMS.Application.DTOs.EmployeeDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public class StaffManagementService : IStaffManagementService
    {
        private static readonly string[] StaffRoles =
            { LeadRoles.Admin, LeadRoles.Manager, LeadRoles.Employee };

        private readonly AppDbContext _context;

        public StaffManagementService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<StaffDirectoryDto>> GetDirectoryAsync(
            LeadUserContext actor,
            CancellationToken cancellationToken = default)
        {
            LeadAccess.EnsureStaff(actor);

            var query = _context.Employees
                .AsNoTracking()
                .Include(e => e.User).ThenInclude(u => u!.Role)
                .Include(e => e.Team)
                .Where(e => e.Status == EmployeeStatus.Active);

            if (actor.IsManager)
            {
                var teamIds = actor.ManagedTeamIds.ToArray();
                query = query.Where(e => e.Id == actor.EmployeeId ||
                    (e.TeamId.HasValue && teamIds.Contains(e.TeamId.Value)));
            }
            else if (actor.IsEmployee)
            {
                var teamId = actor.TeamId;
                query = query.Where(e => e.Id == actor.EmployeeId ||
                    (teamId.HasValue && e.TeamId == teamId));
            }

            return await query
                .OrderBy(e => e.FullName)
                .Select(e => new StaffDirectoryDto
                {
                    EmployeeId = e.Id,
                    UserId = e.UserId,
                    FullName = e.FullName,
                    Email = e.User != null ? e.User.Email : e.Email,
                    Role = e.User != null ? e.User.Role.Role_name : null,
                    TeamId = e.TeamId,
                    TeamName = e.Team != null ? e.Team.Name : null,
                    Status = e.Status,
                    CanOwnLeads = e.User != null &&
                        (e.User.Role.Role_name == LeadRoles.Manager ||
                         e.User.Role.Role_name == LeadRoles.Employee ||
                         e.User.Role.Role_name == LeadRoles.Admin)
                })
                .ToListAsync(cancellationToken);
        }

        public Task<List<StaffAccountDto>> GetAccountsAsync(CancellationToken cancellationToken = default) =>
            AccountQuery()
                .OrderBy(e => e.FullName)
                .Select(e => new StaffAccountDto
                {
                    EmployeeId = e.Id,
                    UserId = e.UserId,
                    FullName = e.FullName,
                    Email = e.User != null ? e.User.Email : e.Email,
                    Role = e.User != null ? e.User.Role.Role_name : null,
                    TeamId = e.TeamId,
                    TeamName = e.Team != null ? e.Team.Name : null,
                    Status = e.Status,
                    CanOwnLeads = e.User != null,
                    JobTitle = e.JobTitle,
                    Department = e.Department,
                    Phone = e.Phone,
                    JoinDate = e.JoinDate,
                    IsTeamManager = _context.Teams.Any(t => t.ManagerEmployeeId == e.Id)
                })
                .ToListAsync(cancellationToken);

        public Task<List<LinkableUserDto>> GetLinkableUsersAsync(CancellationToken cancellationToken = default) =>
            _context.Users
                .AsNoTracking()
                .Where(u => !_context.Employees.Any(e => e.UserId == u.UserId))
                .OrderBy(u => u.FullName)
                .Select(u => new LinkableUserDto
                {
                    UserId = u.UserId,
                    FullName = u.FullName,
                    Email = u.Email,
                    Role = u.Role.Role_name
                })
                .ToListAsync(cancellationToken);

        public async Task<List<CustomerLookupDto>> SearchCustomersAsync(
            LeadUserContext actor,
            string search,
            CancellationToken cancellationToken = default)
        {
            LeadAccess.EnsureCanConvert(actor);

            var term = search?.Trim().ToLowerInvariant() ?? string.Empty;
            if (term.Length < 2)
                return new List<CustomerLookupDto>();

            var query = _context.Customers
                .AsNoTracking()
                .Where(c => c.FullName.ToLower().Contains(term)
                    || c.Phone.Contains(term)
                    || (c.Email != null && c.Email.ToLower().Contains(term))
                    || (c.CNIC != null && c.CNIC.Contains(term)));

            if (!actor.IsAdmin)
            {
                var visibleLeadCustomerIds = LeadAccess.Scope(_context.Leads.AsNoTracking(), actor)
                    .Where(l => l.ConvertedCustomerId != null)
                    .Select(l => l.ConvertedCustomerId!.Value);

                var managedTeamIds = actor.ManagedTeamIds.ToArray();
                var managedUserIds = _context.Employees
                    .AsNoTracking()
                    .Where(e => e.UserId != null
                                && (e.Id == actor.EmployeeId
                                    || (e.TeamId.HasValue && managedTeamIds.Contains(e.TeamId.Value))))
                    .Select(e => e.UserId!.Value);

                var managedBookingCustomerIds = _context.Bookings
                    .AsNoTracking()
                    .Where(b => b.AssignedSalesUserId != null && managedUserIds.Contains(b.AssignedSalesUserId.Value))
                    .Select(b => b.CustomerId);

                query = query.Where(c => visibleLeadCustomerIds.Contains(c.Id)
                                         || managedBookingCustomerIds.Contains(c.Id));
            }

            return await query
                .OrderBy(c => c.FullName)
                .Take(10)
                .Select(c => new CustomerLookupDto
                {
                    Id = c.Id,
                    FullName = c.FullName,
                    Phone = c.Phone,
                    Email = c.Email ?? string.Empty,
                    CNIC = c.CNIC
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<StaffAccountDto> CreateAsync(
            CreateStaffAccountDto dto,
            CancellationToken cancellationToken = default)
        {
            var role = await ResolveRoleAsync(dto.Role, cancellationToken);
            await ValidateTeamAsync(dto.TeamId, cancellationToken);

            User user;
            if (dto.ExistingUserId.HasValue)
            {
                user = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserId == dto.ExistingUserId.Value, cancellationToken)
                    ?? throw new InvalidOperationException("The selected login account does not exist.");

                if (await _context.Employees.AnyAsync(e => e.UserId == user.UserId, cancellationToken))
                    throw new InvalidOperationException("That login account is already linked to an employee.");

                user.RoleId = role.RoleId;
            }
            else
            {
                var email = NormalizeEmail(dto.Email);
                if (string.IsNullOrWhiteSpace(dto.TemporaryPassword) || dto.TemporaryPassword.Length < 8)
                    throw new InvalidOperationException("A temporary password of at least 8 characters is required.");

                if (await _context.Users.AnyAsync(u => u.Email.ToLower() == email, cancellationToken))
                    throw new InvalidOperationException("A login with that email already exists. Choose it from existing accounts.");

                user = new User
                {
                    FullName = dto.FullName.Trim(),
                    Email = email,
                    Password = BCrypt.Net.BCrypt.HashPassword(dto.TemporaryPassword),
                    RoleId = role.RoleId
                };
                _context.Users.Add(user);
            }

            Employee employee;
            if (dto.ExistingEmployeeId.HasValue)
            {
                employee = await _context.Employees
                    .FirstOrDefaultAsync(e => e.Id == dto.ExistingEmployeeId.Value, cancellationToken)
                    ?? throw new InvalidOperationException("The selected employee does not exist.");

                if (employee.UserId.HasValue)
                    throw new InvalidOperationException("That employee already has a login account.");

                employee.User = user;
                employee.TeamId = dto.TeamId ?? employee.TeamId;
                employee.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                employee = new Employee
                {
                    User = user,
                    FullName = dto.FullName.Trim(),
                    Email = NormalizeEmail(dto.Email),
                    Phone = dto.Phone.Trim(),
                    JobTitle = dto.JobTitle.Trim(),
                    Department = dto.Department.Trim(),
                    TeamId = dto.TeamId,
                    JoinDate = (dto.JoinDate ?? DateTime.UtcNow).Date,
                    Status = EmployeeStatus.Active
                };
                _context.Employees.Add(employee);
            }

            await _context.SaveChangesAsync(cancellationToken);
            return await LoadAccountAsync(employee.Id, cancellationToken);
        }

        public async Task<StaffAccountDto> UpdateAsync(
            int employeeId,
            UpdateStaffAccountDto dto,
            CancellationToken cancellationToken = default)
        {
            var employee = await _context.Employees
                .Include(e => e.User)
                .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken)
                ?? throw new InvalidOperationException("Employee not found.");

            if (employee.User == null)
                throw new InvalidOperationException("This employee has no login account. Connect an account first.");

            var role = await ResolveRoleAsync(dto.Role, cancellationToken);

            var managesActiveTeam = await _context.Teams
                .AnyAsync(t => t.ManagerEmployeeId == employeeId && t.IsActive, cancellationToken);
            if (managesActiveTeam &&
                role.Role_name != LeadRoles.Manager &&
                role.Role_name != LeadRoles.Admin)
                throw new InvalidOperationException(
                    "Reassign this employee's active sales team before removing the Sales Manager role.");

            if (managesActiveTeam && dto.Status.HasValue && dto.Status != EmployeeStatus.Active)
                throw new InvalidOperationException(
                    "Reassign this employee's active sales team before changing their employment status.");

            employee.User.RoleId = role.RoleId;

            if (dto.TeamId.HasValue)
            {
                var newTeamId = dto.TeamId.Value == -1 ? null : dto.TeamId;
                await ValidateTeamAsync(newTeamId, cancellationToken);
                employee.TeamId = newTeamId;
            }

            if (dto.Status.HasValue)
                employee.Status = dto.Status.Value;

            if (!string.IsNullOrWhiteSpace(dto.NewTemporaryPassword))
                employee.User.Password = BCrypt.Net.BCrypt.HashPassword(dto.NewTemporaryPassword);

            // Changing security-sensitive account data invalidates every existing session.
            employee.User.RefreshToken = null;
            employee.User.RefreshTokenExpiresAt = null;
            employee.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);
            return await LoadAccountAsync(employee.Id, cancellationToken);
        }

        private IQueryable<Employee> AccountQuery() => _context.Employees
            .AsNoTracking()
            .Include(e => e.User).ThenInclude(u => u!.Role)
            .Include(e => e.Team);

        private async Task<StaffAccountDto> LoadAccountAsync(int employeeId, CancellationToken cancellationToken) =>
            await AccountQuery()
                .Where(e => e.Id == employeeId)
                .Select(e => new StaffAccountDto
                {
                    EmployeeId = e.Id,
                    UserId = e.UserId,
                    FullName = e.FullName,
                    Email = e.User != null ? e.User.Email : e.Email,
                    Role = e.User != null ? e.User.Role.Role_name : null,
                    TeamId = e.TeamId,
                    TeamName = e.Team != null ? e.Team.Name : null,
                    Status = e.Status,
                    CanOwnLeads = e.User != null,
                    JobTitle = e.JobTitle,
                    Department = e.Department,
                    Phone = e.Phone,
                    JoinDate = e.JoinDate,
                    IsTeamManager = _context.Teams.Any(t => t.ManagerEmployeeId == e.Id)
                })
                .FirstAsync(cancellationToken);

        private async Task<Role> ResolveRoleAsync(string requested, CancellationToken cancellationToken)
        {
            var canonical = StaffRoles.FirstOrDefault(r =>
                string.Equals(r, requested?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (canonical == null)
                throw new InvalidOperationException("Role must be Admin, Manager, or Employee.");

            return await _context.Roles.FirstOrDefaultAsync(
                       r => r.Role_name == canonical, cancellationToken)
                   ?? throw new InvalidOperationException($"The {canonical} role is not configured. Apply the Lead Management migration.");
        }

        private async Task ValidateTeamAsync(int? teamId, CancellationToken cancellationToken)
        {
            if (teamId.HasValue &&
                !await _context.Teams.AnyAsync(t => t.Id == teamId.Value && t.IsActive, cancellationToken))
                throw new InvalidOperationException("The selected active team does not exist.");
        }

        private static string NormalizeEmail(string email)
        {
            var value = email?.Trim().ToLowerInvariant() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("Email is required.");
            return value;
        }
    }
}
