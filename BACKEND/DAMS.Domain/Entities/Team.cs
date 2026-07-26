namespace DAMS.Domain.Entities
{
    /// <summary>
    /// A sales team. Used for team-level lead assignment and to scope what a manager
    /// is allowed to see.
    /// </summary>
    public class Team
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        // The employee who manages this team. Their login sees every lead owned by the team.
        public int? ManagerEmployeeId { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public Employee? ManagerEmployee { get; set; }

        public ICollection<Employee> Members { get; set; } = new List<Employee>();
    }
}
