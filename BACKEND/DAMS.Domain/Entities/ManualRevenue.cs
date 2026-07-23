namespace DAMS.Domain.Entities
{
    /// <summary>
    /// Admin-entered income that is not generated from a booking payment
    /// (e.g. transfer charges, documentation charges, parking charges, other income).
    /// </summary>
    public class ManualRevenue
    {
        public int Id { get; set; }

        // Nullable: some income (e.g. general office income) is not tied to a single project.
        public int? ProjectId { get; set; }

        public decimal Amount { get; set; }

        // Free-text type with a suggested set on the UI (Transfer Charges, Documentation Charges, etc.).
        public string RevenueType { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? Reference { get; set; }

        public DateTime Date { get; set; } = DateTime.UtcNow;

        // Admin who recorded this revenue (no FK; resolved at read time like other audit fields).
        public int? CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public Project? Project { get; set; }

        public FinanceAttachment? Attachment { get; set; }
    }
}
