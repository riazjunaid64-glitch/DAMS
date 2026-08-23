using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    public class CapitalPartner
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Cnic { get; set; }
        public string? Ntn { get; set; }
        public decimal ProfitSharePercent { get; set; }
        public int? FinanceAccountId { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime? JoinedDate { get; set; }
        public DateTime? ExitedDate { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public byte[] RowVersion { get; set; } = [];

        public FinanceAccount? FinanceAccount { get; set; }
        public ICollection<CapitalTransaction> Transactions { get; set; } = new List<CapitalTransaction>();
    }

    public class CapitalTransaction
    {
        public int Id { get; set; }
        public int CapitalPartnerId { get; set; }
        public CapitalTransactionType Type { get; set; }
        public decimal Amount { get; set; }
        public DateTime Date { get; set; }
        public int? FinanceAccountId { get; set; }
        public string? Reference { get; set; }
        public string? Note { get; set; }
        public decimal? ProfitSharePercentSnapshot { get; set; }
        public int? RecordedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public CapitalPartner CapitalPartner { get; set; } = null!;
        public FinanceAccount? FinanceAccount { get; set; }

        /// <summary>Receipt for money a partner put in, or took out. See
        /// <see cref="LoanTransaction.Attachment"/> — the same reason.</summary>
        public FinanceAttachment? Attachment { get; set; }
    }
}
