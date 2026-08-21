namespace DAMS.Application.DTOs.FinanceDtos
{
    public class ManualRevenueResponseDto
    {
        public int Id { get; set; }

        public int? ProjectId { get; set; }

        public string? ProjectName { get; set; }

        public int? FinanceAccountId { get; set; }

        public string? FinanceAccountName { get; set; }

        public string? AccountHolderName { get; set; }

        public decimal Amount { get; set; }

        public string RevenueType { get; set; } = string.Empty;

        public int? RevenueCategoryId { get; set; }

        public string? Description { get; set; }

        public string? Reference { get; set; }

        public DateTime Date { get; set; }

        public DateTime CreatedAt { get; set; }

        public FinanceAttachmentDto? Attachment { get; set; }

        /// <summary>Base64 row version — send it back on the next update or delete.</summary>
        public string ConcurrencyToken { get; set; } = string.Empty;
    }
}
