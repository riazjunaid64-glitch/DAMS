using DAMS.Application.Common;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public class LeadNotificationService : ILeadNotificationService
    {
        private readonly AppDbContext _context;

        public LeadNotificationService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<bool> QueueAsync(
            int leadId,
            int recipientUserId,
            LeadNotificationType type,
            string title,
            string? body,
            string dedupKey,
            bool isEscalation = false,
            CancellationToken cancellationToken = default)
        {
            if (recipientUserId <= 0)
                return false;

            var key = Normalize(dedupKey);

            // Two guards: rows already tracked but not yet saved (several alerts raised in
            // one operation) and rows already committed by an earlier scan.
            var pending = _context.ChangeTracker.Entries<LeadNotification>()
                .Any(e => e.State == EntityState.Added && e.Entity.DedupKey == key);
            if (pending)
                return false;

            if (await _context.LeadNotifications.AnyAsync(n => n.DedupKey == key, cancellationToken))
                return false;

            _context.LeadNotifications.Add(new LeadNotification
            {
                LeadId = leadId,
                RecipientUserId = recipientUserId,
                Type = type,
                Title = LeadContactNormalizer.Limit(title, 200),
                Body = LeadContactNormalizer.LimitOrNull(body, 1000),
                DedupKey = key,
                IsEscalation = isEscalation,
                CreatedAt = DateTime.UtcNow
            });

            return true;
        }

        public async Task<int> QueueForSupervisorsAsync(
            Lead lead,
            LeadNotificationType type,
            string title,
            string? body,
            string dedupKeySuffix,
            bool isEscalation = false,
            CancellationToken cancellationToken = default)
        {
            var recipients = await GetSupervisorUserIdsAsync(lead, cancellationToken);
            var created = 0;

            foreach (var userId in recipients)
            {
                if (await QueueAsync(lead.Id, userId, type, title, body,
                        $"{type}:{lead.Id}:{userId}:{dedupKeySuffix}", isEscalation, cancellationToken))
                    created++;
            }

            return created;
        }

        // A single scan raises alerts on hundreds of leads that share the same handful of
        // supervisors; resolve each set once per request rather than per lead.
        private List<int>? _adminUserIds;
        private readonly Dictionary<int, List<int>> _teamSupervisors = new();
        private readonly Dictionary<int, int?> _employeeTeams = new();

        /// <summary>
        /// Everyone who should be told about a lead's problems: every admin, plus the
        /// manager of the owning team when there is one.
        /// </summary>
        private async Task<List<int>> GetSupervisorUserIdsAsync(Lead lead, CancellationToken cancellationToken)
        {
            _adminUserIds ??= await _context.Users
                .AsNoTracking()
                .Where(u => u.Role.Role_name == LeadRoles.Admin)
                .Select(u => u.UserId)
                .ToListAsync(cancellationToken);

            var recipients = new List<int>(_adminUserIds);

            var teamId = lead.AssignedTeamId;
            if (teamId == null && lead.AssignedEmployeeId != null)
                teamId = await GetEmployeeTeamAsync(lead.AssignedEmployeeId.Value, cancellationToken);

            if (teamId != null)
                recipients.AddRange(await GetTeamSupervisorsAsync(teamId.Value, cancellationToken));

            return recipients.Distinct().ToList();
        }

        private async Task<int?> GetEmployeeTeamAsync(int employeeId, CancellationToken cancellationToken)
        {
            if (_employeeTeams.TryGetValue(employeeId, out var cached))
                return cached;

            var teamId = await _context.Employees
                .AsNoTracking()
                .Where(e => e.Id == employeeId)
                .Select(e => e.TeamId)
                .FirstOrDefaultAsync(cancellationToken);

            _employeeTeams[employeeId] = teamId;
            return teamId;
        }

        private async Task<List<int>> GetTeamSupervisorsAsync(int teamId, CancellationToken cancellationToken)
        {
            if (_teamSupervisors.TryGetValue(teamId, out var cached))
                return cached;

            var supervisors = await _context.Teams
                .AsNoTracking()
                .Where(t => t.Id == teamId && t.ManagerEmployee != null && t.ManagerEmployee.UserId != null)
                .Select(t => t.ManagerEmployee!.UserId!.Value)
                .ToListAsync(cancellationToken);

            // Managers who simply belong to the team also need visibility.
            supervisors.AddRange(await _context.Employees
                .AsNoTracking()
                .Where(e => e.TeamId == teamId && e.UserId != null && e.User!.Role.Role_name == LeadRoles.Manager)
                .Select(e => e.UserId!.Value)
                .ToListAsync(cancellationToken));

            var distinct = supervisors.Distinct().ToList();
            _teamSupervisors[teamId] = distinct;
            return distinct;
        }

        public async Task<List<LeadNotificationDto>> GetMyNotificationsAsync(
            LeadUserContext ctx, bool unreadOnly, int take, CancellationToken cancellationToken = default)
        {
            LeadAccess.EnsureStaff(ctx);

            var query = _context.LeadNotifications
                .AsNoTracking()
                .Where(n => n.RecipientUserId == ctx.UserId);

            if (unreadOnly)
                query = query.Where(n => !n.IsRead);

            return await query
                .OrderByDescending(n => n.CreatedAt)
                .ThenByDescending(n => n.Id)
                .Take(Math.Clamp(take, 1, 200))
                .Select(n => new LeadNotificationDto
                {
                    Id = n.Id,
                    LeadId = n.LeadId,
                    LeadReference = n.Lead.LeadReference,
                    Type = n.Type,
                    Title = n.Title,
                    Body = n.Body,
                    IsRead = n.IsRead,
                    IsEscalation = n.IsEscalation,
                    CreatedAt = n.CreatedAt
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<int> GetUnreadCountAsync(LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            LeadAccess.EnsureStaff(ctx);
            return await _context.LeadNotifications
                .CountAsync(n => n.RecipientUserId == ctx.UserId && !n.IsRead, cancellationToken);
        }

        public async Task MarkReadAsync(int notificationId, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            LeadAccess.EnsureStaff(ctx);

            var notification = await _context.LeadNotifications
                .FirstOrDefaultAsync(n => n.Id == notificationId && n.RecipientUserId == ctx.UserId, cancellationToken);

            if (notification == null)
                throw new InvalidOperationException("Notification not found.");

            if (notification.IsRead)
                return;

            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task MarkAllReadAsync(LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            LeadAccess.EnsureStaff(ctx);

            var unread = await _context.LeadNotifications
                .Where(n => n.RecipientUserId == ctx.UserId && !n.IsRead)
                .ToListAsync(cancellationToken);

            if (unread.Count == 0)
                return;

            var now = DateTime.UtcNow;
            foreach (var notification in unread)
            {
                notification.IsRead = true;
                notification.ReadAt = now;
            }

            await _context.SaveChangesAsync(cancellationToken);
        }

        private static string Normalize(string key) => LeadContactNormalizer.Limit(key, 200);
    }
}
