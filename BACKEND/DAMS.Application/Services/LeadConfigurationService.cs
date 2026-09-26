using DAMS.Application.Common;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    /// <summary>
    /// Sources, closure reasons and teams are business data an admin maintains, not
    /// constants in code. System rows can be renamed or deactivated but never deleted, so
    /// historical leads always resolve the reason they were closed with.
    /// </summary>
    public class LeadConfigurationService : ILeadConfigurationService
    {
        private readonly AppDbContext _context;

        public LeadConfigurationService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<LeadSourceDto>> GetSourcesAsync(bool includeInactive, CancellationToken cancellationToken = default)
        {
            var query = _context.LeadSources.AsNoTracking().AsQueryable();
            if (!includeInactive)
                query = query.Where(s => s.IsActive);

            return await query
                .OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name)
                .Select(s => new LeadSourceDto
                {
                    Id = s.Id,
                    Code = s.Code,
                    Name = s.Name,
                    IsActive = s.IsActive,
                    IsSystem = s.IsSystem,
                    DisplayOrder = s.DisplayOrder,
                    CustomerSource = s.CustomerSource
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<LeadSourceDto> CreateSourceAsync(CreateLeadSourceDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            LeadAccess.EnsureCanConfigure(ctx);

            var code = dto.Code.Trim().ToLowerInvariant();
            if (await _context.LeadSources.AnyAsync(s => s.Code == code, cancellationToken))
                throw new InvalidOperationException($"A lead source with code '{code}' already exists.");

            var source = new LeadSource
            {
                Code = code,
                Name = dto.Name.Trim(),
                DisplayOrder = dto.DisplayOrder,
                CustomerSource = dto.CustomerSource,
                IsActive = true,
                IsSystem = false,
                CreatedAt = DateTime.UtcNow
            };

            _context.LeadSources.Add(source);
            await _context.SaveChangesAsync(cancellationToken);

            return Map(source);
        }

        public async Task<LeadSourceDto> UpdateSourceAsync(int id, UpdateLeadSourceDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            LeadAccess.EnsureCanConfigure(ctx);

            var source = await _context.LeadSources.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("Lead source not found.");

            // Meta intake resolves these sources by code and keeps using them even when inactive,
            // so switching one off while Meta is connected would not stop a single lead — only
            // look as if it had. Refused with the step that actually does stop them.
            if (source.IsActive && !dto.IsActive
                && IntegrationSourceCodes.All.Contains(source.Code)
                && await _context.ExternalIntegrationConnections.AnyAsync(
                    c => c.Provider == IntegrationProviders.Meta
                         && c.Status != ExternalIntegrationConnectionStatus.Disconnected,
                    cancellationToken))
            {
                throw new InvalidOperationException(
                    $"Lead source '{source.Name}' receives Meta leads and cannot be deactivated while Meta is connected. " +
                    "Disconnect Meta under CRM settings → Integrations first.");
            }

            source.Name = dto.Name.Trim();
            source.IsActive = dto.IsActive;
            source.DisplayOrder = dto.DisplayOrder;
            source.CustomerSource = dto.CustomerSource;
            source.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            return Map(source);
        }

        public async Task<List<LeadClosureReasonDto>> GetClosureReasonsAsync(
            bool includeInactive, LeadClosureReasonKind? kind, CancellationToken cancellationToken = default)
        {
            var query = _context.LeadClosureReasons.AsNoTracking().AsQueryable();

            if (!includeInactive)
                query = query.Where(r => r.IsActive);

            if (kind.HasValue && kind.Value != LeadClosureReasonKind.Both)
                query = query.Where(r => r.Kind == kind.Value || r.Kind == LeadClosureReasonKind.Both);

            return await query
                .OrderBy(r => r.DisplayOrder).ThenBy(r => r.Name)
                .Select(r => new LeadClosureReasonDto
                {
                    Id = r.Id,
                    Code = r.Code,
                    Name = r.Name,
                    Kind = r.Kind,
                    IsActive = r.IsActive,
                    IsSystem = r.IsSystem,
                    DisplayOrder = r.DisplayOrder
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<LeadClosureReasonDto> CreateClosureReasonAsync(
            CreateLeadClosureReasonDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            LeadAccess.EnsureCanConfigure(ctx);

            var code = dto.Code.Trim().ToLowerInvariant();
            if (await _context.LeadClosureReasons.AnyAsync(r => r.Code == code, cancellationToken))
                throw new InvalidOperationException($"A closure reason with code '{code}' already exists.");

            var reason = new LeadClosureReason
            {
                Code = code,
                Name = dto.Name.Trim(),
                Kind = dto.Kind,
                DisplayOrder = dto.DisplayOrder,
                IsActive = true,
                IsSystem = false,
                CreatedAt = DateTime.UtcNow
            };

            _context.LeadClosureReasons.Add(reason);
            await _context.SaveChangesAsync(cancellationToken);

            return Map(reason);
        }

        public async Task<LeadClosureReasonDto> UpdateClosureReasonAsync(
            int id, UpdateLeadClosureReasonDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            LeadAccess.EnsureCanConfigure(ctx);

            var reason = await _context.LeadClosureReasons.FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("Closure reason not found.");

            reason.Name = dto.Name.Trim();
            reason.Kind = dto.Kind;
            reason.IsActive = dto.IsActive;
            reason.DisplayOrder = dto.DisplayOrder;
            reason.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            return Map(reason);
        }

        public async Task<List<TeamDto>> GetTeamsAsync(CancellationToken cancellationToken = default) =>
            await _context.Teams
                .AsNoTracking()
                .OrderBy(t => t.Name)
                .Select(t => new TeamDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    ManagerEmployeeId = t.ManagerEmployeeId,
                    ManagerName = t.ManagerEmployee != null ? t.ManagerEmployee.FullName : null,
                    IsActive = t.IsActive,
                    MemberCount = t.Members.Count
                })
                .ToListAsync(cancellationToken);

        public async Task<TeamDto> CreateTeamAsync(SaveTeamDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            LeadAccess.EnsureCanConfigure(ctx);

            var name = dto.Name.Trim();
            if (await _context.Teams.AnyAsync(t => t.Name == name, cancellationToken))
                throw new InvalidOperationException($"A team called '{name}' already exists.");

            await EnsureManagerExistsAsync(dto.ManagerEmployeeId, cancellationToken);

            var team = new Team
            {
                Name = name,
                ManagerEmployeeId = dto.ManagerEmployeeId,
                IsActive = dto.IsActive,
                CreatedAt = DateTime.UtcNow
            };

            _context.Teams.Add(team);
            await _context.SaveChangesAsync(cancellationToken);

            return await LoadTeamAsync(team.Id, cancellationToken);
        }

        public async Task<TeamDto> UpdateTeamAsync(int id, SaveTeamDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            LeadAccess.EnsureCanConfigure(ctx);

            var team = await _context.Teams.FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("Team not found.");

            var name = dto.Name.Trim();
            if (await _context.Teams.AnyAsync(t => t.Name == name && t.Id != id, cancellationToken))
                throw new InvalidOperationException($"A team called '{name}' already exists.");

            await EnsureManagerExistsAsync(dto.ManagerEmployeeId, cancellationToken);

            team.Name = name;
            team.ManagerEmployeeId = dto.ManagerEmployeeId;
            team.IsActive = dto.IsActive;
            team.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            return await LoadTeamAsync(team.Id, cancellationToken);
        }

        private async Task EnsureManagerExistsAsync(int? managerEmployeeId, CancellationToken cancellationToken)
        {
            if (managerEmployeeId == null)
                return;

            var manager = await _context.Employees
                .AsNoTracking()
                .Where(e => e.Id == managerEmployeeId.Value)
                .Select(e => new
                {
                    e.Status,
                    HasLogin = e.User != null,
                    LoginActive = e.User != null && e.User.AccountStatus == UserAccountStatus.Active,
                    Role = e.User != null ? e.User.Role.Role_name : null
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (manager == null)
                throw new InvalidOperationException("The chosen manager is not an employee.");

            if (manager.Status != EmployeeStatus.Active)
                throw new InvalidOperationException("The chosen manager must be an active employee.");

            if (!manager.HasLogin ||
                (manager.Role != LeadRoles.Manager && manager.Role != LeadRoles.Admin))
                throw new InvalidOperationException(
                    "The chosen employee must have an Admin or Sales Manager login role.");

            // This rule already meant "has a login that can do manager work". An invited login
            // cannot be signed into at all, so making one a team manager leaves the team with
            // nobody able to act on it.
            if (!manager.LoginActive)
                throw new InvalidOperationException(
                    "The chosen employee has not activated their DAMS login yet, so they cannot manage a team.");
        }

        private async Task<TeamDto> LoadTeamAsync(int id, CancellationToken cancellationToken) =>
            (await GetTeamsAsync(cancellationToken)).First(t => t.Id == id);

        private static LeadSourceDto Map(LeadSource s) => new()
        {
            Id = s.Id,
            Code = s.Code,
            Name = s.Name,
            IsActive = s.IsActive,
            IsSystem = s.IsSystem,
            DisplayOrder = s.DisplayOrder,
            CustomerSource = s.CustomerSource
        };

        private static LeadClosureReasonDto Map(LeadClosureReason r) => new()
        {
            Id = r.Id,
            Code = r.Code,
            Name = r.Name,
            Kind = r.Kind,
            IsActive = r.IsActive,
            IsSystem = r.IsSystem,
            DisplayOrder = r.DisplayOrder
        };
    }
}
