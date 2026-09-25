using DAMS.Application.Common;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
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
            EnsureActive(lead);
            return lead;
        }

        public static async Task<Lead> LoadActiveForAuthorizedWorkAsync(
            AppDbContext context, int leadId, CancellationToken cancellationToken)
        {
            var lead = await context.Leads
                .FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken)
                ?? throw new LeadNotFoundException();

            EnsureActive(lead);
            return lead;
        }

        private static void EnsureActive(Lead lead)
        {
            if (LeadStageRules.IsClosed(lead.Stage))
                throw new InvalidOperationException(
                    lead.Stage == LeadStage.Won
                        ? "This lead has been converted; its history is read-only."
                        : $"This lead is {lead.Stage}. Reopen it before adding new activity.");
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
                .Select(e => new { e.Id, e.Status, e.TeamId })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("Employee not found.");

            if (employee.Status != EmployeeStatus.Active)
                throw new InvalidOperationException("That employee is not active.");

            if (user.IsManager && employee.Id != user.EmployeeId
                && (employee.TeamId == null || !user.ManagedTeamIds.Contains(employee.TeamId.Value)))
                throw new LeadAuthorizationException("You can only assign work within your managed team.");

            return employee.Id;
        }

        public static async Task EnsureEmployeeCanBeMentionedAsync(
            AppDbContext context, int mentionedUserId, Lead lead, LeadUserContext actor, CancellationToken cancellationToken)
        {
            var mentioned = await context.Employees
                .AsNoTracking()
                .Where(e => e.UserId == mentionedUserId)
                .Select(e => new { e.Id, e.Status, e.TeamId })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("You can only mention active colleagues who work on leads.");

            if (mentioned.Status != EmployeeStatus.Active)
                throw new InvalidOperationException("You can only mention active colleagues who work on leads.");

            if (actor.IsAdmin)
                return;

            if (actor.IsManager)
            {
                if (mentioned.Id == actor.EmployeeId ||
                    (mentioned.TeamId.HasValue && actor.ManagedTeamIds.Contains(mentioned.TeamId.Value)))
                    return;

                throw new LeadAuthorizationException("You can only mention people in your managed team.");
            }

            if (actor.IsEmployee)
            {
                if (mentioned.Id == actor.EmployeeId ||
                    (actor.TeamId.HasValue && mentioned.TeamId == actor.TeamId) ||
                    (lead.AssignedEmployeeId.HasValue && mentioned.Id == lead.AssignedEmployeeId.Value))
                    return;

                throw new LeadAuthorizationException("You can only mention people on your team or the lead owner.");
            }

            throw new LeadAuthorizationException("You do not have access to the lead workspace.");
        }

        public static async Task EnsureCanWorkItemAsync(
            AppDbContext context, int assignedEmployeeId, int leadId, LeadUserContext actor, CancellationToken cancellationToken)
        {
            LeadAccess.EnsureStaff(actor);

            if (actor.IsAdmin)
                return;

            if (actor.IsEmployee)
            {
                if (actor.EmployeeId == assignedEmployeeId)
                    return;

                var ownsLead = await context.Leads
                    .AsNoTracking()
                    .AnyAsync(l => l.Id == leadId && l.AssignedEmployeeId == actor.EmployeeId, cancellationToken);
                if (ownsLead)
                    return;

                throw new LeadAuthorizationException("You can only update work assigned to you or to a lead you own.");
            }

            if (actor.IsManager)
            {
                var employee = await context.Employees
                    .AsNoTracking()
                    .Where(e => e.Id == assignedEmployeeId)
                    .Select(e => new { e.Id, e.TeamId })
                    .FirstOrDefaultAsync(cancellationToken);

                if (employee != null &&
                    (employee.Id == actor.EmployeeId ||
                     (employee.TeamId.HasValue && actor.ManagedTeamIds.Contains(employee.TeamId.Value))))
                    return;

                throw new LeadAuthorizationException("You can only update work assigned inside your managed team.");
            }
        }

        public static async Task RefreshNextActionAsync(
            AppDbContext context, int leadId, CancellationToken cancellationToken)
        {
            var followUp = await context.LeadFollowUps
                .AsNoTracking()
                .Where(f => f.LeadId == leadId
                            && (f.Status == LeadFollowUpStatus.Pending || f.Status == LeadFollowUpStatus.Missed))
                .OrderBy(f => f.DueAt)
                .Select(f => new { At = (DateTime?)f.DueAt, Summary = f.Title })
                .FirstOrDefaultAsync(cancellationToken);

            var visit = await context.LeadSiteVisits
                .AsNoTracking()
                .Where(v => v.LeadId == leadId
                            && (v.Status == LeadSiteVisitStatus.Scheduled
                                || v.Status == LeadSiteVisitStatus.Rescheduled
                                || v.Status == LeadSiteVisitStatus.Missed))
                .OrderBy(v => v.ScheduledAt)
                .Select(v => new { At = (DateTime?)v.ScheduledAt, Summary = "Site visit at " + v.MeetingLocation })
                .FirstOrDefaultAsync(cancellationToken);

            // Only the latest communication's plan is outstanding — any later exchange with the
            // customer has either carried it out or replaced it.
            var communication = await context.LeadCommunications
                .AsNoTracking()
                .Where(c => c.LeadId == leadId)
                .OrderByDescending(c => c.OccurredAt)
                .ThenByDescending(c => c.Id)
                .Select(c => new { At = c.NextActionAt, Summary = c.NextAction ?? c.Summary })
                .FirstOrDefaultAsync(cancellationToken);

            var next = new[] { followUp, visit, communication }
                .Where(x => x?.At != null)
                .OrderBy(x => x!.At)
                .FirstOrDefault();

            var lead = await context.Leads.FirstAsync(l => l.Id == leadId, cancellationToken);
            lead.NextActionAt = next?.At;
            lead.NextActionSummary = next == null ? null : LeadContactNormalizer.Limit(next.Summary, 300);
            lead.UpdatedAt = DateTime.UtcNow;
        }
    }
}
