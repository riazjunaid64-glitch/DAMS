namespace DAMS.Domain.Entities
{
    /// <summary>
    /// One floor of a project's building. <see cref="Number"/> is its position (basements negative,
    /// ground 0) and is what <see cref="Unit.FloorNumber"/> points at; <see cref="Name"/> is what
    /// people see ("Parking", "Ground floor", "Rooftop").
    /// </summary>
    public class ProjectFloor
    {
        public int Id { get; set; }

        public int ProjectId { get; set; }

        public int Number { get; set; }

        public string Name { get; set; } = string.Empty;

        // Navigation
        public Project Project { get; set; } = null!;
    }
}
