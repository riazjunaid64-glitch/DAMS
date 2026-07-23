namespace DAMS.Application.DTOs.FinanceDtos
{
    public class ManualRevenueResponseDto
    {
        public int Id { get; set; }

        public int? ProjectId { get; set; }

        public string? ProjectName { get; set; }

        public decimal Amount { get; set; }

        public string RevenueType { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? Reference { get; set; }

        public DateTime Date { get; set; }

        public DateTime CreatedAt { get; set; }

        public FinanceAttachmentDto? Attachment { get; set; }
    }
}
