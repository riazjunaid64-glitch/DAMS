using DAMS.Application.Common;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public class LeadSiteVisitService : ILeadSiteVisitService
    {
        private readonly AppDbContext _context;
        private readonly ILeadNotificationService _notifications;

        public LeadSiteVisitService(AppDbContext context, ILeadNotificationService notifications)
        {
            _context = context;
            _notifications = notifications;
        }

        public async Task<LeadSiteVisitDto> ScheduleAsync(
            int leadId, ScheduleSiteVisitDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            var lead = await LeadGate.LoadActiveAsync(_context, leadId, ctx, cancellationToken);

            if (dto.ScheduledAt <= DateTime.UtcNow)
                throw new InvalidOperationException("A site visit must be scheduled for a future time.");

            if (dto.RemindAt.HasValue && dto.RemindAt > dto.ScheduledAt)
                throw new InvalidOperationException("The reminder must come before the visit.");

            var (projectId, unitId) = await ResolveLocationAsync(dto.ProjectId, dto.UnitId, lead, cancellationToken);
            var employeeId = await LeadGate.ResolveWorkerAsync(_context, dto.AssignedEmployeeId, lead, ctx, cancellationToken);

            var visit = new LeadSiteVisit
            {
                Lead = lead,
                LeadId = lead.Id,
                ProjectId = projectId,
                UnitId = unitId,
                AssignedEmployeeId = employeeId,
                ScheduledAt = dto.ScheduledAt,
                MeetingLocation = dto.MeetingLocation.Trim(),
                CustomerAttendees = LeadContactNormalizer.Clean(dto.CustomerAttendees),
                InternalAttendees = LeadContactNormalizer.Clean(dto.InternalAttendees),
                RemindAt = dto.RemindAt,
                Notes = LeadContactNormalizer.Clean(dto.Notes),
                Status = LeadSiteVisitStatus.Scheduled,
                CreatedByUserId = ctx.UserId,
                CreatedAt = DateTime.UtcNow
            };

            _context.LeadSiteVisits.Add(visit);

            // Scheduling the visit is what moves the pipeline — the stage cannot be set to
            // SiteVisitScheduled any other way.
            var previousStage = lead.Stage;
            if (lead.Stage is not (LeadStage.SiteVisitScheduled or LeadStage.Negotiation
                or LeadStage.DocumentsInProgress or LeadStage.BookingPending))
            {
                lead.Stage = LeadStage.SiteVisitScheduled;
                LeadTimeline.Record(_context, lead, LeadActivityType.StageChanged,
                    $"Stage changed from {previousStage} to {LeadStage.SiteVisitScheduled}.", ctx,
                    a =>
                    {
                        a.PreviousValue = previousStage.ToString();
                        a.NewValue = LeadStage.SiteVisitScheduled.ToString();
                    });
            }

            lead.UpdatedAt = DateTime.UtcNow;

            var activity = LeadTimeline.Record(_context, lead, LeadActivityType.SiteVisitScheduled,
                $"Site visit scheduled for {dto.ScheduledAt:yyyy-MM-dd HH:mm} UTC.", ctx,
                a => a.Notes = visit.MeetingLocation);

            await _context.SaveChangesAsync(cancellationToken);

            activity.SiteVisitId = visit.Id;
            await NotifyEmployeeAsync(lead, employeeId, ctx, NotificationType.SiteVisitScheduled,
                $"Site visit booked for {LeadService.FullName(lead)}",
                $"{dto.ScheduledAt:yyyy-MM-dd HH:mm} UTC at {visit.MeetingLocation}.",
                $"visit:{visit.Id}", cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            await LeadGate.RefreshNextActionAsync(_context, lead.Id, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            return await LoadAsync(visit.Id, cancellationToken);
        }

        public async Task<LeadSiteVisitDto> RescheduleAsync(
            int visitId, RescheduleSiteVisitDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            var visit = await LoadForWriteAsync(visitId, ctx, cancellationToken);

            if (visit.Status is not (LeadSiteVisitStatus.Scheduled or LeadSiteVisitStatus.Rescheduled or LeadSiteVisitStatus.Missed))
                throw new InvalidOperationException($"A {visit.Status} visit cannot be rescheduled.");

            if (dto.ScheduledAt <= DateTime.UtcNow)
                throw new InvalidOperationException("A site visit must be scheduled for a future time.");

            var lead = await LeadGate.LoadActiveForAuthorizedWorkAsync(_context, visit.LeadId, cancellationToken);

            var previous = visit.ScheduledAt;
            visit.OriginalScheduledAt ??= previous;
            visit.ScheduledAt = dto.ScheduledAt;
            visit.MeetingLocation = LeadContactNormalizer.Clean(dto.MeetingLocation) ?? visit.MeetingLocation;
            visit.RemindAt = dto.RemindAt;
            visit.Status = LeadSiteVisitStatus.Rescheduled;
            visit.RescheduleCount++;
            visit.UpdatedAt = DateTime.UtcNow;

            lead.UpdatedAt = DateTime.UtcNow;

            LeadTimeline.Record(_context, lead, LeadActivityType.SiteVisitRescheduled,
                $"Site visit moved from {previous:yyyy-MM-dd HH:mm} to {dto.ScheduledAt:yyyy-MM-dd HH:mm} UTC.", ctx,
                a =>
                {
                    a.Notes = dto.Reason.Trim();
                    a.SiteVisitId = visit.Id;
                    a.PreviousValue = previous.ToString("u");
                    a.NewValue = dto.ScheduledAt.ToString("u");
                });

            await _context.SaveChangesAsync(cancellationToken);
            await LeadGate.RefreshNextActionAsync(_context, lead.Id, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            return await LoadAsync(visit.Id, cancellationToken);
        }

        public async Task<LeadSiteVisitDto> CompleteAsync(
            int visitId, CompleteSiteVisitDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            var visit = await LoadForWriteAsync(visitId, ctx, cancellationToken);

            if (visit.Status is not (LeadSiteVisitStatus.Scheduled or LeadSiteVisitStatus.Rescheduled or LeadSiteVisitStatus.Missed))
                throw new InvalidOperationException($"A {visit.Status} visit cannot be completed.");

            var lead = await LeadGate.LoadActiveForAuthorizedWorkAsync(_context, visit.LeadId, cancellationToken);

            visit.Status = LeadSiteVisitStatus.Completed;
            visit.Outcome = dto.Outcome;
            visit.OutcomeNotes = LeadContactNormalizer.Clean(dto.OutcomeNotes);
            visit.CustomerFeedback = LeadContactNormalizer.Clean(dto.CustomerFeedback);
            visit.NextAction = dto.NextAction.Trim();
            visit.CompletedAt = DateTime.UtcNow;
            visit.UpdatedAt = DateTime.UtcNow;

            // A completed visit is a real meeting: it counts as contact.
            lead.FirstContactAt ??= visit.CompletedAt;
            lead.LastContactAt = visit.CompletedAt;

            var previousStage = lead.Stage;
            if (lead.Stage is LeadStage.SiteVisitScheduled or LeadStage.Contacted
                or LeadStage.Qualified or LeadStage.New or LeadStage.FirstContactPending)
            {
                lead.Stage = LeadStage.SiteVisitCompleted;
                LeadTimeline.Record(_context, lead, LeadActivityType.StageChanged,
                    $"Stage changed from {previousStage} to {LeadStage.SiteVisitCompleted}.", ctx,
                    a =>
                    {
                        a.PreviousValue = previousStage.ToString();
                        a.NewValue = LeadStage.SiteVisitCompleted.ToString();
                    });
            }

            lead.UpdatedAt = DateTime.UtcNow;

            LeadTimeline.Record(_context, lead, LeadActivityType.SiteVisitCompleted,
                $"Site visit completed — {dto.Outcome}.", ctx,
                a =>
                {
                    a.Notes = $"Next: {visit.NextAction}";
                    a.SiteVisitId = visit.Id;
                    a.Channel = LeadCommunicationChannel.SiteVisit;
                    a.NewValue = dto.Outcome.ToString();
                });

            await _context.SaveChangesAsync(cancellationToken);
            await LeadGate.RefreshNextActionAsync(_context, lead.Id, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            return await LoadAsync(visit.Id, cancellationToken);
        }

        public Task<LeadSiteVisitDto> CancelAsync(
            int visitId, CloseSiteVisitDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default) =>
            CloseAsync(visitId, dto, LeadSiteVisitStatus.Cancelled, ctx, cancellationToken);

        public Task<LeadSiteVisitDto> MarkMissedAsync(
            int visitId, CloseSiteVisitDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default) =>
            CloseAsync(visitId, dto, LeadSiteVisitStatus.Missed, ctx, cancellationToken);

        private async Task<LeadSiteVisitDto> CloseAsync(
            int visitId, CloseSiteVisitDto dto, LeadSiteVisitStatus status, LeadUserContext ctx, CancellationToken cancellationToken)
        {
            var visit = await LoadForWriteAsync(visitId, ctx, cancellationToken);

            if (visit.Status is LeadSiteVisitStatus.Completed or LeadSiteVisitStatus.Cancelled)
                throw new InvalidOperationException($"This visit is already {visit.Status}.");

            var lead = await LeadGate.LoadActiveForAuthorizedWorkAsync(_context, visit.LeadId, cancellationToken);

            visit.Status = status;
            visit.CancellationReason = dto.Reason.Trim();
            visit.UpdatedAt = DateTime.UtcNow;
            lead.UpdatedAt = DateTime.UtcNow;

            LeadTimeline.Record(_context, lead,
                status == LeadSiteVisitStatus.Missed ? LeadActivityType.SiteVisitMissed : LeadActivityType.SiteVisitCancelled,
                status == LeadSiteVisitStatus.Missed ? "Site visit missed." : "Site visit cancelled.", ctx,
                a =>
                {
                    a.Notes = visit.CancellationReason;
                    a.SiteVisitId = visit.Id;
                });

            if (status == LeadSiteVisitStatus.Missed)
            {
                await _notifications.QueueForSupervisorsAsync(lead, NotificationType.SiteVisitMissed,
                    $"Site visit missed: {LeadService.FullName(lead)}", visit.CancellationReason,
                    $"missed:{visit.Id}", isEscalation: true, cancellationToken: cancellationToken);
            }

            await _context.SaveChangesAsync(cancellationToken);
            await LeadGate.RefreshNextActionAsync(_context, lead.Id, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            return await LoadAsync(visit.Id, cancellationToken);
        }

        public async Task<List<LeadSiteVisitDto>> GetForLeadAsync(
            int leadId, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            await LeadGate.EnsureVisibleAsync(_context, leadId, ctx, cancellationToken);

            return await _context.LeadSiteVisits
                .AsNoTracking()
                .Where(v => v.LeadId == leadId)
                .OrderByDescending(v => v.ScheduledAt)
                .Select(LeadMapping.ToSiteVisitDto)
                .ToListAsync(cancellationToken);
        }

        public async Task<List<LeadSiteVisitDto>> GetUpcomingAsync(
            LeadUserContext ctx, int days, CancellationToken cancellationToken = default)
        {
            LeadAccess.EnsureStaff(ctx);

            var now = DateTime.UtcNow;
            var until = now.AddDays(Math.Clamp(days, 1, 90));

            var query = _context.LeadSiteVisits
                .AsNoTracking()
                .Where(v => (v.Status == LeadSiteVisitStatus.Scheduled || v.Status == LeadSiteVisitStatus.Rescheduled)
                            && v.ScheduledAt >= now && v.ScheduledAt <= until);

            if (ctx.IsEmployee)
            {
                query = ctx.EmployeeId == null
                    ? query.Where(_ => false)
                    : query.Where(v => v.AssignedEmployeeId == ctx.EmployeeId);
            }
            else if (!ctx.IsAdmin)
            {
                var visibleLeadIds = LeadAccess.Scope(_context.Leads.AsNoTracking(), ctx).Select(l => l.Id);
                query = query.Where(v => visibleLeadIds.Contains(v.LeadId));
            }

            return await query
                .OrderBy(v => v.ScheduledAt)
                .Select(LeadMapping.ToSiteVisitDto)
                .ToListAsync(cancellationToken);
        }

        private async Task<(int? ProjectId, int? UnitId)> ResolveLocationAsync(
            int? projectId, int? unitId, Lead lead, CancellationToken cancellationToken)
        {
            unitId ??= lead.InterestedUnitId;
            projectId ??= lead.InterestedProjectId;

            if (unitId.HasValue)
            {
                var unit = await _context.Units
                    .AsNoTracking()
                    .Where(u => u.Id == unitId.Value)
                    .Select(u => new { u.Id, u.ProjectId })
                    .FirstOrDefaultAsync(cancellationToken)
                    ?? throw new InvalidOperationException("Unit not found.");

                projectId = unit.ProjectId;
            }
            else if (projectId.HasValue)
            {
                var exists = await _context.Projects.AnyAsync(p => p.Id == projectId.Value, cancellationToken);
                if (!exists)
                    throw new InvalidOperationException("Project not found.");
            }

            return (projectId, unitId);
        }

        private async Task NotifyEmployeeAsync(
            Lead lead, int employeeId, LeadUserContext ctx, NotificationType type,
            string title, string? body, string suffix, CancellationToken cancellationToken)
        {
            var userId = await _context.Employees
                .AsNoTracking()
                .Where(e => e.Id == employeeId)
                .Select(e => e.UserId)
                .FirstOrDefaultAsync(cancellationToken);

            if (userId == null || userId == ctx.UserId)
                return;

            await _notifications.QueueAsync(lead.Id, userId.Value, type, title, body,
                $"{type}:{lead.Id}:{userId.Value}:{suffix}", cancellationToken: cancellationToken);
        }

        private async Task<LeadSiteVisit> LoadForWriteAsync(int visitId, LeadUserContext ctx, CancellationToken cancellationToken)
        {
            LeadAccess.EnsureStaff(ctx);

            var visit = await _context.LeadSiteVisits
                .FirstOrDefaultAsync(v => v.Id == visitId, cancellationToken)
                ?? throw new InvalidOperationException("Site visit not found.");

            await LeadGate.EnsureCanWorkItemAsync(
                _context, visit.AssignedEmployeeId, visit.LeadId, ctx, cancellationToken);

            return visit;
        }

        private async Task<LeadSiteVisitDto> LoadAsync(int id, CancellationToken cancellationToken) =>
            await _context.LeadSiteVisits
                .AsNoTracking()
                .Where(v => v.Id == id)
                .Select(LeadMapping.ToSiteVisitDto)
                .FirstAsync(cancellationToken);
    }
}

