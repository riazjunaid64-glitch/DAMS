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
        private readonly IStaffInvitationService _invitations;

        public StaffManagementService(AppDbContext context, IStaffInvitationService invitations)
        {
            _context = context;
            _invitations = invitations;
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
                    IsTeamManager = _context.Teams.Any(t => t.ManagerEmployeeId == e.Id),
                    Access = e.User == null
                        ? StaffAccountAccess.None
                        : e.User.AccountStatus == UserAccountStatus.Invited
                            ? StaffAccountAccess.Invited
                            : e.User.AccountStatus == UserAccountStatus.Disabled
                                ? StaffAccountAccess.Disabled
                                : StaffAccountAccess.Active,
                    InvitationExpiresAt = _context.StaffInvitations
                        .Where(i => i.UserId == e.UserId && i.AcceptedAt == null && i.RevokedAt == null)
                        .OrderByDescending(i => i.CreatedAt)
                        .Select(i => (DateTime?)i.ExpiresAt)
                        .FirstOrDefault()
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

        public async Task<StaffAccountProvisionResult> CreateAsync(
            LeadUserContext actor,
            CreateStaffAccountDto dto,
            CancellationToken cancellationToken = default)
        {
            EnsureCanManageStaff(actor);

            var role = await ResolveRoleAsync(dto.Role, cancellationToken);
            await ValidateTeamAsync(dto.TeamId, cancellationToken);

            User user;
            bool needsInvitation;
            if (dto.ExistingUserId.HasValue)
            {
                user = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserId == dto.ExistingUserId.Value, cancellationToken)
                    ?? throw new InvalidOperationException("The selected login account does not exist.");

                if (await _context.Employees.AnyAsync(e => e.UserId == user.UserId, cancellationToken))
                    throw new InvalidOperationException("That login account is already linked to an employee.");

                // Re-enabling a switched-off login is a decision of its own, not a side effect
                // of granting staff access, so this refuses rather than quietly waking it up.
                if (user.AccountStatus == UserAccountStatus.Disabled)
                    throw new InvalidOperationException(
                        "That login is disabled. Re-enable the account before giving it staff access.");

                user.RoleId = role.RoleId;

                // An active login already has a password only its owner knows — linking it to
                // an employee is not a reason to send an activation link. One still waiting on
                // its first password gets a fresh invitation.
                needsInvitation = user.AccountStatus == UserAccountStatus.Invited;
            }
            else
            {
                var email = NormalizeEmail(dto.Email);
                if (await _context.Users.AnyAsync(u => u.Email.ToLower() == email, cancellationToken))
                    throw new InvalidOperationException("A login with that email already exists. Choose it from existing accounts.");

                // No password is chosen here — not by the Admin and not by DAMS. The account
                // exists but cannot be signed into until the invited person sets their own.
                user = new User
                {
                    FullName = dto.FullName.Trim(),
                    Email = email,
                    Password = null,
                    RoleId = role.RoleId,
                    AccountStatus = UserAccountStatus.Invited
                };
                _context.Users.Add(user);
                needsInvitation = true;
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

            // The account and its employee link are committed first and on their own. Only
            // then is an invitation minted, and only then is SMTP attempted — so no database
            // transaction is ever held open across a mail server round trip, and a mail
            // failure cannot undo the account that was already created.
            await _context.SaveChangesAsync(cancellationToken);

            var invitation = needsInvitation
                ? await _invitations.IssueAsync(user.UserId, actor.UserId, cancellationToken)
                : null;

            var account = await LoadAccountAsync(employee.Id, cancellationToken);
            return invitation == null
                ? StaffAccountProvisionResult.NoInvitationNeeded(account)
                : StaffAccountProvisionResult.From(account, invitation);
        }

        public async Task<StaffInvitationResult> ResendInvitationAsync(
            LeadUserContext actor,
            int employeeId,
            CancellationToken cancellationToken = default)
        {
            EnsureCanManageStaff(actor);

            // Read-only: a resend replaces the outstanding token and changes nothing else
            // about the employee or their account.
            var employee = await _context.Employees
                .AsNoTracking()
                .Include(e => e.User)
                .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken)
                ?? throw new InvalidOperationException("Employee not found.");

            if (employee.User == null)
                throw new InvalidOperationException("This employee has no login account. Connect an account first.");

            if (employee.User.AccountStatus != UserAccountStatus.Invited)
                throw new InvalidOperationException(
                    employee.User.AccountStatus == UserAccountStatus.Active
                        ? "That account is already active and does not need an activation link."
                        : "That account is disabled. Re-enable it before sending an activation link.");

            return await _invitations.ResendAsync(employee.User.UserId, actor.UserId, cancellationToken);
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
                    IsTeamManager = _context.Teams.Any(t => t.ManagerEmployeeId == e.Id),
                    Access = e.User == null
                        ? StaffAccountAccess.None
                        : e.User.AccountStatus == UserAccountStatus.Invited
                            ? StaffAccountAccess.Invited
                            : e.User.AccountStatus == UserAccountStatus.Disabled
                                ? StaffAccountAccess.Disabled
                                : StaffAccountAccess.Active,
                    InvitationExpiresAt = _context.StaffInvitations
                        .Where(i => i.UserId == e.UserId && i.AcceptedAt == null && i.RevokedAt == null)
                        .OrderByDescending(i => i.CreatedAt)
                        .Select(i => (DateTime?)i.ExpiresAt)
                        .FirstOrDefault()
                })
                .FirstAsync(cancellationToken);

        /// <summary>
        /// The controller already gates these routes on the Admin role; this repeats the check
        /// at the service boundary because the same context now supplies the recorded inviter.
        /// </summary>
        private static void EnsureCanManageStaff(LeadUserContext actor)
        {
            if (!actor.IsAdmin)
                throw new LeadAuthorizationException("Only an admin can manage staff accounts.");
        }

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
