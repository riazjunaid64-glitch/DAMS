using DAMS.Domain.Entities;
using DAMS.Domain.Enums;

namespace DAMS.Application.Common
{
    public static class LeadRoles
    {
        public const string Admin = "Admin";
        public const string Manager = "Manager";
        public const string Employee = "Employee";

        /// <summary>Roles allowed anywhere in the lead workspace.</summary>
        public const string Staff = Admin + "," + Manager + "," + Employee;

        public const string AdminOrManager = Admin + "," + Manager;
    }

    /// <summary>
    /// Who is acting on a lead, resolved once per request from the token plus the
    /// employee record it maps to. Every lead service takes one of these rather than a raw
    /// user id, so authorisation cannot be forgotten at a call site.
    /// </summary>
    public sealed class LeadUserContext
    {
        public required int UserId { get; init; }

        public required string Role { get; init; }

        public string? DisplayName { get; init; }

        /// <summary>The employee record linked to this login, when there is one.</summary>
        public int? EmployeeId { get; init; }

        public int? TeamId { get; init; }

        /// <summary>Teams a manager is responsible for: their own plus any they manage.</summary>
        public IReadOnlyList<int> ManagedTeamIds { get; init; } = Array.Empty<int>();

        public bool IsAdmin => string.Equals(Role, LeadRoles.Admin, StringComparison.OrdinalIgnoreCase);

        public bool IsManager => string.Equals(Role, LeadRoles.Manager, StringComparison.OrdinalIgnoreCase);

        public bool IsEmployee => string.Equals(Role, LeadRoles.Employee, StringComparison.OrdinalIgnoreCase);

        public bool IsStaff => IsAdmin || IsManager || IsEmployee;
    }

    /// <summary>
    /// The one place that decides who may see or change what. Read paths funnel every
    /// query through <see cref="Scope"/>; write paths load the lead through the same scoped
    /// query, so an out-of-scope lead is indistinguishable from one that does not exist.
    /// </summary>
    public static class LeadAccess
    {
        public static IQueryable<Lead> Scope(IQueryable<Lead> query, LeadUserContext ctx)
        {
            if (ctx.IsAdmin)
                return query;

            if (ctx.IsManager)
            {
                // Materialised to an array so EF translates Contains to a SQL IN list.
                var teamIds = ctx.ManagedTeamIds.ToArray();
                var employeeId = ctx.EmployeeId;
                return query.Where(l =>
                    l.AssignedEmployeeId == employeeId
                    || (l.AssignedTeamId != null && teamIds.Contains(l.AssignedTeamId.Value))
                    || (l.AssignedEmployee != null && l.AssignedEmployee.TeamId != null
                        && teamIds.Contains(l.AssignedEmployee.TeamId.Value))
                    // Managers own the unassigned queue — they cannot assign what they cannot see.
                    || l.AssignmentState == LeadAssignmentState.Unassigned);
            }

            if (ctx.IsEmployee)
            {
                var employeeId = ctx.EmployeeId;
                var userId = ctx.UserId;
                // Assigned to them, or explicitly shared: given a task or visit on the lead,
                // or tagged in an internal comment.
                return query.Where(l =>
                    (employeeId != null && l.AssignedEmployeeId == employeeId)
                    || (employeeId != null && l.FollowUps.Any(f => f.AssignedEmployeeId == employeeId))
                    || (employeeId != null && l.SiteVisits.Any(v => v.AssignedEmployeeId == employeeId))
                    || l.Comments.Any(c => c.Mentions.Any(m => m.MentionedUserId == userId)));
            }

            // Clients and any future non-staff role see no leads at all.
            return query.Where(_ => false);
        }

        public static void EnsureStaff(LeadUserContext ctx)
        {
            if (!ctx.IsStaff)
                throw new LeadAuthorizationException("You do not have access to the lead workspace.");
        }

        public static void EnsureCanAssign(LeadUserContext ctx)
        {
            if (!ctx.IsAdmin && !ctx.IsManager)
                throw new LeadAuthorizationException("Only an admin or manager can change lead ownership.");
        }

        public static void EnsureCanConvert(LeadUserContext ctx)
        {
            if (!ctx.IsAdmin && !ctx.IsManager)
                throw new LeadAuthorizationException("Only an admin or manager can convert a lead into a booking.");
        }

        public static void EnsureCanReopen(LeadUserContext ctx)
        {
            if (!ctx.IsAdmin && !ctx.IsManager)
                throw new LeadAuthorizationException("Only an admin or manager can reopen a closed lead.");
        }

        public static void EnsureCanConfigure(LeadUserContext ctx)
        {
            if (!ctx.IsAdmin)
                throw new LeadAuthorizationException("Only an admin can change lead configuration.");
        }
    }

    /// <summary>
    /// Raised when the caller is authenticated but not permitted. Surfaced as 403 rather
    /// than being flattened into the generic 400 that <see cref="InvalidOperationException"/>
    /// produces.
    /// </summary>
    public class LeadAuthorizationException : Exception
    {
        public LeadAuthorizationException(string message) : base(message) { }
    }

    /// <summary>Raised when a lead is missing or outside the caller's scope.</summary>
    public class LeadNotFoundException : Exception
    {
        public LeadNotFoundException(string message = "Lead not found or you do not have access to it.")
            : base(message) { }
    }
}
