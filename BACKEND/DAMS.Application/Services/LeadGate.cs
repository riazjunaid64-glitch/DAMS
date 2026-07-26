using DAMS.Application.Common;
using DAMS.Domain.Entities;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    /// <summary>
    /// Every lead read and write goes through here. Loading is always scope-filtered, so a
    /// lead outside the caller's reach is reported exactly like one that does not exist and
    /// no endpoint can leak whether it is real.
    /// </summary>
    internal static class LeadGate
    {
        public static async Task<Lead> LoadAsync(
            AppDbContext context, int leadId, LeadUserContext user, CancellationToken cancellationToken)
        {
            LeadAccess.EnsureStaff(user);

            var lead = await LeadAccess.Scope(context.Leads, user)
                .FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken);

            return lead ?? throw new LeadNotFoundException();
        }

        /// <summary>Loads a lead that is still live; closed leads are read-only.</summary>
        public static async Task<Lead> LoadActiveAsync(
            AppDbContext context, int leadId, LeadUserContext user, CancellationToken cancellationToken)
        {
            var lead = await LoadAsync(context, leadId, user, cancellationToken);

            if (LeadStageRules.IsClosed(lead.Stage))
                throw new InvalidOperationException(
                    lead.Stage == Domain.Enums.LeadStage.Won
                        ? "This lead has been converted; its history is read-only."
                        : $"This lead is {lead.Stage}. Reopen it before adding new activity.");

            return lead;
        }

        public static async Task EnsureVisibleAsync(
            AppDbContext context, int leadId, LeadUserContext user, CancellationToken cancellationToken)
        {
            LeadAccess.EnsureStaff(user);

            var visible = await LeadAccess.Scope(context.Leads.AsNoTracking(), user)
                .AnyAsync(l => l.Id == leadId, cancellationToken);

            if (!visible)
                throw new LeadNotFoundException();
        }

        /// <summary>
        /// Picks the employee a piece of work belongs to: the one asked for, otherwise the
        /// lead's owner, otherwise the caller.
        /// </summary>
        public static async Task<int> ResolveWorkerAsync(
            AppDbContext context, int? requested, Lead lead, LeadUserContext user, CancellationToken cancellationToken)
        {
            var employeeId = requested ?? lead.AssignedEmployeeId ?? user.EmployeeId;

            if (employeeId == null)
                throw new InvalidOperationException(
                    "Assign this lead to an employee first, or name the employee responsible.");

            // Employees may only route work to themselves or the lead's owner; handing work
            // to an arbitrary colleague is a manager's decision.
            if (user.IsEmployee && employeeId != user.EmployeeId && employeeId != lead.AssignedEmployeeId)
                throw new LeadAuthorizationException("You can only take on work yourself or leave it with the lead's owner.");

            var employee = await context.Employees
                .AsNoTracking()
                .Where(e => e.Id == employeeId)
                .Select(e => new { e.Id, e.Status })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("Employee not found.");

            if (employee.Status != Domain.Enums.EmployeeStatus.Active)
                throw new InvalidOperationException("That employee is not active.");

            return employee.Id;
        }
    }
}
