using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>A scheduled or completed visit to a project/unit with the customer.</summary>
    public class LeadSiteVisit
    {
        public int Id { get; set; }

        public int LeadId { get; set; }

        public int? ProjectId { get; set; }

        public int? UnitId { get; set; }

        public int AssignedEmployeeId { get; set; }

        public DateTime ScheduledAt { get; set; }

        public string MeetingLocation { get; set; } = string.Empty;

        public string? CustomerAttendees { get; set; }

        public string? InternalAttendees { get; set; }

        public LeadSiteVisitStatus Status { get; set; } = LeadSiteVisitStatus.Scheduled;

        public DateTime? RemindAt { get; set; }

        public string? Notes { get; set; }

        public LeadSiteVisitOutcome? Outcome { get; set; }

        public string? OutcomeNotes { get; set; }

        public string? CustomerFeedback { get; set; }

        public string? NextAction { get; set; }

        public DateTime? CompletedAt { get; set; }

        // Set when the visit was moved; the first scheduled time is kept for history.
        public DateTime? OriginalScheduledAt { get; set; }

        public int RescheduleCount { get; set; }

        public string? CancellationReason { get; set; }

        public int? CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public Lead Lead { get; set; } = null!;

        public Employee AssignedEmployee { get; set; } = null!;

        public Project? Project { get; set; }

        public Unit? Unit { get; set; }
    }
}
