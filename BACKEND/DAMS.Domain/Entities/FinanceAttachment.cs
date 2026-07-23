namespace DAMS.Domain.Entities
{
    /// <summary>
    /// Private supporting evidence attached to exactly one manual revenue or expense record.
    /// File bytes are stored outside the web root; this entity stores only safe metadata.
    /// </summary>
    public class FinanceAttachment
    {
        public int Id { get; set; }

        public int? ManualRevenueId { get; set; }

        public int? ExpenseId { get; set; }

        public string StoredFileName { get; set; } = string.Empty;

        public string OriginalFileName { get; set; } = string.Empty;

        public string ContentType { get; set; } = string.Empty;

        public long FileSize { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        public ManualRevenue? ManualRevenue { get; set; }

        public Expense? Expense { get; set; }
    }
}
