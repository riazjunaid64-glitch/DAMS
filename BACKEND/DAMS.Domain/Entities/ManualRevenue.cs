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

        // Nullable only for records created before finance accounts were introduced.
        public int? FinanceAccountId { get; set; }

        public decimal Amount { get; set; }

        // Legacy free-text column retained during the production backfill. New writes snapshot the
        // managed category name into both fields so older clients continue to round-trip safely.
        public string RevenueType { get; set; } = string.Empty;

        public int? RevenueCategoryId { get; set; }

        public string RevenueTypeName { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? Reference { get; set; }

        public DateTime Date { get; set; } = DateTime.UtcNow;

        // Admin who recorded this revenue (no FK; resolved at read time like other audit fields).
        public int? CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Optimistic-concurrency token — the counterpart of <c>Expense.RowVersion</c>. Editing a
        /// revenue row moves income and a bank balance, so a lost update here misstates both.
        /// </summary>
        public byte[] RowVersion { get; set; } = [];

        // Navigation
        public Project? Project { get; set; }

        public FinanceAttachment? Attachment { get; set; }

        public FinanceAccount? FinanceAccount { get; set; }

        public RevenueCategory? RevenueCategory { get; set; }
    }
}
