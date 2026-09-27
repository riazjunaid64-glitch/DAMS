using DAMS.Application.Common;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public class LeadFollowUpService : ILeadFollowUpService
    {
        private readonly AppDbContext _context;
        private readonly ILeadNotificationService _notifications;

        public LeadFollowUpService(AppDbContext context, ILeadNotificationService notifications)
        {
            _context = context;
            _notifications = notifications;
        }

        /// <summary>
        /// The one place a follow-up row is made. Callers acting for a person validate the request
        /// and record it on the timeline themselves; the system (a Dormant lead brought back on its
        /// date) passes no creator and writes its own timeline entry.
        /// </summary>
        internal static LeadFollowUp Schedule(
            AppDbContext context, Lead lead, LeadFollowUpType type, int assignedEmployeeId, string title, DateTime dueAt,
            TaskPriority priority, int? createdByUserId, DateTime createdAt, string? notes = null, DateTime? remindAt = null)
        {
            var followUp = new LeadFollowUp
            {
                Lead = lead,
                LeadId = lead.Id,
                Type = type,
                AssignedEmployeeId = assignedEmployeeId,
                Title = title,
                Notes = notes,
                DueAt = dueAt,
                RemindAt = remindAt,
                Priority = priority,
                Status = LeadFollowUpStatus.Pending,
                CreatedByUserId = createdByUserId,
                CreatedAt = createdAt
            };

            context.LeadFollowUps.Add(followUp);
            return followUp;
        }

        public async Task<LeadFollowUpDto> CreateAsync(
            int leadId, CreateLeadFollowUpDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            var lead = await LeadGate.LoadActiveAsync(_context, leadId, ctx, cancellationToken);

            if (dto.DueAt <= DateTime.UtcNow.AddMinutes(-1))
                throw new InvalidOperationException("A follow-up must be scheduled for a future time.");

            if (dto.RemindAt.HasValue && dto.RemindAt > dto.DueAt)
                throw new InvalidOperationException("The reminder must come before the follow-up is due.");

            var employeeId = await LeadGate.ResolveWorkerAsync(_context, dto.AssignedEmployeeId, lead, ctx, cancellationToken);

            var followUp = Schedule(_context, lead, dto.Type, employeeId, dto.Title.Trim(), dto.DueAt, dto.Priority,
                ctx.UserId, DateTime.UtcNow, LeadContactNormalizer.Clean(dto.Notes), dto.RemindAt);

            lead.UpdatedAt = DateTime.UtcNow;

            var activity = LeadTimeline.Record(_context, lead,
                dto.AssignedEmployeeId != null && dto.AssignedEmployeeId != lead.AssignedEmployeeId
                    ? LeadActivityType.TaskCreated
                    : LeadActivityType.FollowUpScheduled,
                $"{LeadDisplay.Words(dto.Type)} scheduled for {LeadDisplay.When(dto.DueAt)}.", ctx,
                a => a.Notes = followUp.Title);

            await _context.SaveChangesAsync(cancellationToken);
            await LeadGate.RefreshNextActionAsync(_context, lead.Id, cancellationToken);

            activity.FollowUpId = followUp.Id;

            var ownerUserId = await GetEmployeeUserIdAsync(employeeId, cancellationToken);
            if (ownerUserId.HasValue && ownerUserId != ctx.UserId)
            {
                await _notifications.QueueAsync(lead.Id, ownerUserId.Value, NotificationType.FollowUpAssigned,
                    $"New {LeadDisplay.Words(dto.Type)} on {LeadService.FullName(lead)}",
                    $"{followUp.Title} — due {LeadDisplay.When(dto.DueAt)}.",
                    $"task:{followUp.Id}:{ownerUserId.Value}", cancellationToken: cancellationToken);
            }

            await _context.SaveChangesAsync(cancellationToken);

            return await LoadAsync(followUp.Id, cancellationToken);
        }

        public async Task<LeadFollowUpDto> CompleteAsync(
            int followUpId, CompleteLeadFollowUpDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            var followUp = await LoadForWriteAsync(followUpId, ctx, cancellationToken);

            if (followUp.Status != LeadFollowUpStatus.Pending && followUp.Status != LeadFollowUpStatus.Missed)
                throw new InvalidOperationException($"This follow-up is already {followUp.Status}.");

            var lead = await LeadGate.LoadActiveForAuthorizedWorkAsync(_context, followUp.LeadId, cancellationToken);

            followUp.Status = LeadFollowUpStatus.Completed;
            followUp.CompletedAt = DateTime.UtcNow;
            followUp.CompletedByUserId = ctx.UserId;
            followUp.Outcome = LeadContactNormalizer.Clean(dto.Outcome);
            followUp.UpdatedAt = DateTime.UtcNow;

            LeadFollowUp? next = null;
            if (dto.NextFollowUpAt.HasValue)
            {
                if (dto.NextFollowUpAt <= DateTime.UtcNow)
                    throw new InvalidOperationException("The next follow-up must be scheduled for a future time.");

                next = Schedule(_context, lead, followUp.Type, followUp.AssignedEmployeeId,
                    LeadContactNormalizer.Clean(dto.NextFollowUpTitle) ?? followUp.Title, dto.NextFollowUpAt.Value,
                    followUp.Priority, ctx.UserId, DateTime.UtcNow);
            }

            LeadTimeline.Record(_context, lead, LeadActivityType.FollowUpCompleted,
                $"{LeadDisplay.Words(followUp.Type)} completed.", ctx, a =>
                {
                    a.Notes = followUp.Outcome;
                    a.FollowUpId = followUp.Id;
                });

            if (next != null)
                LeadTimeline.Record(_context, lead, LeadActivityType.FollowUpScheduled,
                    $"Next {LeadDisplay.Words(next.Type)} scheduled for {LeadDisplay.When(next.DueAt)}.", ctx,
                    a => a.Notes = next.Title);

            await _context.SaveChangesAsync(cancellationToken);
            await LeadGate.RefreshNextActionAsync(_context, lead.Id, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            return await LoadAsync(followUp.Id, cancellationToken);
        }

        public async Task<LeadFollowUpDto> CancelAsync(
            int followUpId, string? reason, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            var followUp = await LoadForWriteAsync(followUpId, ctx, cancellationToken);

            if (followUp.Status is LeadFollowUpStatus.Completed or LeadFollowUpStatus.Cancelled)
                throw new InvalidOperationException($"This follow-up is already {followUp.Status}.");

            var lead = await LeadGate.LoadActiveForAuthorizedWorkAsync(_context, followUp.LeadId, cancellationToken);

            followUp.Status = LeadFollowUpStatus.Cancelled;
            followUp.Outcome = LeadContactNormalizer.Clean(reason);
            followUp.UpdatedAt = DateTime.UtcNow;

            LeadTimeline.Record(_context, lead, LeadActivityType.FollowUpCompleted,
                $"{LeadDisplay.Words(followUp.Type)} cancelled.", ctx, a =>
                {
                    a.Notes = followUp.Outcome;
                    a.FollowUpId = followUp.Id;
                });

            await _context.SaveChangesAsync(cancellationToken);
            await LeadGate.RefreshNextActionAsync(_context, lead.Id, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            return await LoadAsync(followUp.Id, cancellationToken);
        }

        public async Task<LeadFollowUpDto> RescheduleAsync(
            int followUpId,
            RescheduleLeadFollowUpDto dto,
            LeadUserContext ctx,
            CancellationToken cancellationToken = default)
        {
            if (dto.DueAt <= DateTime.UtcNow.AddMinutes(-1))
                throw new InvalidOperationException("A follow-up must be rescheduled for a future time.");

            if (dto.RemindAt.HasValue && dto.RemindAt > dto.DueAt)
                throw new InvalidOperationException("The reminder must come before the follow-up is due.");

            var followUp = await LoadForWriteAsync(followUpId, ctx, cancellationToken);
            if (followUp.Status is not (LeadFollowUpStatus.Pending or LeadFollowUpStatus.Missed))
                throw new InvalidOperationException($"This follow-up is already {followUp.Status}.");

            var lead = await LeadGate.LoadActiveForAuthorizedWorkAsync(_context, followUp.LeadId, cancellationToken);
            var previousDueAt = followUp.DueAt;

            followUp.DueAt = dto.DueAt;
            followUp.RemindAt = dto.RemindAt;
            followUp.Status = LeadFollowUpStatus.Pending;
            followUp.RescheduleCount++;
            followUp.UpdatedAt = DateTime.UtcNow;

            LeadTimeline.Record(_context, lead, LeadActivityType.FollowUpRescheduled,
                $"{LeadDisplay.Words(followUp.Type)} rescheduled for {LeadDisplay.When(dto.DueAt)}.", ctx,
                activity =>
                {
                    activity.Notes = LeadContactNormalizer.Clean(dto.Reason);
                    activity.PreviousValue = previousDueAt.ToString("O");
                    activity.NewValue = dto.DueAt.ToString("O");
                    activity.FollowUpId = followUp.Id;
                });

            await _context.SaveChangesAsync(cancellationToken);
            await LeadGate.RefreshNextActionAsync(_context, lead.Id, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            return await LoadAsync(followUp.Id, cancellationToken);
        }

        public async Task<List<LeadFollowUpDto>> GetForLeadAsync(
            int leadId, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            await LeadGate.EnsureVisibleAsync(_context, leadId, ctx, cancellationToken);

            return await _context.LeadFollowUps
                .AsNoTracking()
                .Where(f => f.LeadId == leadId)
                .OrderBy(f => f.Status == LeadFollowUpStatus.Pending ? 0 : 1)
                .ThenBy(f => f.DueAt)
                .Select(LeadMapping.ToFollowUpDto)
                .ToListAsync(cancellationToken);
        }

        public async Task<List<LeadFollowUpDto>> GetMyFollowUpsAsync(
            LeadUserContext ctx, bool overdueOnly, CancellationToken cancellationToken = default)
        {
            LeadAccess.EnsureStaff(ctx);

            if (ctx.EmployeeId == null)
                return new List<LeadFollowUpDto>();

            var now = DateTime.UtcNow;
            var query = _context.LeadFollowUps
                .AsNoTracking()
                .Where(f => f.AssignedEmployeeId == ctx.EmployeeId
                            && (f.Status == LeadFollowUpStatus.Pending || f.Status == LeadFollowUpStatus.Missed));

            if (overdueOnly)
                query = query.Where(f => f.DueAt < now);

            return await query
                .OrderBy(f => f.DueAt)
                .Select(LeadMapping.ToFollowUpDto)
                .ToListAsync(cancellationToken);
        }

        private async Task<LeadFollowUp> LoadForWriteAsync(int followUpId, LeadUserContext ctx, CancellationToken cancellationToken)
        {
            LeadAccess.EnsureStaff(ctx);

            var followUp = await _context.LeadFollowUps
                .FirstOrDefaultAsync(f => f.Id == followUpId, cancellationToken)
                ?? throw new InvalidOperationException("Follow-up not found.");

            await LeadGate.EnsureCanWorkItemAsync(
                _context, followUp.AssignedEmployeeId, followUp.LeadId, ctx, cancellationToken);

            return followUp;
        }

        private async Task<int?> GetEmployeeUserIdAsync(int employeeId, CancellationToken cancellationToken) =>
            await _context.Employees
                .AsNoTracking()
                .Where(e => e.Id == employeeId)
                .Select(e => e.UserId)
                .FirstOrDefaultAsync(cancellationToken);

        private async Task<LeadFollowUpDto> LoadAsync(int id, CancellationToken cancellationToken) =>
            await _context.LeadFollowUps
                .AsNoTracking()
                .Where(f => f.Id == id)
                .Select(LeadMapping.ToFollowUpDto)
                .FirstAsync(cancellationToken);
    }
}
