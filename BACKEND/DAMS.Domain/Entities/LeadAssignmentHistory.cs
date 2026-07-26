namespace DAMS.Domain.Entities
{
    /// <summary>Who owned a lead, when, and why it changed. Append-only.</summary>
    public class LeadAssignmentHistory
    {
        public int Id { get; set; }

        public int LeadId { get; set; }

        public int? PreviousEmployeeId { get; set; }

        public int? PreviousTeamId { get; set; }

        public int? AssignedEmployeeId { get; set; }

        public int? AssignedTeamId { get; set; }

        public string? Reason { get; set; }

        public int? AssignedByUserId { get; set; }

        public string? AssignedByName { get; set; }

        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

        public Lead Lead { get; set; } = null!;
    }
}
