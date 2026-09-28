using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    /// <summary>
    /// The name a unit's floor is shown under: the project's own floor name for that number, or —
    /// while the project has no floor list, or the number is not on it — the standard label.
    /// Every screen, the receipt and the booking projections read it from here so the rule lives once.
    /// </summary>
    public sealed class ProjectFloorNames
    {
        private readonly Dictionary<(int ProjectId, int Number), string> _names;

        private ProjectFloorNames(Dictionary<(int ProjectId, int Number), string> names) => _names = names;

        public static async Task<ProjectFloorNames> LoadAsync(
            AppDbContext context, IEnumerable<int> projectIds, CancellationToken cancellationToken = default)
        {
            var ids = projectIds.Distinct().ToList();
            if (ids.Count == 0)
                return new ProjectFloorNames(new Dictionary<(int, int), string>());

            var floors = await context.ProjectFloors
                .AsNoTracking()
                .Where(f => ids.Contains(f.ProjectId))
                .Select(f => new { f.ProjectId, f.Number, f.Name })
                .ToListAsync(cancellationToken);

            return new ProjectFloorNames(floors.ToDictionary(f => (f.ProjectId, f.Number), f => f.Name));
        }

        public string For(int projectId, int floorNumber) =>
            _names.TryGetValue((projectId, floorNumber), out var name) ? name : Standard(floorNumber);

        /// <summary>"Basement 1" · "Ground floor" · "1st floor" · "8th floor" — the same text as the web app's floorLabel.</summary>
        public static string Standard(int floorNumber)
        {
            if (floorNumber == 0) return "Ground floor";
            if (floorNumber < 0) return $"Basement {Math.Abs(floorNumber)}";
            var mod100 = floorNumber % 100;
            var suffix = mod100 is >= 11 and <= 13
                ? "th"
                : (floorNumber % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
            return $"{floorNumber}{suffix} floor";
        }
    }
}
