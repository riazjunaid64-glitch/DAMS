using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    public class CommissionPayout
    {
        public int Id { get; set; }
        public int CommissionId { get; set; }
        public int FinanceAccountId { get; set; }
        public decimal Amount { get; set; }
        public DateTime PaymentDate { get; set; }
        public PaymentMethod PaymentMethod { get; set; }
        public string? PaymentReference { get; set; }
        public string? DestinationBankNameSnapshot { get; set; }
        public string? DestinationAccountTitleSnapshot { get; set; }
        public string? DestinationAccountNumberSnapshot { get; set; }
        public string? DestinationIbanSnapshot { get; set; }
        public string IdempotencyKey { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public int? RecordedByUserId { get; set; }
        public string? RecordedByName { get; set; }
        public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
        public byte[] RowVersion { get; set; } = [];

        public BookingCommission Commission { get; set; } = null!;
        public FinanceAccount FinanceAccount { get; set; } = null!;
        public ICollection<CommissionPayoutReversal> Reversals { get; set; } = new List<CommissionPayoutReversal>();
        public ICollection<FinancialEvidence> Evidence { get; set; } = new List<FinancialEvidence>();
    }

    public class CommissionPayoutReversal
    {
        public int Id { get; set; }
        public int PayoutId { get; set; }
        public decimal Amount { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string IdempotencyKey { get; set; } = string.Empty;
        public int? ReversedByUserId { get; set; }
        public string? ReversedByName { get; set; }
        public DateTime ReversedAt { get; set; } = DateTime.UtcNow;

        public CommissionPayout Payout { get; set; } = null!;
    }
}
