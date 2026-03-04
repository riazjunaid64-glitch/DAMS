using DAMS.Application.DTOs.ProjectDtos;

namespace DAMS.Application.Interfaces
{
    public interface IProjectService
    {
        Task<ProjectResponseDto> CreateProjectAsync(CreateProjectDto dto, int adminId);

        Task<ProjectResponseDto> UpdateProjectAsync(int id, UpdateProjectDto dto);

        Task<List<ProjectResponseDto>> GetAllProjectsAsync();

        Task<ProjectResponseDto?> GetProjectByIdAsync(int id);
    }
}