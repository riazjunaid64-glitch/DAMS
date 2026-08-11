using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Domain.Enums;

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

        /// <summary>Gross cost of the purchase.</summary>
        public decimal Amount { get; set; }

        public string Category { get; set; } = string.Empty;

        public int? CategoryId { get; set; }

        public string? Description { get; set; }

        public string? Vendor { get; set; }

        public int? VendorId { get; set; }

        public DateTime Date { get; set; }

        public bool WhtApplied { get; set; }

        public decimal WhtRate { get; set; }

        public decimal WhtAmount { get; set; }

        /// <summary>Cash that left the account: gross − withheld.</summary>
        public decimal NetPaid { get; set; }

        public bool WhtRateOverridden { get; set; }

        public string? WhtOverrideReason { get; set; }

        public string? WhtTaxSection { get; set; }

        public FilerStatus VendorFilerStatusAtEntry { get; set; }

        public DateTime CreatedAt { get; set; }

        public FinanceAttachmentDto? Attachment { get; set; }
    }
}
