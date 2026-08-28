using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.FinanceDtos
{
    public sealed class SaveLoanDto
    {
        public string Name { get; set; } = string.Empty;
        public string? LenderName { get; set; }
        public int FinanceAccountId { get; set; }
        public bool IsActive { get; set; } = true;
        public string ConcurrencyToken { get; set; } = string.Empty;
    }

    public sealed class LoanDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? LenderName { get; set; }
        public int FinanceAccountId { get; set; }
        public string FinanceAccountName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public decimal OpeningBalance { get; set; }
        public decimal DrawnPrincipal { get; set; }
        public decimal RepaidPrincipal { get; set; }
        public decimal InterestPaid { get; set; }
        public decimal CurrentBalance { get; set; }
        public int TransactionCount { get; set; }
        public string ConcurrencyToken { get; set; } = string.Empty;
    }

    public sealed class LoanAccountOptionDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string AccountHolderName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int? LinkedLoanId { get; set; }
        public string? LinkedLoanName { get; set; }
    }

    public sealed class SaveLoanTransactionDto
    {
        public LoanTransactionType Type { get; set; }
        public decimal PrincipalAmount { get; set; }
        public decimal InterestAmount { get; set; }
        public DateTime Date { get; set; }
        public int FinanceAccountId { get; set; }
        public string? Reference { get; set; }
        public string? Note { get; set; }

        /// <summary>
        /// The row version of the movement being corrected, and absent when one is being recorded.
        /// </summary>
        /// <remarks>
        /// Nullable deliberately, and for the same reason as
        /// <see cref="SaveStaffCashTransferDto.ConcurrencyToken"/>. Both routes are bound from a
        /// multipart form so the bank slip travels with the figures, and form binding turns an empty
        /// field into null; a non-nullable string is implicitly required, so every NEW drawdown and
        /// repayment was rejected as "The ConcurrencyToken field is required." before it ever reached
        /// the service. Presence was never the check that matters: <c>ApplyToken</c> compares the token
        /// against the stored row version and refuses a blank one for a row that has one, which is the
        /// guard that counts.
        /// </remarks>
        public string? ConcurrencyToken { get; set; }
    }

    public sealed class LoanTransactionDto
    {
        public int Id { get; set; }
        public int LoanId { get; set; }
        public LoanTransactionType Type { get; set; }
        public decimal PrincipalAmount { get; set; }
        public decimal InterestAmount { get; set; }
        public decimal TotalCashMovement { get; set; }
        public DateTime Date { get; set; }
        public int FinanceAccountId { get; set; }
        public string FinanceAccountName { get; set; } = string.Empty;
        public string? Reference { get; set; }
        public string? Note { get; set; }
        public decimal RunningBalance { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string ConcurrencyToken { get; set; } = string.Empty;

        /// <summary>The bank slip or lender statement behind this movement, when one was attached.</summary>
        public FinanceAttachmentDto? Attachment { get; set; }
    }

    public sealed class LoanStatementDto
    {
        public LoanDto Loan { get; set; } = new();
        public List<LoanTransactionDto> Items { get; set; } = new();
        public bool HasMore { get; set; }
    }
}
