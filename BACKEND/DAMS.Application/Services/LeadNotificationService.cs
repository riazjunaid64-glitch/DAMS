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
            CancellationToken cancellationToken = default)
        {
            // A single scan raises alerts on hundreds of leads that share the same supervisors;
            // resolve them once per request rather than per lead.
            _supervisorUserIds ??= await CrmSupervisorUserIdsAsync(_context, cancellationToken);
            var created = 0;

            foreach (var userId in _supervisorUserIds)
            {
                var dedupKey = LeadService.BuildDedupKey(type, lead.Id, userId, dedupKeySuffix);
                if (await QueueAsync(lead.Id, userId, type, title, body, dedupKey, isEscalation, cancellationToken))
                    created++;
            }

            return created;
        }

        /// <summary>
        /// Everyone who runs the CRM: every Admin and every Sales Manager who can still sign in.
        /// Both see every lead (see <see cref="LeadAccess.Scope"/>), so both hear about every
        /// lead's progress and problems. Managers need an active employee record — the
        /// notification policy refuses them otherwise, and each refusal would be logged as a
        /// producer mistake.
        /// </summary>
        internal static Task<List<int>> CrmSupervisorUserIdsAsync(AppDbContext context, CancellationToken cancellationToken) =>
            context.Users
                .AsNoTracking()
                .Where(u => u.AccountStatus == UserAccountStatus.Active
                            && (u.Role.Role_name == LeadRoles.Admin
                                || (u.Role.Role_name == LeadRoles.Manager
                                    && context.Employees.Any(e => e.UserId == u.UserId && e.Status == EmployeeStatus.Active))))
                .Select(u => u.UserId)
                .ToListAsync(cancellationToken);

        private List<int>? _supervisorUserIds;
        private readonly Dictionary<int, (string Name, string Reference)> _leads = new();
        private readonly Dictionary<int, string?> _employeeNames = new();

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
