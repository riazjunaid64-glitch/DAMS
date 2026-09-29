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
                .Select(e => new { e.Id, e.Status })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("Employee not found.");

            if (employee.Status != EmployeeStatus.Active)
                throw new InvalidOperationException("That employee is not active.");

            return employee.Id;
        }

        public static async Task EnsureEmployeeCanBeMentionedAsync(
            AppDbContext context, int mentionedUserId, Lead lead, LeadUserContext actor, CancellationToken cancellationToken)
        {
            var mentioned = await context.Employees
                .AsNoTracking()
                .Where(e => e.UserId == mentionedUserId)
                .Select(e => new { e.Id, e.Status })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("You can only mention active colleagues who work on leads.");

            if (mentioned.Status != EmployeeStatus.Active)
                throw new InvalidOperationException("You can only mention active colleagues who work on leads.");

            if (actor.IsAdmin || actor.IsManager || actor.IsEmployee)
                return;

            throw new LeadAuthorizationException("You do not have access to the lead workspace.");
        }

        public static async Task EnsureCanWorkItemAsync(
            AppDbContext context, int assignedEmployeeId, int leadId, LeadUserContext actor, CancellationToken cancellationToken)
        {
            LeadAccess.EnsureStaff(actor);

            if (actor.IsAdmin || actor.IsManager)
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
        }

        /// <summary>
        /// Runs one lead action as a single commit. SQL Server retries transient errors, and a
        /// retry must not replay rows a failed attempt already marked as stored, so the tracker
        /// is cleared only when this method owns the transaction. A caller that already has one
        /// open — recording a call that also schedules the follow-up — is joined rather than nested.
        /// The in-memory store used by tests is not relational and runs the action directly.
        /// </summary>
        public static async Task<T> RunAtomicallyAsync<T>(
            AppDbContext context, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
        {
            if (!context.Database.IsRelational() || context.Database.CurrentTransaction != null)
                return await action(cancellationToken);

            var attempt = 0;
            return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                if (attempt++ > 0)
                    context.ChangeTracker.Clear();

                await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
                var result = await action(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            });
        }

        public static async Task RefreshNextActionAsync(
            AppDbContext context, int leadId, CancellationToken cancellationToken)
        {
            // Rows staged in this context but not saved yet still count. Callers can refresh
            // before the one save that writes them, instead of saving first and then writing
            // the lead again — the gap another user can win, leaving the follow-up committed
            // and the lead update failed.
            var followUp = await EarliestOpenFollowUpAsync(context, leadId, cancellationToken);
            var visit = await EarliestOpenVisitAsync(context, leadId, cancellationToken);

            // Plans made before the lead was closed died with it; a reopen starts clean.
            // Compare the recording time: a closed lead cannot receive communications, so anything
            // recorded after the reopen belongs to it, even if it is logged with an earlier OccurredAt.
            var reopenedAt = await context.LeadActivities
                .AsNoTracking()
                .Where(a => a.LeadId == leadId && a.Type == LeadActivityType.LeadReopened)
                .MaxAsync(a => (DateTime?)a.OccurredAt, cancellationToken);

            // Only the latest real exchange's plan is outstanding. A later conversation has either
            // carried the plan out or replaced it; an unanswered attempt does neither unless it
            // sets a plan of its own.
            var communication = await LatestExchangeAsync(context, leadId, reopenedAt, cancellationToken);

            // Stable order: a follow-up and a visit at the same moment keep the follow-up, matching
            // the previous array order. A latest exchange with no date of its own contributes nothing.
            var next = new NextStep?[] { followUp, visit, communication }
                .Where(x => x?.At != null)
                .OrderBy(x => x!.At)
                .FirstOrDefault();

            var lead = await context.Leads.FirstAsync(l => l.Id == leadId, cancellationToken);
            lead.NextActionAt = next?.At;
            lead.NextActionSummary = next == null ? null : LeadContactNormalizer.Limit(next.Summary, 300);
            lead.UpdatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Writes the staged follow-up, visit or communication together with the lead's next
        /// action, then the links that need the database-generated id (timeline foreign key,
        /// notification dedup key). Both saves share the caller's transaction.
        /// </summary>
        public static async Task SaveThenLinkAsync(
            AppDbContext context, int leadId, Func<CancellationToken, Task> linkAsync, CancellationToken cancellationToken)
        {
            await RefreshNextActionAsync(context, leadId, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await linkAsync(cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }

        private sealed record NextStep(DateTime? At, string Summary);

        // A class, not a struct: FirstOrDefaultAsync must be able to return null when the lead
        // has no exchange yet. A default struct would look like a real row dated year 1.
        private sealed record Exchange(DateTime OccurredAt, int SortId, DateTime CreatedAt, DateTime? At, string Summary);

        private static List<int> ShadowedIds<TEntity>(AppDbContext context, int leadId, Func<TEntity, int> leadIdOf, Func<TEntity, int> idOf)
            where TEntity : class =>
            context.ChangeTracker.Entries<TEntity>()
                .Where(e => leadIdOf(e.Entity) == leadId
                            && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                            && idOf(e.Entity) > 0)
                .Select(e => idOf(e.Entity))
                .Distinct()
                .ToList();

        private static async Task<NextStep?> EarliestOpenFollowUpAsync(
            AppDbContext context, int leadId, CancellationToken cancellationToken)
        {
            var shadowed = ShadowedIds<LeadFollowUp>(context, leadId, f => f.LeadId, f => f.Id);
            var storedQuery = context.LeadFollowUps.AsNoTracking()
                .Where(f => f.LeadId == leadId
                            && (f.Status == LeadFollowUpStatus.Pending || f.Status == LeadFollowUpStatus.Missed));
            if (shadowed.Count > 0)
                storedQuery = storedQuery.Where(f => !shadowed.Contains(f.Id));

            var stored = await storedQuery
                .OrderBy(f => f.DueAt)
                .Select(f => new NextStep((DateTime?)f.DueAt, f.Title))
                .FirstOrDefaultAsync(cancellationToken);

            var local = context.ChangeTracker.Entries<LeadFollowUp>()
                .Where(e => e.Entity.LeadId == leadId && e.State is EntityState.Added or EntityState.Modified)
                .Select(e => e.Entity)
                .Where(f => f.Status is LeadFollowUpStatus.Pending or LeadFollowUpStatus.Missed)
                .Select(f => new NextStep(f.DueAt, f.Title));

            return Earliest(stored, local);
        }

        private static async Task<NextStep?> EarliestOpenVisitAsync(
            AppDbContext context, int leadId, CancellationToken cancellationToken)
        {
            var shadowed = ShadowedIds<LeadSiteVisit>(context, leadId, v => v.LeadId, v => v.Id);
            var storedQuery = context.LeadSiteVisits.AsNoTracking()
                .Where(v => v.LeadId == leadId
                            && (v.Status == LeadSiteVisitStatus.Scheduled
                                || v.Status == LeadSiteVisitStatus.Rescheduled
                                || v.Status == LeadSiteVisitStatus.Missed));
            if (shadowed.Count > 0)
                storedQuery = storedQuery.Where(v => !shadowed.Contains(v.Id));

            var stored = await storedQuery
                .OrderBy(v => v.ScheduledAt)
                .Select(v => new NextStep((DateTime?)v.ScheduledAt, "Site visit at " + v.MeetingLocation))
                .FirstOrDefaultAsync(cancellationToken);

            var local = context.ChangeTracker.Entries<LeadSiteVisit>()
                .Where(e => e.Entity.LeadId == leadId && e.State is EntityState.Added or EntityState.Modified)
                .Select(e => e.Entity)
                .Where(v => v.Status is LeadSiteVisitStatus.Scheduled or LeadSiteVisitStatus.Rescheduled or LeadSiteVisitStatus.Missed)
                .Select(v => new NextStep(v.ScheduledAt, "Site visit at " + v.MeetingLocation));

            return Earliest(stored, local);
        }

        private static NextStep? Earliest(NextStep? stored, IEnumerable<NextStep> local)
        {
            var steps = local.ToList();
            if (stored != null)
                steps.Add(stored);
            return steps.OrderBy(s => s.At).FirstOrDefault();
        }

        private static async Task<NextStep?> LatestExchangeAsync(
            AppDbContext context, int leadId, DateTime? reopenedAt, CancellationToken cancellationToken)
        {
            var shadowed = ShadowedIds<LeadCommunication>(context, leadId, c => c.LeadId, c => c.Id);
            var storedQuery = context.LeadCommunications.AsNoTracking()
                .Where(c => c.LeadId == leadId
                            && (c.Connected || c.NextActionAt != null)
                            && (reopenedAt == null || c.CreatedAt >= reopenedAt));
            if (shadowed.Count > 0)
                storedQuery = storedQuery.Where(c => !shadowed.Contains(c.Id));

            var stored = await storedQuery
                .OrderByDescending(c => c.OccurredAt)
                .ThenByDescending(c => c.Id)
                .Select(c => new Exchange(
                    c.OccurredAt,
                    c.Id,
                    c.CreatedAt,
                    // A follow-up linked to this exchange already carries the plan. Counting the
                    // communication as well would leave it Overdue after that follow-up is done.
                    c.FollowUpId == null ? c.NextActionAt : null,
                    c.NextAction ?? c.Summary))
                .FirstOrDefaultAsync(cancellationToken);

            // An unsaved row has no id yet. It sorts after every saved row that shares its
            // OccurredAt, which is where it will land once the database assigns the id.
            var local = context.ChangeTracker.Entries<LeadCommunication>()
                .Where(e => e.Entity.LeadId == leadId && e.State is EntityState.Added or EntityState.Modified)
                .Select(e => e.Entity)
                .Where(c => (c.Connected || c.NextActionAt != null)
                            && (reopenedAt == null || c.CreatedAt >= reopenedAt))
                .Select(c => new Exchange(
                    c.OccurredAt,
                    c.Id > 0 ? c.Id : int.MaxValue,
                    c.CreatedAt,
                    c.FollowUpId == null && c.FollowUp == null ? c.NextActionAt : null,
                    c.NextAction ?? c.Summary));

            var exchanges = local.ToList();
            if (stored != null)
                exchanges.Add(stored);

            if (exchanges.Count == 0)
                return null;

            var latest = exchanges
                .OrderByDescending(e => e.OccurredAt)
                .ThenByDescending(e => e.SortId)
                .ThenByDescending(e => e.CreatedAt)
                .First();

            return new NextStep(latest.At, latest.Summary);
        }
    }
}
