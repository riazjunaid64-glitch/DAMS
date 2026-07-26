using DAMS.Application.Common;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DAMS.Application.Services
{
    /// <summary>
    /// Dashboard and reporting reads. Counts are aggregated in SQL; the breakdowns and time
    /// averages are derived from one narrow projection (a handful of columns per lead)
    /// rather than a query each, which is both fewer round trips and free of any
    /// provider-specific date functions.
    ///
    /// Every figure is computed over the caller's own scope, so a manager's numbers cover
    /// their team and nobody else's.
    /// </summary>
    public class LeadReportingService : ILeadReportingService
    {
        /// <summary>A lead that reached at least Qualified, for source/campaign attribution.</summary>
        private static readonly LeadStage[] QualifiedOrBetter =
        {
            LeadStage.Qualified, LeadStage.SiteVisitScheduled, LeadStage.SiteVisitCompleted,
            LeadStage.Negotiation, LeadStage.DocumentsInProgress, LeadStage.BookingPending, LeadStage.Won
        };

        private readonly AppDbContext _context;
        private readonly LeadAlertOptions _options;
        private readonly TimeProvider _clock;

        public LeadReportingService(AppDbContext context, IOptions<LeadAlertOptions> options, TimeProvider clock)
        {
            _context = context;
            _options = options.Value;
            _clock = clock;
        }

        /// <summary>The columns every breakdown is derived from.</summary>
        private sealed record LeadFact(
            LeadStage Stage,
            int LeadSourceId,
            string SourceCode,
            string SourceName,
            string? CampaignName,
            string? CampaignReference,
            int? EmployeeId,
            string? EmployeeName,
            int? TeamId,
            string? TeamName,
            int? ClosureReasonId,
            string? ClosureReasonName,
            DateTime CreatedAt,
            DateTime? UpdatedAt,
            DateTime? AssignedAt,
            DateTime? FirstContactAt,
            DateTime? ConvertedAt);

        private static Task<List<LeadFact>> LoadFactsAsync(IQueryable<Lead> query, CancellationToken cancellationToken) =>
            query.Select(l => new LeadFact(
                    l.Stage,
                    l.LeadSourceId,
                    l.Source.Code,
                    l.Source.Name,
                    l.CampaignName,
                    l.CampaignReference,
                    l.AssignedEmployeeId,
                    l.AssignedEmployee != null ? l.AssignedEmployee.FullName : null,
                    l.AssignedTeamId != null
                        ? l.AssignedTeamId
                        : (l.AssignedEmployee != null ? l.AssignedEmployee.TeamId : null),
                    l.AssignedTeam != null
                        ? l.AssignedTeam.Name
                        : (l.AssignedEmployee != null && l.AssignedEmployee.Team != null ? l.AssignedEmployee.Team.Name : null),
                    l.ClosureReasonId,
                    l.ClosureReason != null ? l.ClosureReason.Name : null,
                    l.CreatedAt,
                    l.UpdatedAt,
                    l.AssignedAt,
                    l.FirstContactAt,
                    l.ConvertedAt))
                .ToListAsync(cancellationToken);

        public async Task<EmployeeLeadDashboardDto> GetEmployeeDashboardAsync(
            LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            LeadAccess.EnsureStaff(ctx);

            var now = _clock.GetUtcNow().UtcDateTime;
            var endOfToday = now.Date.AddDays(1);
            var inactiveCutoff = now.AddDays(-_options.InactivityDays);

            var mine = ctx.EmployeeId == null
                ? _context.Leads.AsNoTracking().Where(_ => false)
                : _context.Leads.AsNoTracking().Where(l => l.AssignedEmployeeId == ctx.EmployeeId);

            var open = mine.Where(l => !LeadStageRules.ClosedStages.Contains(l.Stage));

            var dashboard = new EmployeeLeadDashboardDto
            {
                EmployeeId = ctx.EmployeeId,
                EmployeeName = ctx.DisplayName,
                NewLeads = await open.CountAsync(
                    l => l.Stage == LeadStage.New || l.Stage == LeadStage.FirstContactPending, cancellationToken),
                ActiveLeads = await open.CountAsync(cancellationToken),
                LeadsWithoutRecentActivity = await open.CountAsync(
                    l => (l.LastActivityAt == null ? l.CreatedAt : l.LastActivityAt.Value) < inactiveCutoff, cancellationToken),
                Conversions = await mine.CountAsync(l => l.Stage == LeadStage.Won, cancellationToken),
                UnreadNotifications = await _context.LeadNotifications
                    .CountAsync(n => n.RecipientUserId == ctx.UserId && !n.IsRead, cancellationToken),
                ByStage = BuildStageCounts(await LoadFactsAsync(open, cancellationToken), now)
            };

            if (ctx.EmployeeId != null)
            {
                var myFollowUps = _context.LeadFollowUps
                    .AsNoTracking()
                    .Where(f => f.AssignedEmployeeId == ctx.EmployeeId && f.Status == LeadFollowUpStatus.Pending);

                dashboard.FollowUpsDueToday = await myFollowUps.CountAsync(f => f.DueAt < endOfToday && f.DueAt >= now, cancellationToken);
                dashboard.OverdueFollowUps = await myFollowUps.CountAsync(f => f.DueAt < now, cancellationToken);

                dashboard.DueToday = await myFollowUps
                    .Where(f => f.DueAt < endOfToday && f.DueAt >= now)
                    .OrderBy(f => f.DueAt).Take(20)
                    .Select(LeadMapping.ToFollowUpDto).ToListAsync(cancellationToken);

                dashboard.Overdue = await myFollowUps
                    .Where(f => f.DueAt < now)
                    .OrderBy(f => f.DueAt).Take(20)
                    .Select(LeadMapping.ToFollowUpDto).ToListAsync(cancellationToken);

                var myVisits = _context.LeadSiteVisits
                    .AsNoTracking()
                    .Where(v => v.AssignedEmployeeId == ctx.EmployeeId
                                && (v.Status == LeadSiteVisitStatus.Scheduled || v.Status == LeadSiteVisitStatus.Rescheduled)
                                && v.ScheduledAt >= now);

                dashboard.UpcomingSiteVisits = await myVisits.CountAsync(cancellationToken);
                dashboard.NextSiteVisits = await myVisits
                    .OrderBy(v => v.ScheduledAt).Take(10)
                    .Select(LeadMapping.ToSiteVisitDto).ToListAsync(cancellationToken);
            }

            return dashboard;
        }

        public async Task<ManagerLeadDashboardDto> GetManagerDashboardAsync(
            LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            LeadAccess.EnsureStaff(ctx);

            if (ctx.IsEmployee)
                throw new LeadAuthorizationException("The team dashboard is for managers and admins.");

            var now = _clock.GetUtcNow().UtcDateTime;
            var inactiveCutoff = now.AddDays(-_options.InactivityDays);
            var firstContactCutoff = now.AddHours(-_options.FirstResponseHours);

            var scoped = LeadAccess.Scope(_context.Leads.AsNoTracking(), ctx);
            var open = scoped.Where(l => !LeadStageRules.ClosedStages.Contains(l.Stage));
            var scopedLeadIds = scoped.Select(l => l.Id);

            var facts = await LoadFactsAsync(scoped, cancellationToken);
            var won = facts.Count(f => f.Stage == LeadStage.Won);
            var closed = facts.Count(f => LeadStageRules.IsClosed(f.Stage));

            return new ManagerLeadDashboardDto
            {
                TeamLeads = facts.Count,
                UnassignedLeads = await open.CountAsync(
                    l => l.AssignedEmployeeId == null && l.AssignedTeamId == null, cancellationToken),
                OverdueFirstContacts = await open.CountAsync(
                    l => l.FirstContactAt == null && l.AssignedAt != null && l.AssignedAt < firstContactCutoff, cancellationToken),
                OverdueFollowUps = await _context.LeadFollowUps
                    .CountAsync(f => scopedLeadIds.Contains(f.LeadId)
                                     && f.Status == LeadFollowUpStatus.Pending && f.DueAt < now, cancellationToken),
                InactiveLeads = await open.CountAsync(
                    l => (l.LastActivityAt == null ? l.CreatedAt : l.LastActivityAt.Value) < inactiveCutoff, cancellationToken),
                UpcomingSiteVisits = await _context.LeadSiteVisits
                    .CountAsync(v => scopedLeadIds.Contains(v.LeadId)
                                     && (v.Status == LeadSiteVisitStatus.Scheduled || v.Status == LeadSiteVisitStatus.Rescheduled)
                                     && v.ScheduledAt >= now, cancellationToken),
                MissedSiteVisits = await _context.LeadSiteVisits
                    .CountAsync(v => scopedLeadIds.Contains(v.LeadId) && v.Status == LeadSiteVisitStatus.Missed, cancellationToken),
                ManagerReviewRequests = await _context.LeadComments
                    .CountAsync(c => scopedLeadIds.Contains(c.LeadId) && c.IsManagerReviewRequest, cancellationToken),
                WonLeads = won,
                ClosedLeads = closed,
                ConversionRatePercent = Percent(won, facts.Count),
                ByStage = BuildStageCounts(facts.Where(f => !LeadStageRules.IsClosed(f.Stage)).ToList(), now),
                ByEmployee = BuildEmployeePerformance(facts)
            };
        }

        public async Task<AdminLeadDashboardDto> GetAdminDashboardAsync(
            LeadUserContext ctx, DateTime? from, DateTime? to, CancellationToken cancellationToken = default)
        {
            if (!ctx.IsAdmin)
                throw new LeadAuthorizationException("The organisation dashboard is for admins.");

            var now = _clock.GetUtcNow().UtcDateTime;
            var query = _context.Leads.AsNoTracking().AsQueryable();

            if (from.HasValue)
                query = query.Where(l => l.CreatedAt >= from.Value.Date);
            if (to.HasValue)
            {
                var toExclusive = to.Value.Date.AddDays(1);
                query = query.Where(l => l.CreatedAt < toExclusive);
            }

            var facts = await LoadFactsAsync(query, cancellationToken);
            var won = facts.Count(f => f.Stage == LeadStage.Won);

            // Averages come from the two timestamp pairs the workflow guarantees:
            // assignment to first contact, and creation to conversion.
            var responseHours = facts
                .Where(f => f.AssignedAt != null && f.FirstContactAt != null && f.FirstContactAt >= f.AssignedAt)
                .Select(f => (f.FirstContactAt!.Value - f.AssignedAt!.Value).TotalHours)
                .ToList();

            var conversionDays = facts
                .Where(f => f.ConvertedAt != null && f.ConvertedAt >= f.CreatedAt)
                .Select(f => (f.ConvertedAt!.Value - f.CreatedAt).TotalDays)
                .ToList();

            return new AdminLeadDashboardDto
            {
                TotalLeads = facts.Count,
                OpenLeads = facts.Count(f => !LeadStageRules.IsClosed(f.Stage)),
                UnassignedLeads = facts.Count(f => f.EmployeeId == null && f.TeamId == null && !LeadStageRules.IsClosed(f.Stage)),
                WonLeads = won,
                LostLeads = facts.Count(f => f.Stage == LeadStage.Lost),
                DormantLeads = facts.Count(f => f.Stage == LeadStage.Dormant),
                AverageFirstResponseHours = Average(responseHours),
                AverageConversionDays = Average(conversionDays),
                ConversionRatePercent = Percent(won, facts.Count),
                ByStage = BuildStageCounts(facts.Where(f => !LeadStageRules.IsClosed(f.Stage)).ToList(), now),
                BySource = BuildSourcePerformance(facts),
                ByCampaign = BuildCampaignPerformance(facts),
                LossReasons = BuildLossReasons(facts),
                ByEmployee = BuildEmployeePerformance(facts),
                ByTeam = BuildTeamPerformance(facts)
            };
        }

        private static List<LeadStageCountDto> BuildStageCounts(IReadOnlyCollection<LeadFact> facts, DateTime now) =>
            facts
                .GroupBy(f => f.Stage)
                .Select(g => new LeadStageCountDto
                {
                    Stage = g.Key,
                    Count = g.Count(),
                    // Stage aging: how long these leads have been sitting where they are.
                    AverageAgeDays = Math.Round(g.Average(f => (now - (f.UpdatedAt ?? f.CreatedAt)).TotalDays), 2)
                })
                .OrderBy(r => r.Stage)
                .ToList();

        private static List<EmployeePerformanceDto> BuildEmployeePerformance(IReadOnlyCollection<LeadFact> facts) =>
            facts
                .Where(f => f.EmployeeId != null)
                .GroupBy(f => new { EmployeeId = f.EmployeeId!.Value, f.EmployeeName, f.TeamName })
                .Select(g =>
                {
                    var responseHours = g
                        .Where(f => f.AssignedAt != null && f.FirstContactAt != null && f.FirstContactAt >= f.AssignedAt)
                        .Select(f => (f.FirstContactAt!.Value - f.AssignedAt!.Value).TotalHours)
                        .ToList();

                    var wonCount = g.Count(f => f.Stage == LeadStage.Won);

                    return new EmployeePerformanceDto
                    {
                        EmployeeId = g.Key.EmployeeId,
                        EmployeeName = g.Key.EmployeeName ?? string.Empty,
                        TeamName = g.Key.TeamName,
                        TotalLeads = g.Count(),
                        ActiveLeads = g.Count(f => !LeadStageRules.IsClosed(f.Stage)),
                        WonLeads = wonCount,
                        LostLeads = g.Count(f => f.Stage == LeadStage.Lost),
                        ConversionRatePercent = Percent(wonCount, g.Count()),
                        AverageFirstResponseHours = Average(responseHours)
                    };
                })
                .OrderByDescending(r => r.WonLeads)
                .ThenByDescending(r => r.TotalLeads)
                .ToList();

        private static List<LeadSourcePerformanceDto> BuildSourcePerformance(IReadOnlyCollection<LeadFact> facts) =>
            facts
                .GroupBy(f => new { f.LeadSourceId, f.SourceCode, f.SourceName })
                .Select(g =>
                {
                    var total = g.Count();
                    var qualified = g.Count(f => QualifiedOrBetter.Contains(f.Stage));
                    var won = g.Count(f => f.Stage == LeadStage.Won);

                    return new LeadSourcePerformanceDto
                    {
                        LeadSourceId = g.Key.LeadSourceId,
                        SourceCode = g.Key.SourceCode,
                        SourceName = g.Key.SourceName,
                        TotalLeads = total,
                        QualifiedLeads = qualified,
                        WonLeads = won,
                        LostLeads = g.Count(f => f.Stage == LeadStage.Lost),
                        SourceToQualifiedPercent = Percent(qualified, total),
                        SourceToBookingPercent = Percent(won, total)
                    };
                })
                .OrderByDescending(r => r.TotalLeads)
                .ToList();

        private static List<LeadCampaignPerformanceDto> BuildCampaignPerformance(IReadOnlyCollection<LeadFact> facts) =>
            facts
                .Where(f => !string.IsNullOrWhiteSpace(f.CampaignName))
                .GroupBy(f => new { f.CampaignName, f.CampaignReference })
                .Select(g =>
                {
                    var total = g.Count();
                    var won = g.Count(f => f.Stage == LeadStage.Won);

                    return new LeadCampaignPerformanceDto
                    {
                        CampaignName = g.Key.CampaignName!,
                        CampaignReference = g.Key.CampaignReference,
                        TotalLeads = total,
                        QualifiedLeads = g.Count(f => QualifiedOrBetter.Contains(f.Stage)),
                        WonLeads = won,
                        ConversionRatePercent = Percent(won, total)
                    };
                })
                .OrderByDescending(r => r.TotalLeads)
                .ToList();

        private static List<LeadClosureReasonCountDto> BuildLossReasons(IReadOnlyCollection<LeadFact> facts) =>
            facts
                .Where(f => f.Stage is LeadStage.Lost or LeadStage.Dormant)
                .GroupBy(f => new { f.ClosureReasonId, f.ClosureReasonName })
                .Select(g => new LeadClosureReasonCountDto
                {
                    ClosureReasonId = g.Key.ClosureReasonId,
                    ReasonName = g.Key.ClosureReasonName ?? "Not recorded",
                    Count = g.Count()
                })
                .OrderByDescending(r => r.Count)
                .ToList();

        private static List<TeamPerformanceDto> BuildTeamPerformance(IReadOnlyCollection<LeadFact> facts) =>
            facts
                .Where(f => f.TeamId != null)
                .GroupBy(f => new { TeamId = f.TeamId!.Value, f.TeamName })
                .Select(g =>
                {
                    var total = g.Count();
                    var won = g.Count(f => f.Stage == LeadStage.Won);

                    return new TeamPerformanceDto
                    {
                        TeamId = g.Key.TeamId,
                        TeamName = g.Key.TeamName ?? string.Empty,
                        TotalLeads = total,
                        WonLeads = won,
                        ConversionRatePercent = Percent(won, total)
                    };
                })
                .OrderByDescending(r => r.WonLeads)
                .ToList();

        private static double Percent(int part, int total) =>
            total <= 0 ? 0 : Math.Round(part * 100d / total, 2);

        private static double? Average(IReadOnlyCollection<double> samples) =>
            samples.Count == 0 ? null : Math.Round(samples.Average(), 2);
    }
}
