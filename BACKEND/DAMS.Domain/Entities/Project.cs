using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    public class Project
    {
        public int Id { get; set; }

        public string ProjectName { get; set; } = string.Empty;

        public string Location { get; set; } = string.Empty;

        public string? Description { get; set; }

        public DateTime StartingDate { get; set; }

        public DateTime? ExpectedCompletionDate { get; set; }

        public ProjectStatus Status { get; set; } = ProjectStatus.Ongoing;

      

        public int CreatedById { get; set; } 

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        // Navigation Property
        public ICollection<Unit> Units { get; set; } = new List<Unit>();
        public ICollection<ProjectMedia> MediaFiles { get; set; } = new List<ProjectMedia>();
    }
}
