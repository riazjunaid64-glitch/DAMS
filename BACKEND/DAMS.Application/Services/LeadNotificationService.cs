using DAMS.Application.Common;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    /// <summary>
    /// The lead module's doorway into the central notification platform.
    ///
    /// The lead CRM's alert rules — who hears about an overdue follow-up, when a manager is
    /// escalated to, how a repeating scan avoids nagging — are unchanged and still live in the
    /// lead services. What changed is where the resulting notification goes: one row in the
    /// central store, with delivery, preferences, templates and history handled by the
    /// platform, instead of a lead-only table that only ever appeared in-app.
    ///
    /// This deliberately keeps the same shape the lead services already call, so the CRM's
    /// behaviour is preserved rather than reimplemented.
    /// </summary>
    public class LeadNotificationService : ILeadNotificationService
    {
        private readonly AppDbContext _context;
        private readonly INotificationDispatcher _dispatcher;

        public LeadNotificationService(AppDbContext context, INotificationDispatcher dispatcher)
        {
            _context = context;
            _dispatcher = dispatcher;
        }

        public async Task<bool> QueueAsync(
            int leadId,
            int recipientUserId,
            NotificationType type,
            string title,
            string? body,
            string dedupKey,
            bool isEscalation = false,
            CancellationToken cancellationToken = default)
        {
            if (recipientUserId <= 0)
                return false;

            var lead = await DescribeAsync(leadId, cancellationToken);

            return await _dispatcher.QueueAsync(new NotificationRequest
            {
                Type = type,
                RecipientUserId = recipientUserId,
                DedupKey = dedupKey,
                Title = LeadContactNormalizer.Limit(title, 200),
                Message = LeadContactNormalizer.LimitOrNull(body, 2000) ?? string.Empty,
                EntityType = NotificationEntityType.Lead,
                EntityId = leadId,
                IsEscalation = isEscalation,
                Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["leadName"] = lead.Name,
                    ["leadReference"] = lead.Reference,
                    ["employeeName"] = await EmployeeNameAsync(recipientUserId, cancellationToken)
                }
            }, cancellationToken);
        }

        public async Task<int> QueueForSupervisorsAsync(
            Lead lead,
            NotificationType type,
            string title,
            string? body,
            string dedupKeySuffix,
            bool isEscalation = false,
            bool includeQueueManagers = false,
            CancellationToken cancellationToken = default)
        {
            var recipients = await GetSupervisorUserIdsAsync(lead, includeQueueManagers, cancellationToken);
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
        private List<int>? _queueManagerUserIds;
        private readonly Dictionary<int, List<int>> _teamSupervisors = new();
        private readonly Dictionary<int, int?> _employeeTeams = new();
        private readonly Dictionary<int, (string Name, string Reference)> _leads = new();
        private readonly Dictionary<int, string?> _employeeNames = new();

        /// <summary>
        /// Everyone who should be told about a lead's problems: every admin, plus the
        /// manager of the owning team when there is one. An unassigned lead has no team, and
        /// the unassigned queue belongs to every manager (see <see cref="LeadAccess.Scope"/>),
        /// so every active manager is told — they are the ones who have to hand it out.
        /// Only used for new-lead and repeat-enquiry alerts when includeQueueManagers is true.
        /// </summary>
        private async Task<List<int>> GetSupervisorUserIdsAsync(Lead lead, bool includeQueueManagers = false, CancellationToken cancellationToken = default)
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

            if (includeQueueManagers && lead.AssignmentState == LeadAssignmentState.Unassigned)
                recipients.AddRange(await GetQueueManagersAsync(cancellationToken));

            return recipients.Distinct().ToList();
        }

        // Only managers with an active employee record: the notification policy refuses
        // anyone else, and each refusal would be logged as a producer mistake.
        private async Task<List<int>> GetQueueManagersAsync(CancellationToken cancellationToken) =>
            _queueManagerUserIds ??= await _context.Employees
                .AsNoTracking()
                .Where(e => e.Status == EmployeeStatus.Active && e.UserId != null
                            && e.User!.Role.Role_name == LeadRoles.Manager)
                .Select(e => e.UserId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);

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

        /// <summary>Lead name and reference, so an email or push template can name the record
        /// without the lead services having to know what a template variable is.</summary>
        private async Task<(string Name, string Reference)> DescribeAsync(int leadId, CancellationToken cancellationToken)
        {
            if (_leads.TryGetValue(leadId, out var cached))
                return cached;

            // A brand-new lead is notified before its final LD-###### reference has been
            // flushed to the database (LeadService queues the notification inside the same
            // save that writes that reference, to close a durability gap). An AsNoTracking
            // query run at that moment would still see the LD-PENDING placeholder, so the
            // change tracker — which already holds the in-memory, post-assignment value for
            // any lead this same request created or loaded — is checked first.
            var tracked = _context.ChangeTracker.Entries<Lead>()
                .Select(e => e.Entity)
                .FirstOrDefault(l => l.Id == leadId);

            var described = tracked != null
                ? ($"{tracked.FirstName} {tracked.LastName}".Trim(), tracked.LeadReference)
                : await DescribeFromDatabaseAsync(leadId, cancellationToken);

            _leads[leadId] = described;
            return described;
        }

        private async Task<(string Name, string Reference)> DescribeFromDatabaseAsync(
            int leadId, CancellationToken cancellationToken)
        {
            var lead = await _context.Leads
                .AsNoTracking()
                .Where(l => l.Id == leadId)
                .Select(l => new { l.FirstName, l.LastName, l.LeadReference })
                .FirstOrDefaultAsync(cancellationToken);

            return lead == null
                ? (string.Empty, string.Empty)
                : ($"{lead.FirstName} {lead.LastName}".Trim(), lead.LeadReference);
        }

        private async Task<string?> EmployeeNameAsync(int userId, CancellationToken cancellationToken)
        {
            if (_employeeNames.TryGetValue(userId, out var cached))
                return cached;

            var name = await _context.Employees
                .AsNoTracking()
                .Where(e => e.UserId == userId)
                .Select(e => e.FullName)
                .FirstOrDefaultAsync(cancellationToken);

            _employeeNames[userId] = name;
            return name;
        }
    }
}
