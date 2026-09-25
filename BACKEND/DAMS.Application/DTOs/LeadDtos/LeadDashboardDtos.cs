using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.LeadDtos
{
    public class LeadStageCountDto
    {
        public LeadStage Stage { get; set; }

        public int Count { get; set; }

        /// <summary>Average days leads have been sitting in this stage.</summary>
        public double AverageAgeDays { get; set; }
    }

    public class EmployeeLeadDashboardDto
    {
        public int? EmployeeId { get; set; }

        public string? EmployeeName { get; set; }

        public int NewLeads { get; set; }

        public int ActiveLeads { get; set; }

        public int FollowUpsDueToday { get; set; }

        public int OverdueFollowUps { get; set; }

        public int UpcomingSiteVisits { get; set; }

        public int LeadsWithoutRecentActivity { get; set; }

        public int Conversions { get; set; }

        public int UnreadNotifications { get; set; }

        public List<LeadStageCountDto> ByStage { get; set; } = new();

        public List<LeadFollowUpDto> DueToday { get; set; } = new();

        public List<LeadFollowUpDto> Overdue { get; set; } = new();

        public List<LeadSiteVisitDto> NextSiteVisits { get; set; } = new();
    }

    public class ManagerLeadDashboardDto
    {
        public int TeamLeads { get; set; }

        public int UnassignedLeads { get; set; }

        public int OverdueFirstContacts { get; set; }

        public int OverdueFollowUps { get; set; }

        public int InactiveLeads { get; set; }

        public int UpcomingSiteVisits { get; set; }

        public int MissedSiteVisits { get; set; }

        public int ManagerReviewRequests { get; set; }

        public int WonLeads { get; set; }

        public int ClosedLeads { get; set; }

        public double ConversionRatePercent { get; set; }

        public List<LeadStageCountDto> ByStage { get; set; } = new();

        public List<EmployeePerformanceDto> ByEmployee { get; set; } = new();
    }

    public class EmployeePerformanceDto
    {
        public int EmployeeId { get; set; }

        public string EmployeeName { get; set; } = string.Empty;

        public string? TeamName { get; set; }

        public int TotalLeads { get; set; }

        public int ActiveLeads { get; set; }

        public int WonLeads { get; set; }

        public int LostLeads { get; set; }

        public int OverdueFollowUps { get; set; }

        public int InactiveLeads { get; set; }

        public double ConversionRatePercent { get; set; }

        /// <summary>Average hours between lead assignment and the first recorded contact.</summary>
        public double? AverageFirstResponseHours { get; set; }
    }

    public class AdminLeadDashboardDto
    {
        public int TotalLeads { get; set; }

        public int OpenLeads { get; set; }

        public int UnassignedLeads { get; set; }

        public int WonLeads { get; set; }

        public int LostLeads { get; set; }

        public int DormantLeads { get; set; }

        public double? AverageFirstResponseHours { get; set; }

        public double? AverageConversionDays { get; set; }

        public double ConversionRatePercent { get; set; }

        public List<LeadStageCountDto> ByStage { get; set; } = new();

        public List<LeadSourcePerformanceDto> BySource { get; set; } = new();

        public List<LeadCampaignPerformanceDto> ByCampaign { get; set; } = new();

        public List<LeadClosureReasonCountDto> LossReasons { get; set; } = new();

        public List<EmployeePerformanceDto> ByEmployee { get; set; } = new();

        public List<TeamPerformanceDto> ByTeam { get; set; } = new();
    }

    public class LeadSourcePerformanceDto
    {
        public int LeadSourceId { get; set; }

        public string SourceCode { get; set; } = string.Empty;

        public string SourceName { get; set; } = string.Empty;

        public int TotalLeads { get; set; }

        public int QualifiedLeads { get; set; }

        public int WonLeads { get; set; }

        public int LostLeads { get; set; }

        public double SourceToQualifiedPercent { get; set; }

        public double SourceToBookingPercent { get; set; }
    }

    public class LeadCampaignPerformanceDto
    {
        public string CampaignName { get; set; } = string.Empty;

        public string? CampaignReference { get; set; }

        public int TotalLeads { get; set; }

        public int QualifiedLeads { get; set; }

        public int WonLeads { get; set; }

        public double ConversionRatePercent { get; set; }
    }

    public class LeadClosureReasonCountDto
    {
        public int? ClosureReasonId { get; set; }

        public string ReasonName { get; set; } = string.Empty;

        public int Count { get; set; }
    }

    public class TeamPerformanceDto
    {
        public int TeamId { get; set; }

        public string TeamName { get; set; } = string.Empty;

        public int TotalLeads { get; set; }

        public int WonLeads { get; set; }

        public double ConversionRatePercent { get; set; }
    }

    public class LeadAlertScanResultDto
    {
        /// <summary>Count of leads newly evaluated for first contact overdue in this scan run.
        /// Note: this is not the total number of currently overdue leads, but the count of leads
        /// newly checked by this scan (moves through backlog in batches to avoid starvation).</summary>
        public int FirstContactOverdue { get; set; }

        public int FollowUpsDue { get; set; }

        public int FollowUpsOverdue { get; set; }

        public int FollowUpsMarkedMissed { get; set; }

        /// <summary>Count of leads newly evaluated for inactivity in this scan run.
        /// Note: this is not the total number of currently inactive leads, but the count of leads
        /// newly checked by this scan (moves through backlog in batches to avoid starvation).</summary>
        public int InactiveLeads { get; set; }

        public int SiteVisitsToday { get; set; }

        public int SiteVisitsMarkedMissed { get; set; }

        public int NotificationsCreated { get; set; }

        public int EscalationsRaised { get; set; }
    }
}
