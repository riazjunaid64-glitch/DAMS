using DAMS.Application.DTOs.FinanceDtos;

namespace DAMS.Application.DTOs.ExpenseDtos
{
    public class ExpenseResponseDto
    {
        public int Id { get; set; }

        public int? ProjectId { get; set; }

        public string? ProjectName { get; set; }

        public int? FinanceAccountId { get; set; }

        public string? FinanceAccountName { get; set; }

        public string? AccountHolderName { get; set; }

        public decimal Amount { get; set; }

        public string Category { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? Vendor { get; set; }

        public DateTime Date { get; set; }

        public DateTime CreatedAt { get; set; }

        public FinanceAttachmentDto? Attachment { get; set; }
    }
}
