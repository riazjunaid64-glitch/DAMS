using DAMS.Application.DTOs.LeadDtos;
using DAMS.Domain.Entities;

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
            // Admins and Sales Managers run the same CRM: every lead, including the unassigned
            // queue, so either can pick up a new enquiry and hand it to any salesperson.
            if (ctx.IsAdmin || ctx.IsManager)
                return query;

            if (ctx.IsEmployee)
            {
                var employeeId = ctx.EmployeeId;
                // Employees see leads they own. Assigned tasks/visits and mentions can notify
                // them or let them complete that one work item, but they do not silently grant
                // full lead-record access across teams.
                return query.Where(l => employeeId != null && l.AssignedEmployeeId == employeeId);
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

        /// <summary>
        /// Admin and Manager can convert any lead. An employee can convert only a lead
        /// assigned to them. Callers that have no lead yet (customer search) pass null and
        /// stay limited to admin and manager.
        /// </summary>
        public static void EnsureCanConvert(LeadUserContext ctx, Lead? lead = null)
        {
            if (ctx.IsAdmin || ctx.IsManager)
                return;

            if (ctx.IsEmployee
                && ctx.EmployeeId != null
                && lead != null
                && lead.AssignedEmployeeId == ctx.EmployeeId)
                return;

            throw new LeadAuthorizationException(
                ctx.IsEmployee
                    ? "You can only convert leads assigned to you."
                    : "Only an admin or manager can convert a lead into a booking.");
        }

        /// <summary>
        /// Price, discount, booking amount and the choice of an existing customer are
        /// admin and manager decisions. A salesperson converting their own lead gets the
        /// unit's standard terms and a customer matched or created from the lead, so any of
        /// these sent by an employee is refused rather than quietly applied or dropped.
        /// </summary>
        public static void EnsureCanSetConversionTerms(LeadUserContext ctx, ConvertLeadDto dto)
        {
            if (!ctx.IsEmployee)
                return;

            if (dto.CustomerId.HasValue
                || dto.AgreedSalePrice.HasValue
                || dto.DiscountPercent.HasValue
                || !string.IsNullOrWhiteSpace(dto.DiscountReason)
                || dto.BookingAmountRequired.HasValue
                || dto.BookingAmountDueDate.HasValue)
                throw new LeadAuthorizationException(
                    "Only an admin or manager can set the price, discount, booking amount or customer when converting a lead.");
        }

        /// <summary>
        /// Admin and Manager can reopen any closed lead. An employee can reopen only a
        /// lead assigned to them; it stays assigned to them.
        /// </summary>
        public static void EnsureCanReopen(LeadUserContext ctx, Lead? lead = null)
        {
            if (ctx.IsAdmin || ctx.IsManager)
                return;

            if (ctx.IsEmployee
                && ctx.EmployeeId != null
                && lead != null
                && lead.AssignedEmployeeId == ctx.EmployeeId)
                return;

            throw new LeadAuthorizationException(
                ctx.IsEmployee
                    ? "You can only reopen leads assigned to you."
                    : "Only an admin or manager can reopen a closed lead.");
        }

        public static void EnsureCanConfigure(LeadUserContext ctx)
        {
            if (!ctx.IsAdmin && !ctx.IsManager)
                throw new LeadAuthorizationException("Only an admin or manager can change lead configuration.");
        }

        /// <summary>
        /// A held enquiry names leads from any team, so deciding it needs a role that can
        /// see them all.
        /// </summary>
        public static void EnsureCanResolveIntakeHolds(LeadUserContext ctx)
        {
            if (!ctx.IsAdmin && !ctx.IsManager)
                throw new LeadAuthorizationException("Only an admin or manager can review held enquiries.");
        }

        /// <summary>
        /// The original provider payload carries the enquirer's personal details and every answer
        /// verbatim, so it goes no further than the roles that already manage leads. Which leads
        /// is still decided by <see cref="Scope"/>.
        /// </summary>
        public static void EnsureCanViewRawIntegrationData(LeadUserContext ctx)
        {
            if (!ctx.IsAdmin && !ctx.IsManager)
                throw new LeadAuthorizationException("Only an admin or manager can view the original provider data.");
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

    /// <summary>Raised when a staff employee record does not exist. Surfaced as 404.</summary>
    public class StaffNotFoundException : Exception
    {
        public StaffNotFoundException(string message) : base(message) { }
    }

    /// <summary>Raised when a lead is missing or outside the caller's scope.</summary>
    public class LeadNotFoundException : Exception
    {
        public LeadNotFoundException(string message = "Lead not found or you do not have access to it.")
            : base(message) { }
    }
}
