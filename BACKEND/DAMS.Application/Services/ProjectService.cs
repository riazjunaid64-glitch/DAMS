using DAMS.Application.DTOs.ProjectDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Infrastructure.Data;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public class ProjectService : IProjectService
    {
        private readonly AppDbContext _context;
        private readonly IMemoryCache _cache;
        private static readonly TimeSpan ProjectsCacheDuration = TimeSpan.FromSeconds(30);
        private const string AllProjectsCacheKey = "projects:all";

        public ProjectService(AppDbContext context, IMemoryCache cache)
        {
            _context = context;
            _cache = cache;
        }

        public async Task<ProjectResponseDto> CreateProjectAsync(CreateProjectDto dto, int adminId)
        {
            if (await _context.Projects.AnyAsync(p => p.ProjectName == dto.ProjectName))
                throw new Exception("Project name already exists.");

            var project = new Project
            {
                ProjectName = dto.ProjectName,
                Location = dto.Location,
                Category = dto.Category,
                Description = dto.Description,
                StartingDate = dto.StartingDate,
                ExpectedCompletionDate = dto.ExpectedCompletionDate,
                CreatedById = adminId
            };

            _context.Projects.Add(project);
            await _context.SaveChangesAsync();
            _cache.Remove(AllProjectsCacheKey);

            return MapToResponse(project);
        }

        public async Task<ProjectResponseDto> UpdateProjectAsync(int id, UpdateProjectDto dto)
        {
            var project = await _context.Projects.FindAsync(id);

            if (project == null)
                throw new Exception("Project not found.");

            project.ProjectName = dto.ProjectName;
            project.Location = dto.Location;
            project.Category = dto.Category;
            project.Description = dto.Description;
            project.StartingDate = dto.StartingDate;
            project.ExpectedCompletionDate = dto.ExpectedCompletionDate;
            project.Status = dto.Status;
            project.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            _cache.Remove(AllProjectsCacheKey);

            return await GetProjectByIdAsync(id) ?? MapToResponse(project);
        }

        public async Task<List<ProjectResponseDto>> GetAllProjectsAsync()
        {
            if (_cache.TryGetValue(AllProjectsCacheKey, out List<ProjectResponseDto>? cached) && cached != null)
            {
                return cached;
            }

            var projects = await _context.Projects
                .AsNoTracking()
                .OrderBy(p => p.Id)
                .Select(MapToDtoExpression)
                .ToListAsync();

            _cache.Set(AllProjectsCacheKey, projects, ProjectsCacheDuration);
            return projects;
        }

        public async Task<ProjectResponseDto?> GetProjectByIdAsync(int id)
        {
            return await _context.Projects
                .AsNoTracking()
                .Where(p => p.Id == id)
                .Select(MapToDtoExpression)
                .FirstOrDefaultAsync();
        }

        private static readonly System.Linq.Expressions.Expression<Func<Project, ProjectResponseDto>> MapToDtoExpression =
            p => new ProjectResponseDto
            {
                Id = p.Id,
                ProjectName = p.ProjectName,
                Location = p.Location,
                Category = p.Category,
                CoverImageUrl = p.MediaFiles
                    .Where(m => m.IsCover)
                    .OrderBy(m => m.DisplayOrder)
                    .Select(m => m.MediaUrl)
                    .FirstOrDefault()
                    ?? p.MediaFiles
                        .OrderBy(m => m.DisplayOrder)
                        .Select(m => m.MediaUrl)
                        .FirstOrDefault(),
                Description = p.Description,
                StartingDate = p.StartingDate,
                ExpectedCompletionDate = p.ExpectedCompletionDate,
                Status = p.Status,
                CreatedAt = p.CreatedAt
            };

        private static ProjectResponseDto MapToResponse(Project project)
        {
            return new ProjectResponseDto
            {
                Id = project.Id,
                ProjectName = project.ProjectName,
                Location = project.Location,
                Category = project.Category,
                Description = project.Description,
                StartingDate = project.StartingDate,
                ExpectedCompletionDate = project.ExpectedCompletionDate,
                Status = project.Status,
                CreatedAt = project.CreatedAt
            };
        }
    }
}
