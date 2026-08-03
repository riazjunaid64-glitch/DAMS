using System.Linq.Expressions;
using DAMS.Application.Common;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services.Notifications
{
    /// <summary>
    /// The notification platform's single authorization policy. It owns the role/type matrix,
    /// category metadata, creation-time recipient checks, inbox query scope and delivery-time
    /// revalidation. Unknown roles, types and inconsistent stored mappings are denied.
    /// </summary>
    public sealed class NotificationEligibilityPolicy
    {
        private readonly AppDbContext _context;

        private static readonly HashSet<NotificationType> CustomerTypes = new()
        {
            NotificationType.PaymentReceipt,
            NotificationType.BookingRequestReceived,
            NotificationType.BookingApproved,
            NotificationType.BookingRejected,
            NotificationType.BookingCancelled,
            NotificationType.PossessionGiven,
            NotificationType.SaleCompleted,
            NotificationType.InstallmentDue,
            NotificationType.InstallmentOverdue,
            NotificationType.ProjectUpdated,
            NotificationType.AdminAnnouncement,
            NotificationType.AccountSecurity
        };

        private static readonly HashSet<NotificationType> EmployeeTypes = new()
        {
            NotificationType.LeadCreated,
            NotificationType.LeadAssigned,
            NotificationType.LeadReassigned,
            NotificationType.LeadStageChanged,
            NotificationType.LeadConverted,
            NotificationType.LeadClosed,
            NotificationType.LeadInactive,
            NotificationType.FirstContactDue,
            NotificationType.FirstContactOverdue,
            NotificationType.FollowUpAssigned,
            NotificationType.FollowUpDue,
            NotificationType.FollowUpOverdue,
            NotificationType.SiteVisitScheduled,
            NotificationType.SiteVisitUpdated,
            NotificationType.SiteVisitReminder,
            NotificationType.UserMentioned,
            NotificationType.EmployeeTaskAssigned,
            NotificationType.ProjectUpdated,
            NotificationType.AdminAnnouncement,
            NotificationType.AccountSecurity
        };

        private static readonly HashSet<NotificationType> ManagerTypes = new(EmployeeTypes)
        {
            NotificationType.FollowUpMissed,
            NotificationType.SiteVisitMissed,
            NotificationType.ManagerAttentionRequired
        };

        // Admins supervise internal work and system delivery. Customer contractual messages
        // are deliberately absent: an admin only sees those through delivery/audit history.
        private static readonly HashSet<NotificationType> AdminTypes = new(ManagerTypes);

        // Addressed to one person because of a relationship they hold with the record. Every
        // role — admins included — must still hold that relationship, so a producer that picks
        // the wrong recipient cannot hand somebody else's assignment to a supervisor.
        private static readonly HashSet<NotificationType> OwnerAddressedLeadTypes = new()
        {
            NotificationType.LeadAssigned,
            NotificationType.LeadInactive,
            NotificationType.FirstContactDue,
            NotificationType.FirstContactOverdue,
            NotificationType.FollowUpAssigned,
            NotificationType.FollowUpDue,
            NotificationType.FollowUpOverdue,
            NotificationType.SiteVisitScheduled,
            NotificationType.SiteVisitUpdated,
            NotificationType.SiteVisitReminder,
            NotificationType.UserMentioned
        };

        // Raised about a lead's progress rather than to its owner, so supervisors hear about
        // leads they do not personally work: every admin, and a manager's own teams.
        private static readonly NotificationType[] SupervisoryLeadTypes =
        {
            NotificationType.LeadCreated,
            NotificationType.LeadReassigned,
            NotificationType.LeadStageChanged,
            NotificationType.LeadConverted,
            NotificationType.LeadClosed,
            NotificationType.FollowUpMissed,
            NotificationType.SiteVisitMissed,
            NotificationType.ManagerAttentionRequired
        };

        private static readonly CategoryDescriptor[] Categories =
        {
            new(NotificationCategory.PaymentsAndReceipts, "Payments and receipts", "Payment confirmations and official receipts."),
            new(NotificationCategory.BookingUpdates, "Booking updates", "Approvals, rejections, cancellations and possession updates."),
            new(NotificationCategory.InstallmentReminders, "Installment reminders", "Reminders before and after an installment falls due."),
            new(NotificationCategory.LeadAssignments, "Lead assignments", "Lead ownership and pipeline updates relevant to your work."),
            new(NotificationCategory.FollowUps, "Follow-ups", "Follow-up work assigned to you or your managed team."),
            new(NotificationCategory.SiteVisits, "Site visits", "Visits assigned to you or your managed team."),
            new(NotificationCategory.Mentions, "Mentions", "A permitted colleague mentions you in an internal comment."),
            new(NotificationCategory.EmployeeTasks, "Employee tasks", "Tasks assigned directly to you."),
            new(NotificationCategory.ProjectUpdates, "Project updates", "Progress and news on projects relevant to you."),
            new(NotificationCategory.Announcements, "Announcements", "Messages explicitly addressed to your audience."),
            new(NotificationCategory.AccountAndSecurity, "Account and security", "Important account and security messages."),
            new(NotificationCategory.ManagerEscalations, "Escalations", "Managed-team issues that require supervisor attention.")
        };

        public NotificationEligibilityPolicy(AppDbContext context)
        {
            _context = context;
        }

        public bool CanRoleReceive(string? role, NotificationType type)
        {
            if (!NotificationCatalog.TryGet(type, out _))
                return false;

            return NormalizeRole(role) switch
            {
                LeadRoles.Admin => AdminTypes.Contains(type),
                LeadRoles.Manager => ManagerTypes.Contains(type),
                LeadRoles.Employee => EmployeeTypes.Contains(type),
                "Client" => CustomerTypes.Contains(type),
                _ => false
            };
        }

        public bool CanRoleUseCategory(string? role, NotificationCategory category) =>
            AllowedDefinitions(role).Any(d => d.Category == category);

        public IReadOnlyList<CategoryDescriptor> CategoriesForRole(string? role)
        {
            var allowed = AllowedDefinitions(role).Select(d => d.Category).ToHashSet();
            return Categories.Where(c => allowed.Contains(c.Category)).ToList();
        }

        public string EmptyStateForRole(string? role) => NormalizeRole(role) switch
        {
            "Client" => "Booking, payment, installment, project, announcement and account updates will appear here.",
            LeadRoles.Manager => "Your work, managed-team activity, escalations and account updates will appear here.",
            LeadRoles.Employee => "Assigned leads, follow-ups, visits, mentions, tasks and account updates will appear here.",
            LeadRoles.Admin => "Administrative, staff, delivery and account updates will appear here.",
            _ => "No notification categories are available for this account."
        };

        public void EnsureCategoryAllowed(NotificationUserContext ctx, NotificationCategory? category)
        {
            if (category.HasValue && !CanRoleUseCategory(ctx.Role, category.Value))
                throw new LeadAuthorizationException("That notification category is not available for your account.");
        }

        /// <summary>Applies exact type/category pairs, not two independent allow-lists.</summary>
        public IQueryable<Notification> ApplyRoleScope(IQueryable<Notification> query, string? role)
        {
            var definitions = AllowedDefinitions(role);
            if (definitions.Count == 0)
                return query.Where(_ => false);

            var notification = Expression.Parameter(typeof(Notification), "notification");
            Expression body = Expression.Constant(false);

            foreach (var definition in definitions)
            {
                var typeMatch = Expression.Equal(
                    Expression.Property(notification, nameof(Notification.Type)),
                    Expression.Constant(definition.Type));
                var categoryMatch = Expression.Equal(
                    Expression.Property(notification, nameof(Notification.Category)),
                    Expression.Constant(definition.Category));
                body = Expression.OrElse(body, Expression.AndAlso(typeMatch, categoryMatch));
            }

            return query.Where(Expression.Lambda<Func<Notification, bool>>(body, notification));
        }

        /// <summary>
        /// Resolves once what the reader currently owns, so the inbox filter below is a single
        /// query rather than a per-row eligibility call.
        /// </summary>
        public async Task<ResourceScope> BuildResourceScopeAsync(
            int userId, string? role, CancellationToken cancellationToken = default)
        {
            var normalized = NormalizeRole(role);
            if (normalized is null or "Client")
                return new ResourceScope(userId, normalized, 0, Array.Empty<int>());

            var employee = await _context.Employees.AsNoTracking()
                .Where(e => e.UserId == userId && e.Status == EmployeeStatus.Active)
                .Select(e => new { e.Id, e.TeamId })
                .FirstOrDefaultAsync(cancellationToken);

            if (employee == null)
                return new ResourceScope(userId, normalized, 0, Array.Empty<int>());

            var teamIds = await _context.Teams.AsNoTracking()
                .Where(t => t.IsActive && t.ManagerEmployeeId == employee.Id)
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);
            if (employee.TeamId.HasValue)
                teamIds.Add(employee.TeamId.Value);

            return new ResourceScope(userId, normalized, employee.Id, teamIds.Distinct().ToArray());
        }

        /// <summary>
        /// The read-side twin of <see cref="ResourceMatchesAsync"/>: same ownership rules, in a
        /// form the database can answer for a whole page at once. Without it a row that was
        /// misrouted or corrupted still shows its title and message in a listing, because
        /// ownership was only ever re-checked when the notification was opened.
        ///
        /// It judges committed state only. The creation-time check keeps its change-tracker
        /// fast paths for work that has not been saved yet; nothing is readable at that point.
        ///
        /// It withholds a row when the record it names exists and belongs to somebody else —
        /// not when the record has simply gone. A message about a deleted record still says
        /// what happened, and opening it explains that the record is no longer there.
        /// </summary>
        public IQueryable<Notification> ApplyResourceScope(IQueryable<Notification> query, ResourceScope scope) =>
            scope.Role switch
            {
                "Client" => query.Where(CustomerResourceFilter(scope.UserId)),
                LeadRoles.Admin or LeadRoles.Manager or LeadRoles.Employee => query.Where(StaffResourceFilter(scope)),
                _ => query.Where(_ => false)
            };

        private Expression<Func<Notification, bool>> CustomerResourceFilter(int userId) => n =>
            (n.Type == NotificationType.AdminAnnouncement
             && (n.EntityType == NotificationEntityType.None || n.EntityType == NotificationEntityType.Announcement))
            || (n.Type == NotificationType.AccountSecurity
                && (n.EntityType == NotificationEntityType.None
                    || n.EntityType == NotificationEntityType.Announcement
                    || (n.EntityType == NotificationEntityType.Account
                        && (n.EntityId == null || n.EntityId <= 0 || n.EntityId == userId))))
            || (n.Type == NotificationType.ProjectUpdated
                && (n.EntityType == NotificationEntityType.Announcement
                    || (n.EntityType == NotificationEntityType.Project && n.EntityId > 0
                        && (!_context.Projects.Any(p => p.Id == n.EntityId)
                            || _context.Bookings.Any(b => b.Customer.UserId == userId
                                && b.Customer.Status == CustomerStatus.Active
                                && b.Status != BookingStatus.Cancelled
                                && b.Unit.ProjectId == n.EntityId)))))
            || (n.Type == NotificationType.PaymentReceipt
                && n.EntityType == NotificationEntityType.Payment && n.EntityId > 0
                && (!_context.Payments.Any(p => p.Id == n.EntityId)
                    || _context.Payments.Any(p => p.Id == n.EntityId && p.Booking.Customer.UserId == userId)))
            || ((n.Type == NotificationType.BookingRequestReceived || n.Type == NotificationType.BookingRejected)
                && n.EntityType == NotificationEntityType.BookingRequest && n.EntityId > 0
                && (!_context.BookingRequests.Any(r => r.Id == n.EntityId)
                    || _context.BookingRequests.Any(r => r.Id == n.EntityId && r.UserId == userId)))
            || ((n.Type == NotificationType.BookingApproved || n.Type == NotificationType.BookingRejected
                 || n.Type == NotificationType.BookingCancelled || n.Type == NotificationType.PossessionGiven
                 || n.Type == NotificationType.SaleCompleted)
                && n.EntityType == NotificationEntityType.Booking && n.EntityId > 0
                && (!_context.Bookings.Any(b => b.Id == n.EntityId)
                    || _context.Bookings.Any(b => b.Id == n.EntityId && b.Customer.UserId == userId)))
            || ((n.Type == NotificationType.InstallmentDue || n.Type == NotificationType.InstallmentOverdue)
                && n.EntityType == NotificationEntityType.Installment && n.EntityId > 0
                && (!_context.Installments.Any(i => i.Id == n.EntityId)
                    || _context.Installments.Any(i => i.Id == n.EntityId && i.Booking.Customer.UserId == userId)));

        private Expression<Func<Notification, bool>> StaffResourceFilter(ResourceScope scope)
        {
            var userId = scope.UserId;
            var employeeId = scope.EmployeeId;
            var teamIds = scope.ManagedTeamIds;
            var isAdmin = scope.Role == LeadRoles.Admin;
            var isManager = scope.Role == LeadRoles.Manager;
            var supervisory = SupervisoryLeadTypes;

            return n =>
                (n.Type == NotificationType.AdminAnnouncement
                 && (n.EntityType == NotificationEntityType.None || n.EntityType == NotificationEntityType.Announcement))
                || (n.Type == NotificationType.AccountSecurity
                    && (n.EntityType == NotificationEntityType.None
                        || n.EntityType == NotificationEntityType.Announcement
                        || (n.EntityType == NotificationEntityType.Account
                            && (n.EntityId == null || n.EntityId <= 0 || n.EntityId == userId))))
                || (n.Type == NotificationType.ProjectUpdated
                    && (n.EntityType == NotificationEntityType.Announcement
                        || (n.EntityType == NotificationEntityType.Project && n.EntityId > 0
                            && (!_context.Projects.Any(p => p.Id == n.EntityId)
                                || isAdmin
                                || _context.EmployeeTasks.Any(t => t.ProjectId == n.EntityId
                                    && t.Employee.Status == EmployeeStatus.Active
                                    && (t.Employee.UserId == userId
                                        || (isManager && t.Employee.Team != null
                                            && t.Employee.Team.ManagerEmployee != null
                                            && t.Employee.Team.ManagerEmployee.UserId == userId)))))))
                || (n.Type == NotificationType.EmployeeTaskAssigned
                    && n.EntityType == NotificationEntityType.EmployeeTask && n.EntityId > 0
                    && (!_context.EmployeeTasks.Any(t => t.Id == n.EntityId)
                        || _context.EmployeeTasks.Any(t => t.Id == n.EntityId
                            && t.Employee.UserId == userId
                            && t.Employee.Status == EmployeeStatus.Active)))
                || (n.EntityType == NotificationEntityType.Lead && n.EntityId > 0
                    && (!_context.Leads.Any(l => l.Id == n.EntityId)
                        || (n.Type == NotificationType.UserMentioned
                         && _context.LeadCommentMentions.Any(m => m.MentionedUserId == userId
                             && m.LeadComment.LeadId == n.EntityId))
                        || ((n.Type == NotificationType.FollowUpAssigned
                             || n.Type == NotificationType.FollowUpDue
                             || n.Type == NotificationType.FollowUpOverdue)
                            && _context.LeadFollowUps.Any(f => f.LeadId == n.EntityId
                                && f.AssignedEmployeeId == employeeId))
                        || ((n.Type == NotificationType.SiteVisitScheduled
                             || n.Type == NotificationType.SiteVisitUpdated
                             || n.Type == NotificationType.SiteVisitReminder)
                            && _context.LeadSiteVisits.Any(v => v.LeadId == n.EntityId
                                && v.AssignedEmployeeId == employeeId))
                        || ((n.Type == NotificationType.LeadAssigned
                             || n.Type == NotificationType.LeadInactive
                             || n.Type == NotificationType.FirstContactDue
                             || n.Type == NotificationType.FirstContactOverdue)
                            && _context.Leads.Any(l => l.Id == n.EntityId && l.AssignedEmployeeId == employeeId))
                        || (supervisory.Contains(n.Type)
                            && (isAdmin
                                || (isManager && _context.Leads.Any(l => l.Id == n.EntityId
                                    && (l.AssignedEmployeeId == employeeId
                                        || (l.AssignedTeamId != null && teamIds.Contains(l.AssignedTeamId.Value))
                                        || (l.AssignedEmployee != null && l.AssignedEmployee.TeamId != null
                                            && teamIds.Contains(l.AssignedEmployee.TeamId.Value))
                                        || l.AssignmentState == LeadAssignmentState.Unassigned)))
                                || (!isAdmin && !isManager && _context.Leads.Any(l => l.Id == n.EntityId
                                    && l.AssignedEmployeeId == employeeId))))));
        }

        public async Task<bool> CanReceiveAsync(
            int userId,
            NotificationType type,
            NotificationEntityType entityType = NotificationEntityType.None,
            int? entityId = null,
            CancellationToken cancellationToken = default)
        {
            var recipient = await LoadRecipientAsync(userId, cancellationToken);
            if (recipient == null || !CanRoleReceive(recipient.Role, type))
                return false;

            // A booking request is linked directly to its login before approval creates a
            // Customer row. The resource check below still proves ownership of that request.
            var directRequestOwner = string.Equals(recipient.Role, "Client", StringComparison.OrdinalIgnoreCase)
                && type is NotificationType.BookingRequestReceived or NotificationType.BookingRejected
                && entityType == NotificationEntityType.BookingRequest;
            if (!recipient.HasRequiredAccount && !directRequestOwner)
                return false;

            return await ResourceMatchesAsync(recipient, type, entityType, entityId, cancellationToken);
        }

        public async Task<bool> CanReceiveAsync(
            Notification notification, CancellationToken cancellationToken = default)
        {
            if (notification.RecipientUserId is not > 0
                || !NotificationCatalog.TryGet(notification.Type, out var definition)
                || definition.Category != notification.Category)
                return false;

            return await CanReceiveAsync(
                notification.RecipientUserId.Value,
                notification.Type,
                notification.EntityType,
                notification.EntityId,
                cancellationToken);
        }

        public async Task<bool> CanDeliverAsync(
            Notification notification, CancellationToken cancellationToken = default)
        {
            if (!NotificationCatalog.TryGet(notification.Type, out var definition)
                || definition.Category != notification.Category)
                return false;

            if (notification.RecipientUserId is > 0)
                return await CanReceiveAsync(notification, cancellationToken);

            if (!SmtpEmailSender.IsValidAddress(notification.RecipientEmail))
                return false;

            return await ContactMatchesAsync(new NotificationRequest
            {
                Type = notification.Type,
                RecipientEmail = notification.RecipientEmail,
                RecipientName = notification.RecipientName,
                DedupKey = notification.DedupKey,
                EntityType = notification.EntityType,
                EntityId = notification.EntityId
            }, cancellationToken);
        }

        public async Task<bool> CanQueueAsync(
            NotificationRequest request, CancellationToken cancellationToken = default)
        {
            if (!NotificationCatalog.TryGet(request.Type, out _))
                return false;

            if (request.RecipientUserId is > 0)
            {
                return await CanReceiveAsync(
                    request.RecipientUserId.Value,
                    request.Type,
                    request.EntityType,
                    request.EntityId,
                    cancellationToken);
            }

            return await ContactMatchesAsync(request, cancellationToken);
        }

        public async Task<List<NotificationRecipientTarget>> FilterEligibleTargetsAsync(
            NotificationType type,
            IEnumerable<NotificationRecipientTarget> targets,
            CancellationToken cancellationToken = default)
        {
            if (!NotificationCatalog.TryGet(type, out _))
                return new List<NotificationRecipientTarget>();

            var materialized = targets
                .DistinctBy(t => t.UserId.HasValue ? $"u:{t.UserId}" : $"e:{t.Email?.Trim().ToLowerInvariant()}")
                .ToList();
            var ids = materialized.Where(t => t.UserId is > 0).Select(t => t.UserId!.Value).Distinct().ToArray();

            var eligibleIds = ids.Length == 0
                ? new HashSet<int>()
                : (await _context.Users
                    .AsNoTracking()
                    .Where(u => ids.Contains(u.UserId))
                    .Select(u => new
                    {
                        u.UserId,
                        Role = u.Role.Role_name,
                        HasCustomer = _context.Customers.Any(c => c.UserId == u.UserId && c.Status == CustomerStatus.Active),
                        HasEmployee = _context.Employees.Any(e => e.UserId == u.UserId && e.Status == EmployeeStatus.Active)
                    })
                    .ToListAsync(cancellationToken))
                    .Where(u => CanRoleReceive(u.Role, type)
                                && RequiredAccountExists(u.Role, u.HasCustomer, u.HasEmployee))
                    .Select(u => u.UserId)
                    .ToHashSet();

            return materialized
                .Where(target => target.UserId is > 0
                    ? eligibleIds.Contains(target.UserId.Value)
                    : CustomerTypes.Contains(type) && SmtpEmailSender.IsValidAddress(target.Email))
                .ToList();
        }

        private async Task<bool> ResourceMatchesAsync(
            RecipientSnapshot recipient,
            NotificationType type,
            NotificationEntityType entityType,
            int? entityId,
            CancellationToken cancellationToken)
        {
            if (type == NotificationType.AdminAnnouncement)
                return entityType is NotificationEntityType.None or NotificationEntityType.Announcement;

            if (type == NotificationType.AccountSecurity)
                return entityType is NotificationEntityType.None or NotificationEntityType.Announcement
                       || (entityType == NotificationEntityType.Account
                           && (entityId is null or <= 0 || entityId == recipient.UserId));

            if (type == NotificationType.ProjectUpdated)
            {
                if (entityType == NotificationEntityType.Announcement)
                    return true;
                if (entityType != NotificationEntityType.Project || entityId is not > 0)
                    return false;
                if (string.Equals(recipient.Role, LeadRoles.Admin, StringComparison.OrdinalIgnoreCase))
                    return await _context.Projects.AnyAsync(p => p.Id == entityId.Value, cancellationToken);

                if (string.Equals(recipient.Role, LeadRoles.Employee, StringComparison.OrdinalIgnoreCase))
                    return await _context.EmployeeTasks.AnyAsync(task =>
                        task.ProjectId == entityId.Value
                        && task.Employee.UserId == recipient.UserId
                        && task.Employee.Status == EmployeeStatus.Active, cancellationToken);

                if (string.Equals(recipient.Role, LeadRoles.Manager, StringComparison.OrdinalIgnoreCase))
                    return await _context.EmployeeTasks.AnyAsync(task =>
                        task.ProjectId == entityId.Value
                        && task.Employee.Status == EmployeeStatus.Active
                        && (task.Employee.UserId == recipient.UserId
                            || (task.Employee.Team != null
                                && task.Employee.Team.ManagerEmployee != null
                                && task.Employee.Team.ManagerEmployee.UserId == recipient.UserId)), cancellationToken);

                if (!string.Equals(recipient.Role, "Client", StringComparison.OrdinalIgnoreCase))
                    return false;

                return await _context.Bookings.AnyAsync(b =>
                    b.Customer.UserId == recipient.UserId
                    && b.Customer.Status == CustomerStatus.Active
                    && b.Status != BookingStatus.Cancelled
                    && b.Unit.ProjectId == entityId.Value, cancellationToken);
            }

            if (type == NotificationType.PaymentReceipt)
                return entityType == NotificationEntityType.Payment && entityId is > 0
                       && await _context.Payments.AnyAsync(p =>
                           p.Id == entityId.Value && p.Booking.Customer.UserId == recipient.UserId, cancellationToken);

            if ((type is NotificationType.BookingRequestReceived or NotificationType.BookingRejected)
                && entityType == NotificationEntityType.BookingRequest)
                return entityId is > 0 && await _context.BookingRequests.AnyAsync(r =>
                    r.Id == entityId.Value && r.UserId == recipient.UserId, cancellationToken);

            if (type is NotificationType.BookingApproved or NotificationType.BookingRejected
                or NotificationType.BookingCancelled or NotificationType.PossessionGiven or NotificationType.SaleCompleted)
                return entityType == NotificationEntityType.Booking && entityId is > 0
                       && await _context.Bookings.AnyAsync(b =>
                           b.Id == entityId.Value && b.Customer.UserId == recipient.UserId, cancellationToken);

            if (type is NotificationType.InstallmentDue or NotificationType.InstallmentOverdue)
                return entityType == NotificationEntityType.Installment && entityId is > 0
                       && await _context.Installments.AnyAsync(i =>
                           i.Id == entityId.Value && i.Booking.Customer.UserId == recipient.UserId, cancellationToken);

            if (type == NotificationType.EmployeeTaskAssigned)
            {
                // "Assigned to you" means exactly that for every role: an admin hears about a
                // task through the employee module, not through the assignee's own message.
                return entityType == NotificationEntityType.EmployeeTask && entityId is > 0
                       && await _context.EmployeeTasks.AnyAsync(t =>
                           t.Id == entityId.Value && t.Employee.UserId == recipient.UserId
                           && t.Employee.Status == EmployeeStatus.Active, cancellationToken);
            }

            if (NotificationCatalog.GetRequired(type).Module == NotificationModule.Leads)
                return await LeadResourceMatchesAsync(recipient, type, entityType, entityId, cancellationToken);

            return false;
        }

        private async Task<bool> LeadResourceMatchesAsync(
            RecipientSnapshot recipient,
            NotificationType type,
            NotificationEntityType entityType,
            int? entityId,
            CancellationToken cancellationToken)
        {
            if (entityType != NotificationEntityType.Lead || entityId is not > 0)
                return false;

            // A mention names a login, so an admin who has no employee record still qualifies
            // for one they were actually named in — and for no other.
            if (type == NotificationType.UserMentioned)
            {
                if (_context.ChangeTracker.Entries<LeadCommentMention>().Any(entry =>
                        entry.State != EntityState.Deleted
                        && entry.Entity.MentionedUserId == recipient.UserId
                        && entry.Entity.LeadComment?.LeadId == entityId.Value))
                    return true;

                return await _context.LeadCommentMentions.AnyAsync(m =>
                    m.MentionedUserId == recipient.UserId && m.LeadComment.LeadId == entityId.Value, cancellationToken);
            }

            var trackedLead = _context.ChangeTracker.Entries<Lead>()
                .Where(entry => entry.State != EntityState.Deleted && entry.Entity.Id == entityId.Value)
                .Select(entry => entry.Entity)
                .FirstOrDefault();

            var isAdmin = string.Equals(recipient.Role, LeadRoles.Admin, StringComparison.OrdinalIgnoreCase);
            if (isAdmin && !OwnerAddressedLeadTypes.Contains(type))
                return trackedLead != null
                       || await _context.Leads.AnyAsync(l => l.Id == entityId.Value, cancellationToken);

            var employee = await _context.Employees.AsNoTracking()
                .Where(e => e.UserId == recipient.UserId && e.Status == EmployeeStatus.Active)
                .Select(e => new { e.Id, e.TeamId })
                .FirstOrDefaultAsync(cancellationToken);
            if (employee == null)
                return false;

            if (type is NotificationType.FollowUpAssigned or NotificationType.FollowUpDue or NotificationType.FollowUpOverdue)
            {
                if (_context.ChangeTracker.Entries<LeadFollowUp>().Any(entry =>
                        entry.State != EntityState.Deleted
                        && entry.Entity.LeadId == entityId.Value
                        && entry.Entity.AssignedEmployeeId == employee.Id))
                    return true;

                return await _context.LeadFollowUps.AnyAsync(f =>
                    f.LeadId == entityId.Value && f.AssignedEmployeeId == employee.Id, cancellationToken);
            }

            if (type is NotificationType.SiteVisitScheduled or NotificationType.SiteVisitUpdated or NotificationType.SiteVisitReminder)
            {
                if (_context.ChangeTracker.Entries<LeadSiteVisit>().Any(entry =>
                        entry.State != EntityState.Deleted
                        && entry.Entity.LeadId == entityId.Value
                        && entry.Entity.AssignedEmployeeId == employee.Id))
                    return true;

                return await _context.LeadSiteVisits.AnyAsync(v =>
                    v.LeadId == entityId.Value && v.AssignedEmployeeId == employee.Id, cancellationToken);
            }

            if (!OwnerAddressedLeadTypes.Contains(type)
                && string.Equals(recipient.Role, LeadRoles.Manager, StringComparison.OrdinalIgnoreCase))
            {
                var managedTeams = await _context.Teams.AsNoTracking()
                    .Where(t => t.IsActive && t.ManagerEmployeeId == employee.Id)
                    .Select(t => t.Id)
                    .ToListAsync(cancellationToken);
                if (employee.TeamId.HasValue)
                    managedTeams.Add(employee.TeamId.Value);
                var teamIds = managedTeams.Distinct().ToArray();

                if (trackedLead != null && (trackedLead.AssignedEmployeeId == employee.Id
                    || (trackedLead.AssignedTeamId.HasValue && teamIds.Contains(trackedLead.AssignedTeamId.Value))
                    || trackedLead.AssignmentState == LeadAssignmentState.Unassigned))
                    return true;

                return await _context.Leads.AnyAsync(l => l.Id == entityId.Value
                    && (l.AssignedEmployeeId == employee.Id
                        || (l.AssignedTeamId != null && teamIds.Contains(l.AssignedTeamId.Value))
                        || (l.AssignedEmployee != null && l.AssignedEmployee.TeamId != null
                            && teamIds.Contains(l.AssignedEmployee.TeamId.Value))
                        || l.AssignmentState == LeadAssignmentState.Unassigned), cancellationToken);
            }

            if (trackedLead?.AssignedEmployeeId == employee.Id)
                return true;

            return await _context.Leads.AnyAsync(l =>
                l.Id == entityId.Value && l.AssignedEmployeeId == employee.Id, cancellationToken);
        }

        private async Task<bool> ContactMatchesAsync(NotificationRequest request, CancellationToken cancellationToken)
        {
            if (!CustomerTypes.Contains(request.Type) || !SmtpEmailSender.IsValidAddress(request.RecipientEmail))
                return false;

            var email = request.RecipientEmail!.Trim().ToLower();
            if (request.Type is NotificationType.AdminAnnouncement or NotificationType.AccountSecurity)
            {
                if (request.EntityType is not (NotificationEntityType.None or NotificationEntityType.Announcement))
                    return false;

                return await _context.Customers.AnyAsync(customer =>
                    customer.UserId == null
                    && customer.Status == CustomerStatus.Active
                    && customer.Email != null
                    && customer.Email.ToLower() == email, cancellationToken);
            }

            if (request.Type == NotificationType.ProjectUpdated)
                return request.EntityType == NotificationEntityType.Project && request.EntityId is > 0
                       && await _context.Bookings.AnyAsync(b =>
                           b.Unit.ProjectId == request.EntityId.Value
                           && b.Status != BookingStatus.Cancelled
                           && b.Customer.Status == CustomerStatus.Active
                           && b.Customer.Email != null
                           && b.Customer.Email.ToLower() == email, cancellationToken);

            if (request.Type == NotificationType.PaymentReceipt)
                return request.EntityType == NotificationEntityType.Payment && request.EntityId is > 0
                       && await _context.Payments.AnyAsync(p => p.Id == request.EntityId.Value
                           && p.Booking.Customer.Email != null
                           && p.Booking.Customer.Email.ToLower() == email, cancellationToken);

            if ((request.Type is NotificationType.BookingRequestReceived or NotificationType.BookingRejected)
                && request.EntityType == NotificationEntityType.BookingRequest)
                return request.EntityId is > 0 && await _context.BookingRequests.AnyAsync(r =>
                    r.Id == request.EntityId.Value && r.Email.ToLower() == email, cancellationToken);

            if (request.Type is NotificationType.BookingApproved or NotificationType.BookingRejected
                or NotificationType.BookingCancelled or NotificationType.PossessionGiven or NotificationType.SaleCompleted)
                return request.EntityType == NotificationEntityType.Booking && request.EntityId is > 0
                       && await _context.Bookings.AnyAsync(b => b.Id == request.EntityId.Value
                           && b.Customer.Email != null && b.Customer.Email.ToLower() == email, cancellationToken);

            if (request.Type is NotificationType.InstallmentDue or NotificationType.InstallmentOverdue)
                return request.EntityType == NotificationEntityType.Installment && request.EntityId is > 0
                       && await _context.Installments.AnyAsync(i => i.Id == request.EntityId.Value
                           && i.Booking.Customer.Email != null
                           && i.Booking.Customer.Email.ToLower() == email, cancellationToken);

            return false;
        }

        private async Task<RecipientSnapshot?> LoadRecipientAsync(int userId, CancellationToken cancellationToken)
        {
            var user = await _context.Users.AsNoTracking()
                .Where(u => u.UserId == userId)
                .Select(u => new
                {
                    u.UserId,
                    Role = u.Role.Role_name,
                    HasCustomer = _context.Customers.Any(c => c.UserId == u.UserId && c.Status == CustomerStatus.Active),
                    HasEmployee = _context.Employees.Any(e => e.UserId == u.UserId && e.Status == EmployeeStatus.Active)
                })
                .FirstOrDefaultAsync(cancellationToken);

            return user == null
                ? null
                : new RecipientSnapshot(
                    user.UserId,
                    user.Role,
                    RequiredAccountExists(user.Role, user.HasCustomer, user.HasEmployee));
        }

        private static bool RequiredAccountExists(string? role, bool hasCustomer, bool hasEmployee) =>
            NormalizeRole(role) switch
            {
                "Client" => hasCustomer,
                LeadRoles.Manager or LeadRoles.Employee => hasEmployee,
                LeadRoles.Admin => true,
                _ => false
            };

        private static List<NotificationDefinition> AllowedDefinitions(string? role) =>
            NotificationCatalog.All.Where(d => AllowedSet(role).Contains(d.Type)).ToList();

        private static HashSet<NotificationType> AllowedSet(string? role) => NormalizeRole(role) switch
        {
            LeadRoles.Admin => AdminTypes,
            LeadRoles.Manager => ManagerTypes,
            LeadRoles.Employee => EmployeeTypes,
            "Client" => CustomerTypes,
            _ => EmptyTypes
        };

        private static readonly HashSet<NotificationType> EmptyTypes = new();

        private static string? NormalizeRole(string? role)
        {
            if (string.Equals(role, LeadRoles.Admin, StringComparison.OrdinalIgnoreCase)) return LeadRoles.Admin;
            if (string.Equals(role, LeadRoles.Manager, StringComparison.OrdinalIgnoreCase)) return LeadRoles.Manager;
            if (string.Equals(role, LeadRoles.Employee, StringComparison.OrdinalIgnoreCase)) return LeadRoles.Employee;
            if (string.Equals(role, "Client", StringComparison.OrdinalIgnoreCase)) return "Client";
            return null;
        }

        private sealed record RecipientSnapshot(int UserId, string Role, bool HasRequiredAccount);

        /// <summary>What a reader owns right now. <see cref="EmployeeId"/> is 0 when there is
        /// no active employee record, which matches nothing.</summary>
        public sealed record ResourceScope(
            int UserId,
            string? Role,
            int EmployeeId,
            IReadOnlyList<int> ManagedTeamIds);

        public sealed record CategoryDescriptor(
            NotificationCategory Category,
            string Label,
            string Description);
    }
}
