using DAMS.Application.DTOs.ProjectDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public class ProjectService : IProjectService
    {
        private readonly AppDbContext _context;

        public ProjectService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<ProjectResponseDto> CreateProjectAsync(CreateProjectDto dto, int adminId)
        {
            // Check uniqueness
            if (await _context.Projects.AnyAsync(p => p.ProjectName == dto.ProjectName))
                throw new Exception("Project name already exists.");

            var project = new Project
            {
                ProjectName = dto.ProjectName,
                Location = dto.Location,
                Description = dto.Description,
                StartingDate = dto.StartingDate,
                ExpectedCompletionDate = dto.ExpectedCompletionDate,
                CreatedById = adminId
            };

            _context.Projects.Add(project);
            await _context.SaveChangesAsync();

            return MapToResponse(project);
        }

        public async Task<ProjectResponseDto> UpdateProjectAsync(int id, UpdateProjectDto dto)
        {
            var project = await _context.Projects.FindAsync(id);

            if (project == null)
                throw new Exception("Project not found.");

            project.ProjectName = dto.ProjectName;
            project.Location = dto.Location;
            project.Description = dto.Description;
            project.StartingDate = dto.StartingDate;
            project.ExpectedCompletionDate = dto.ExpectedCompletionDate;
            project.Status = dto.Status;
            project.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return MapToResponse(project);
        }

        public async Task<List<ProjectResponseDto>> GetAllProjectsAsync()
        {
            var projects = await _context.Projects.ToListAsync();
            return projects.Select(MapToResponse).ToList();
        }

        public async Task<ProjectResponseDto?> GetProjectByIdAsync(int id)
        {
            var project = await _context.Projects.FindAsync(id);

            return project == null ? null : MapToResponse(project);
        }

        private static ProjectResponseDto MapToResponse(Project project)
        {
            return new ProjectResponseDto
            {
                Id = project.Id,
                ProjectName = project.ProjectName,
                Location = project.Location,
                Description = project.Description,
                StartingDate = project.StartingDate,
                ExpectedCompletionDate = project.ExpectedCompletionDate,
                Status = project.Status,
                CreatedAt = project.CreatedAt
            };
        }
    }
}