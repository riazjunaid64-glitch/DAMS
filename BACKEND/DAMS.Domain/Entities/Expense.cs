namespace DAMS.Domain.Entities
{
    public class Expense
    {
        public int Id { get; set; }

        // Nullable: company-wide costs (salary, marketing, office) need not be tied to a project.
        public int? ProjectId { get; set; }

        // Nullable for legacy and system-generated expenses that predate account assignment.
        public int? FinanceAccountId { get; set; }

        public decimal Amount { get; set; }

        public string Category { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? Vendor { get; set; }

        public DateTime Date { get; set; } = DateTime.UtcNow;

        // Admin who recorded this expense (no FK; resolved at read time like other audit fields).
        public int? CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public Project? Project { get; set; }

        public FinanceAttachment? Attachment { get; set; }

        public FinanceAccount? FinanceAccount { get; set; }
    }
}
