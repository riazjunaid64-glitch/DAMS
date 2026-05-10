namespace DAMS.Domain.Entities
{
    public class Expense
    {
        public int Id { get; set; }

        public int ProjectId { get; set; }

        public decimal Amount { get; set; }

        public string Category { get; set; } = string.Empty;

        public string? Description { get; set; }

        public DateTime Date { get; set; } = DateTime.UtcNow;

        // Navigation
        public Project Project { get; set; } = null!;
    }
}