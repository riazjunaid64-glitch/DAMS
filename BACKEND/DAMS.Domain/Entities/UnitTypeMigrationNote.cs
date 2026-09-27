namespace DAMS.Domain.Entities
{
    /// <summary>
    /// Unit types the canonical-list migration could not map. Kept in a table because
    /// EF does not show SQL PRINT output from a database update.
    /// </summary>
    public class UnitTypeMigrationNote
    {
        public int Id { get; set; }

        public string UnitType { get; set; } = string.Empty;

        public DateTime NotedAt { get; set; }
    }
}
