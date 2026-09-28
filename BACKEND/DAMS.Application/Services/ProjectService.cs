using DAMS.Application.Common;
using DAMS.Application.DTOs.ProjectDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
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
        private static string AllProjectsCacheKey => $"projects:all:{ProjectListCache.Version}";

        public ProjectService(AppDbContext context, IMemoryCache cache)
        {
            _context = context;
            _cache = cache;
        }

        public async Task<ProjectResponseDto> CreateProjectAsync(CreateProjectDto dto, int adminId)
        {
            var name = RequireText(dto.ProjectName, "Project name is required.");
            var location = RequireText(dto.Location, "Location is required.");
            RequireDates(dto.StartingDate, dto.ExpectedCompletionDate);

            if (await _context.Projects.AnyAsync(p => p.ProjectName == name))
                throw new BusinessRuleException("Project name already exists.");

            var project = new Project
            {
                ProjectName = name,
                Location = location,
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

            var name = RequireText(dto.ProjectName, "Project name is required.");
            var location = RequireText(dto.Location, "Location is required.");
            RequireDates(dto.StartingDate, dto.ExpectedCompletionDate);

            if (await _context.Projects.AnyAsync(p => p.Id != id && p.ProjectName == name))
                throw new BusinessRuleException("Project name already exists.");

            project.ProjectName = name;
            project.Location = location;
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
                CreatedAt = p.CreatedAt,
                TotalUnits = p.Units.Count(),
                AvailableUnits = p.Units.Count(u => u.Status == UnitStatus.Available),
                BookedUnits = p.Units.Count(u =>
                    u.Status == UnitStatus.PendingReview
                    || u.Status == UnitStatus.Booked
                    || u.Status == UnitStatus.Reserved
                    || u.Status == UnitStatus.OnPaymentPlan),
                SoldUnits = p.Units.Count(u => u.Status == UnitStatus.Sold)
            };

        private static string RequireText(string? value, string message)
        {
            var trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
                throw new BusinessRuleException(message);
            return trimmed;
        }

        // The form compares calendar days. A completion on the start date is allowed;
        // a completion with no start date is allowed.
        private static void RequireDates(DateTime? start, DateTime? completion)
        {
            if (start.HasValue && completion.HasValue && completion.Value.Date < start.Value.Date)
                throw new BusinessRuleException("Completion date can't be before the start date.");
        }

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
