namespace DAMS.Domain.Entities
{
    /// <summary>
    /// Lead apartment types the canonical-list migration could not map. Kept in a table,
    /// with the lead id and the value as it was, because EF does not show SQL PRINT
    /// output from a database update. The value on the lead is left unchanged.
    /// </summary>
    public class LeadApartmentTypeMigrationNote
    {
        public int Id { get; set; }

        public int LeadId { get; set; }

        public string PropertyType { get; set; } = string.Empty;

        public DateTime NotedAt { get; set; }
    }
}
