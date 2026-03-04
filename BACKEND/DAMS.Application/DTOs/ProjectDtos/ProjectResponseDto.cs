using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.ProjectDtos
{
    public class ProjectResponseDto
    {
        public int Id { get; set; }

        public string ProjectName { get; set; } = string.Empty;

        public string Location { get; set; } = string.Empty;

        public string? Description { get; set; }

        public DateTime StartingDate { get; set; }

        public DateTime? ExpectedCompletionDate { get; set; }

        public ProjectStatus Status { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}