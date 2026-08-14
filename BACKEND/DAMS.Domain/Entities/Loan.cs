using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>
    /// A real borrowing arrangement linked to the liability account that carries the amount owed.
    /// The link distinguishes loan liabilities from tax and trade payables without duplicating the
    /// chart of accounts.
    /// </summary>
    public sealed class Loan
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? LenderName { get; set; }
        public int FinanceAccountId { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public byte[] RowVersion { get; set; } = [];

        public FinanceAccount FinanceAccount { get; set; } = null!;
        public ICollection<LoanTransaction> Transactions { get; set; } = new List<LoanTransaction>();
    }

    /// <summary>
    /// One actual movement on a loan. Drawdowns increase cash and principal owed. Repayments reduce
    /// cash by <see cref="TotalPaid"/>, but only <see cref="PrincipalAmount"/> reduces the loan;
    /// <see cref="InterestAmount"/> is a period cost.
    /// </summary>
    public sealed class LoanTransaction
    {
        public int Id { get; set; }
        /// <summary>Server-generated idempotency key retained across EF execution-strategy retries.</summary>
        public Guid OperationId { get; set; } = Guid.NewGuid();
        public int LoanId { get; set; }
        public LoanTransactionType Type { get; set; }
        public decimal PrincipalAmount { get; set; }
        public decimal InterestAmount { get; set; }
        public decimal TotalPaid => Type == LoanTransactionType.Drawdown
            ? PrincipalAmount
            : PrincipalAmount + InterestAmount;
        public DateTime Date { get; set; }
        public int FinanceAccountId { get; set; }
        public string? Reference { get; set; }
        public string? Note { get; set; }
        public int? CreatedByUserId { get; set; }
        public int? UpdatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public byte[] RowVersion { get; set; } = [];

        public Loan Loan { get; set; } = null!;
        public FinanceAccount FinanceAccount { get; set; } = null!;
    }
}
