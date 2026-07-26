using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>A task, follow-up or reminder attached to a lead.</summary>
    public class LeadFollowUp
    {
        public int Id { get; set; }

        public int LeadId { get; set; }

        public LeadFollowUpType Type { get; set; } = LeadFollowUpType.FollowUp;

        public int AssignedEmployeeId { get; set; }

        public string Title { get; set; } = string.Empty;

        public string? Notes { get; set; }

        public DateTime DueAt { get; set; }

        public DateTime? RemindAt { get; set; }

        public TaskPriority Priority { get; set; } = TaskPriority.Medium;

        public LeadFollowUpStatus Status { get; set; } = LeadFollowUpStatus.Pending;

        public DateTime? CompletedAt { get; set; }

        public string? Outcome { get; set; }

        public int? CompletedByUserId { get; set; }

        public int? CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public Lead Lead { get; set; } = null!;

        public Employee AssignedEmployee { get; set; } = null!;
    }
}
