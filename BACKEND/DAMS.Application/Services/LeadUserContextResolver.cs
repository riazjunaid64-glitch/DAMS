using System.Security.Claims;
using DAMS.Application.Common;
using DAMS.Application.Interfaces;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public class LeadUserContextResolver : ILeadUserContextResolver
    {
        private readonly AppDbContext _context;

        public LeadUserContextResolver(AppDbContext context)
        {
            _context = context;
        }

        public async Task<LeadUserContext> ResolveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
        {
            var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                throw new LeadAuthorizationException("Your session is not valid. Sign in again.");

            var role = principal.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
            var displayName = principal.FindFirst(ClaimTypes.Name)?.Value;

            var employee = await _context.Employees
                .AsNoTracking()
                .Where(e => e.UserId == userId)
                .Select(e => new { e.Id, e.TeamId, e.FullName })
                .FirstOrDefaultAsync(cancellationToken);

            var managedTeamIds = new List<int>();
            if (employee != null)
            {
                if (employee.TeamId.HasValue)
                    managedTeamIds.Add(employee.TeamId.Value);

                var ownedTeams = await _context.Teams
                    .AsNoTracking()
                    .Where(t => t.ManagerEmployeeId == employee.Id)
                    .Select(t => t.Id)
                    .ToListAsync(cancellationToken);

                managedTeamIds.AddRange(ownedTeams);
            }

            return new LeadUserContext
            {
                UserId = userId,
                Role = role,
                DisplayName = employee?.FullName ?? displayName,
                EmployeeId = employee?.Id,
                TeamId = employee?.TeamId,
                ManagedTeamIds = managedTeamIds.Distinct().ToList()
            };
        }
    }
}
