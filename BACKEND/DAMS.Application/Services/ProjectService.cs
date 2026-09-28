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
        private const int LowestFloor = -10;
        private const int HighestFloor = 200;
        private const int MaxFloors = 200;
        private const int MaxFloorNameLength = 40;

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
                throw new MissingRecordException("Project not found.");

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
            var project = await _context.Projects
                .AsNoTracking()
                .Where(p => p.Id == id)
                .Select(MapToDtoExpression)
                .FirstOrDefaultAsync();

            if (project == null)
                return null;

            // The unit popups pick a floor from this list, so the detail carries it and they need no second call.
            project.Floors = await _context.ProjectFloors
                .AsNoTracking()
                .Where(f => f.ProjectId == id)
                .OrderBy(f => f.Number)
                .Select(f => new ProjectFloorDto { Number = f.Number, Name = f.Name })
                .ToListAsync();

            return project;
        }

        public async Task<List<ProjectFloorResponseDto>?> GetFloorsAsync(int projectId)
        {
            if (!await _context.Projects.AnyAsync(p => p.Id == projectId))
                return null;

            return await FloorsWithUnitCounts(projectId);
        }

        public async Task<List<ProjectFloorResponseDto>> ReplaceFloorsAsync(int projectId, List<ProjectFloorDto>? floors)
        {
            if (!await _context.Projects.AnyAsync(p => p.Id == projectId))
                throw new MissingRecordException("Project not found.");

            var wanted = RequireFloorList(floors);

            var existing = await _context.ProjectFloors
                .Where(f => f.ProjectId == projectId)
                .ToListAsync();

            // A unit points at its floor by number, so every number that has units must survive the
            // save — renaming that floor is fine, removing or renumbering it would orphan the units.
            var unitsByFloor = await _context.Units
                .Where(u => u.ProjectId == projectId)
                .GroupBy(u => u.FloorNumber)
                .Select(g => new { Number = g.Key, Count = g.Count() })
                .ToListAsync();

            var keptNumbers = wanted.Select(f => f.Number).ToHashSet();
            var orphaned = unitsByFloor
                .Where(f => !keptNumbers.Contains(f.Number))
                .OrderBy(f => f.Number)
                .FirstOrDefault();
            if (orphaned != null)
            {
                var name = existing.FirstOrDefault(f => f.Number == orphaned.Number)?.Name
                    ?? ProjectFloorNames.Standard(orphaned.Number);
                var units = orphaned.Count == 1 ? "1 unit" : $"{orphaned.Count} units";
                throw new BusinessRuleException($"{name} has {units}, so its number can't change. Move the units first.");
            }

            // Delete-then-insert in one save: EF orders the deletes first, so a name or number moving
            // between floors never trips the unique indexes, and a failure leaves the old list intact.
            _context.ProjectFloors.RemoveRange(existing);
            _context.ProjectFloors.AddRange(wanted.Select(f => new ProjectFloor
            {
                ProjectId = projectId,
                Number = f.Number,
                Name = f.Name
            }));

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another save replaced the list between our read and our delete.
                throw new BusinessRuleException("The floor list was changed by someone else. Reload it and try again.");
            }

            _cache.Remove(AllProjectsCacheKey);

            return await FloorsWithUnitCounts(projectId);
        }

        private Task<List<ProjectFloorResponseDto>> FloorsWithUnitCounts(int projectId) =>
            _context.ProjectFloors
                .AsNoTracking()
                .Where(f => f.ProjectId == projectId)
                .OrderBy(f => f.Number)
                .Select(f => new ProjectFloorResponseDto
                {
                    Number = f.Number,
                    Name = f.Name,
                    UnitCount = _context.Units.Count(u => u.ProjectId == f.ProjectId && u.FloorNumber == f.Number)
                })
                .ToListAsync();

        // Rules are checked before anything is written, so one bad row saves nothing.
        private static List<ProjectFloorDto> RequireFloorList(List<ProjectFloorDto>? floors)
        {
            if (floors == null || floors.Count == 0)
                throw new BusinessRuleException("Add at least one floor.");
            if (floors.Count > MaxFloors)
                throw new BusinessRuleException($"A project can have at most {MaxFloors} floors.");

            var cleaned = new List<ProjectFloorDto>(floors.Count);
            var numbers = new HashSet<int>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var floor in floors)
            {
                if (floor == null)
                    throw new BusinessRuleException("Every floor needs a name.");

                var name = (floor.Name ?? string.Empty).Trim();
                if (name.Length == 0)
                    throw new BusinessRuleException("Every floor needs a name.");
                if (name.Length > MaxFloorNameLength)
                    throw new BusinessRuleException($"A floor name can be at most {MaxFloorNameLength} characters.");
                if (floor.Number < LowestFloor || floor.Number > HighestFloor)
                    throw new BusinessRuleException($"Floor numbers go from {LowestFloor} to {HighestFloor}.");
                if (!numbers.Add(floor.Number))
                    throw new BusinessRuleException($"Floor number {floor.Number} is used twice.");
                if (!names.Add(name))
                    throw new BusinessRuleException($"\"{name}\" is used for two floors.");

                cleaned.Add(new ProjectFloorDto { Number = floor.Number, Name = name });
            }

            return cleaned.OrderBy(f => f.Number).ToList();
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
                SoldUnits = p.Units.Count(u => u.Status == UnitStatus.Sold),
                FloorCount = p.Floors.Count()
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
