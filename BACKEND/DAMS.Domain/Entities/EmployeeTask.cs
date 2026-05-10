using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    public class EmployeeTask
    {
        public int Id { get; set; }

        public int EmployeeId { get; set; }

        public int? ProjectId { get; set; }

        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }

        public TaskPriority Priority { get; set; } = TaskPriority.Medium;

        public EmployeeTaskStatus Status { get; set; } = EmployeeTaskStatus.Pending;

        public DateTime? DueDate { get; set; }

        public DateTime? CompletedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public Employee Employee { get; set; } = null!;

        public Project? Project { get; set; }
    }
}
