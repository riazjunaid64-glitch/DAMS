using System.Text.Json.Serialization;
using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.ProjectDtos
{
    public class ProjectResponseDto
    {
        public int Id { get; set; }

        public string ProjectName { get; set; } = string.Empty;

        public string Location { get; set; } = string.Empty;

        public string? Category { get; set; }

        public string? CoverImageUrl { get; set; }

        public string? Description { get; set; }

        public DateTime? StartingDate { get; set; }

        public DateTime? ExpectedCompletionDate { get; set; }

        public ProjectStatus Status { get; set; }

        public DateTime CreatedAt { get; set; }

        public int TotalUnits { get; set; }

        public int AvailableUnits { get; set; }

        public int BookedUnits { get; set; }

        public int SoldUnits { get; set; }

        /// <summary>0 while the project has no floor list.</summary>
        public int FloorCount { get; set; }

        /// <summary>The floor list, bottom to top. Only the project detail fills it; the list leaves it out.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ProjectFloorDto>? Floors { get; set; }
    }
}