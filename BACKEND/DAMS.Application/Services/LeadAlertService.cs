using DAMS.Application.Common;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace DAMS.Application.Services
{
    /// <summary>
    /// The time-based half of lead management: nothing here reacts to a user action, it all
    /// reacts to time passing. Safe to run as often as you like — every alert carries a
    /// dedup key and every state change is guarded, so a scan that runs twice does nothing
    /// the second time.
    /// </summary>
    public class LeadAlertService : ILeadAlertService
    {
        private readonly AppDbContext _context;
        private readonly ILeadNotificationService _notifications;
        private readonly LeadAlertOptions _options;
        private readonly TimeProvider _clock;

        public LeadAlertService(
            AppDbContext context,
            ILeadNotificationService notifications,
            IOptions<LeadAlertOptions> options,
            TimeProvider clock)
        {
            _context = context;
            _notifications = notifications;
            _options = options.Value;
            _clock = clock;
        }

        public async Task<LeadAlertScanResultDto> RunScanAsync(CancellationToken cancellationToken = default)
        {
            var result = new LeadAlertScanResultDto();
            // Every rule below is relative to one instant, so a scan cannot half-apply
            // across a midnight boundary.
            var now = _clock.GetUtcNow().UtcDateTime;
            _checks.Clear();

            await ScanFirstContactAsync(result, now, cancellationToken);
            await ScanFollowUpsAsync(result, now, cancellationToken);
            await ScanInactiveLeadsAsync(result, now, cancellationToken);
            await ScanSiteVisitsAsync(result, now, cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);

            return result;
        }

        private async Task ScanFirstContactAsync(LeadAlertScanResultDto result, DateTime now, CancellationToken cancellationToken)
        {
            var cutoff = now.AddHours(-_options.FirstResponseHours);

            // Only leads that are genuinely still waiting on a first conversation. A lead
            // that has visibly moved on (a booked visit, a negotiation) is not chased, even
            // if nobody logged the call that got it there. One already evaluated for its
            // current assignment is skipped, so the batch always moves on to leads not yet seen.
            var overdue = await _context.Leads
                .Where(l => l.FirstContactAt == null
                            && l.AssignedEmployeeId != null
                            && l.AssignedAt != null && l.AssignedAt < cutoff
                            && (l.Stage == LeadStage.New || l.Stage == LeadStage.FirstContactPending)
                            && !_context.LeadAlertChecks.Any(c => c.LeadId == l.Id && c.FirstContactAssignmentCheckedAt == l.AssignedAt))
                .OrderBy(l => l.AssignedAt)
                .Take(_options.MaxRowsPerScan)
                .ToListAsync(cancellationToken);

            await LoadChecksAsync(overdue, cancellationToken);

            foreach (var lead in overdue)
            {
                _checks[lead.Id].FirstContactAssignmentCheckedAt = lead.AssignedAt;
                result.FirstContactOverdue++;

                var name = Name(lead);
                var ownerUserId = await OwnerUserIdAsync(lead, cancellationToken);
                if (ownerUserId.HasValue &&
                    await _notifications.QueueAsync(lead.Id, ownerUserId.Value,
                        NotificationType.FirstContactOverdue,
                        $"First contact overdue: {name}",
                        $"Assigned {lead.AssignedAt:yyyy-MM-dd HH:mm} UTC with no contact recorded yet.",
                        $"FirstContactOverdue:{lead.Id}:{ownerUserId.Value}", cancellationToken: cancellationToken))
                    result.NotificationsCreated++;

                // The manager only hears about it once — the dedup key has no time bucket.
                var escalated = await _notifications.QueueForSupervisorsAsync(lead,
                    NotificationType.ManagerAttentionRequired,
                    $"No first contact on {name}",
                    $"{_options.FirstResponseHours}h have passed since assignment with no contact recorded.",
                    "first-contact", isEscalation: true, cancellationToken: cancellationToken);

                result.NotificationsCreated += escalated;
                if (escalated > 0)
                    result.EscalationsRaised++;
            }
        }

        private async Task ScanFollowUpsAsync(LeadAlertScanResultDto result, DateTime now, CancellationToken cancellationToken)
        {
            var dueWindow = now.AddHours(_options.FollowUpDueWindowHours);
            var overdueCutoff = now.AddHours(-_options.FollowUpOverdueGraceHours);
            var missedCutoff = now.AddHours(-_options.FollowUpMissedAfterHours);

            var followUps = await _context.LeadFollowUps
                .Include(f => f.Lead)
                .Where(f => f.Status == LeadFollowUpStatus.Pending && f.DueAt <= dueWindow)
                .OrderBy(f => f.DueAt)
                .Take(_options.MaxRowsPerScan)
                .ToListAsync(cancellationToken);

            foreach (var followUp in followUps)
            {
                var lead = followUp.Lead;
                var name = Name(lead);
                var ownerUserId = await EmployeeUserIdAsync(followUp.AssignedEmployeeId, cancellationToken);
                var occurrence = Occurrence(followUp);

                if (followUp.DueAt < missedCutoff)
                {
                    // Long past due and still untouched: record it as missed so it stops
                    // being "pending" for ever and shows honestly in performance figures.
                    followUp.Status = LeadFollowUpStatus.Missed;
                    followUp.UpdatedAt = now;
                    result.FollowUpsMarkedMissed++;

                    LeadTimeline.Record(_context, lead, LeadActivityType.FollowUpMissed,
                        $"{followUp.Type} missed — it was due {followUp.DueAt:yyyy-MM-dd HH:mm} UTC.", null,
                        a =>
                        {
                            a.FollowUpId = followUp.Id;
                            a.Notes = followUp.Title;
                        });

                    var escalated = await _notifications.QueueForSupervisorsAsync(lead,
                        NotificationType.ManagerAttentionRequired,
                        $"Follow-up missed on {name}",
                        $"{followUp.Title} was due {followUp.DueAt:yyyy-MM-dd HH:mm} UTC.",
                        $"followup-missed:{followUp.Id}:{occurrence}", isEscalation: true, cancellationToken: cancellationToken);

                    result.NotificationsCreated += escalated;
                    if (escalated > 0)
                        result.EscalationsRaised++;
                }
                else if (followUp.DueAt < overdueCutoff)
                {
                    result.FollowUpsOverdue++;

                    if (ownerUserId.HasValue &&
                        await _notifications.QueueAsync(lead.Id, ownerUserId.Value,
                            NotificationType.FollowUpOverdue,
                            $"Follow-up overdue: {name}",
                            $"{followUp.Title} was due {followUp.DueAt:yyyy-MM-dd HH:mm} UTC.",
                            $"FollowUpOverdue:{followUp.Id}:{ownerUserId.Value}:{occurrence}", cancellationToken: cancellationToken))
                        result.NotificationsCreated++;
                }
                else
                {
                    result.FollowUpsDue++;

                    if (ownerUserId.HasValue &&
                        await _notifications.QueueAsync(lead.Id, ownerUserId.Value,
                            NotificationType.FollowUpDue,
                            $"Follow-up due: {name}",
                            $"{followUp.Title} is due {followUp.DueAt:yyyy-MM-dd HH:mm} UTC.",
                            $"FollowUpDue:{followUp.Id}:{ownerUserId.Value}:{occurrence}", cancellationToken: cancellationToken))
                        result.NotificationsCreated++;
                }
            }
        }

        private async Task ScanInactiveLeadsAsync(LeadAlertScanResultDto result, DateTime now, CancellationToken cancellationToken)
        {
            var cutoff = now.AddDays(-_options.InactivityDays);
            var bucket = now.ToString("yyyy-MM-dd");
            var bucketStart = now.Date;

            // A lead already evaluated today is skipped, so the batch reaches the rest of the
            // quiet leads instead of re-reading the same oldest ones every scan.
            var stale = await _context.Leads
                .Where(l => !LeadStageRules.ClosedStages.Contains(l.Stage)
                            && l.AssignedEmployeeId != null
                            && (l.LastActivityAt == null ? l.CreatedAt : l.LastActivityAt.Value) < cutoff
                            && !_context.LeadAlertChecks.Any(c => c.LeadId == l.Id && c.InactivityCheckedAt >= bucketStart))
                .OrderBy(l => l.LastActivityAt)
                .Take(_options.MaxRowsPerScan)
                .ToListAsync(cancellationToken);

            await LoadChecksAsync(stale, cancellationToken);

            foreach (var lead in stale)
            {
                _checks[lead.Id].InactivityCheckedAt = now;
                result.InactiveLeads++;

                var name = Name(lead);
                var since = lead.LastActivityAt ?? lead.CreatedAt;
                var ownerUserId = await OwnerUserIdAsync(lead, cancellationToken);

                // Bucketed by day: a lead that stays quiet nudges once a day, not every scan.
                if (ownerUserId.HasValue &&
                    await _notifications.QueueAsync(lead.Id, ownerUserId.Value, NotificationType.LeadInactive,
                        $"No activity on {name}",
                        $"Nothing has been recorded since {since:yyyy-MM-dd}.",
                        $"LeadInactive:{lead.Id}:{ownerUserId.Value}:{bucket}", cancellationToken: cancellationToken))
                    result.NotificationsCreated++;

                var escalated = await _notifications.QueueForSupervisorsAsync(lead,
                    NotificationType.ManagerAttentionRequired,
                    $"{name} has gone quiet",
                    $"No activity since {since:yyyy-MM-dd}.",
                    $"inactive:{bucket}", isEscalation: true, cancellationToken: cancellationToken);

                result.NotificationsCreated += escalated;
                if (escalated > 0)
                    result.EscalationsRaised++;
            }
        }

        private async Task ScanSiteVisitsAsync(LeadAlertScanResultDto result, DateTime now, CancellationToken cancellationToken)
        {
            var missedCutoff = now.AddHours(-_options.SiteVisitMissedAfterHours);
            var endOfDay = now.Date.AddDays(1);
            var bucket = now.ToString("yyyy-MM-dd");

            var visits = await _context.LeadSiteVisits
                .Include(v => v.Lead)
                .Where(v => (v.Status == LeadSiteVisitStatus.Scheduled || v.Status == LeadSiteVisitStatus.Rescheduled)
                            && v.ScheduledAt < endOfDay)
                .OrderBy(v => v.ScheduledAt)
                .Take(_options.MaxRowsPerScan)
                .ToListAsync(cancellationToken);

            foreach (var visit in visits)
            {
                var lead = visit.Lead;
                var name = Name(lead);
                var ownerUserId = await EmployeeUserIdAsync(visit.AssignedEmployeeId, cancellationToken);

                if (visit.ScheduledAt < missedCutoff)
                {
                    visit.Status = LeadSiteVisitStatus.Missed;
                    visit.CancellationReason ??= "No outcome was recorded after the scheduled time.";
                    visit.UpdatedAt = now;
                    result.SiteVisitsMarkedMissed++;

                    LeadTimeline.Record(_context, lead, LeadActivityType.SiteVisitMissed,
                        $"Site visit missed — it was scheduled for {visit.ScheduledAt:yyyy-MM-dd HH:mm} UTC.", null,
                        a => a.SiteVisitId = visit.Id);

                    var escalated = await _notifications.QueueForSupervisorsAsync(lead,
                        NotificationType.SiteVisitMissed,
                        $"Site visit missed: {name}",
                        $"Scheduled for {visit.ScheduledAt:yyyy-MM-dd HH:mm} UTC with no outcome recorded.",
                        $"visit-missed:{visit.Id}", isEscalation: true, cancellationToken: cancellationToken);

                    result.NotificationsCreated += escalated;
                    if (escalated > 0)
                        result.EscalationsRaised++;
                }
                else if (visit.ScheduledAt >= now)
                {
                    result.SiteVisitsToday++;

                    if (ownerUserId.HasValue &&
                        await _notifications.QueueAsync(lead.Id, ownerUserId.Value, NotificationType.SiteVisitReminder,
                            $"Site visit today: {name}",
                            $"{visit.ScheduledAt:HH:mm} UTC at {visit.MeetingLocation}.",
                            $"SiteVisitToday:{visit.Id}:{ownerUserId.Value}:{bucket}", cancellationToken: cancellationToken))
                        result.NotificationsCreated++;
                }
            }
        }

        private static string Name(Lead lead) => LeadService.FullName(lead);

        // Rescheduling keeps the same follow-up row and moves DueAt. Keying follow-up alerts
        // by the schedule they were raised for lets the new time be reminded and escalated
        // afresh, while repeat scans of one schedule still produce the same key.
        private static string Occurrence(LeadFollowUp followUp) =>
            followUp.DueAt.ToString("yyyyMMddHHmmssfffffff", CultureInfo.InvariantCulture);

        private Task<int?> OwnerUserIdAsync(Lead lead, CancellationToken cancellationToken) =>
            lead.AssignedEmployeeId == null
                ? Task.FromResult<int?>(null)
                : EmployeeUserIdAsync(lead.AssignedEmployeeId.Value, cancellationToken);

        // Both lead scans can reach the same lead in one run; each gets one tracked row.
        private readonly Dictionary<int, LeadAlertCheck> _checks = new();

        private async Task LoadChecksAsync(List<Lead> leads, CancellationToken cancellationToken)
        {
            var missing = leads.Select(l => l.Id).Where(id => !_checks.ContainsKey(id)).ToList();
            if (missing.Count == 0)
                return;

            foreach (var check in await _context.LeadAlertChecks
                         .Where(c => missing.Contains(c.LeadId))
                         .ToListAsync(cancellationToken))
                _checks[check.LeadId] = check;

            foreach (var leadId in missing.Where(id => !_checks.ContainsKey(id)))
            {
                var check = new LeadAlertCheck { LeadId = leadId };
                _context.LeadAlertChecks.Add(check);
                _checks[leadId] = check;
            }
        }

        // A scan touches the same handful of employees over and over; look each up once.
        private readonly Dictionary<int, int?> _employeeUserIds = new();

        private async Task<int?> EmployeeUserIdAsync(int employeeId, CancellationToken cancellationToken)
        {
            if (_employeeUserIds.TryGetValue(employeeId, out var cached))
                return cached;

            var userId = await _context.Employees
                .AsNoTracking()
                .Where(e => e.Id == employeeId)
                .Select(e => e.UserId)
                .FirstOrDefaultAsync(cancellationToken);

            _employeeUserIds[employeeId] = userId;
            return userId;
        }
    }
}
