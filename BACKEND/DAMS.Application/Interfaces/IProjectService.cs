using DAMS.Application.DTOs.ProjectDtos;

namespace DAMS.Application.Interfaces
{
    public interface IProjectService
    {
        Task<ProjectResponseDto> CreateProjectAsync(CreateProjectDto dto, int adminId);

        Task<ProjectResponseDto> UpdateProjectAsync(int id, UpdateProjectDto dto);

        Task<List<ProjectResponseDto>> GetAllProjectsAsync();

        Task<ProjectResponseDto?> GetProjectByIdAsync(int id);

        /// <summary>The project's floors bottom to top with their unit counts, or null when the project does not exist.</summary>
        Task<List<ProjectFloorResponseDto>?> GetFloorsAsync(int projectId);

        /// <summary>Replaces the whole floor list in one save and returns it as <see cref="GetFloorsAsync"/> does.</summary>
        Task<List<ProjectFloorResponseDto>> ReplaceFloorsAsync(int projectId, List<ProjectFloorDto>? floors);
    }
}