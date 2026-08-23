using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    public class CustomerRebate
    {
        public int Id { get; set; }
        public int BookingId { get; set; }
        public int CustomerId { get; set; }
        public FinancialCalculationType CalculationType { get; set; }
        public decimal? PercentageRate { get; set; }
        public decimal? FixedAmount { get; set; }
        public FinancialCalculationBasis CalculationBasis { get; set; }
        public decimal BasisAmount { get; set; }
        public decimal CalculatedAmount { get; set; }
        public decimal AdjustmentAmount { get; set; }
        public string? AdjustmentReason { get; set; }
        public decimal FinalAmount { get; set; }
        public string Reason { get; set; } = string.Empty;
        public CustomerRebateMethod Method { get; set; }
        public CustomerRebateStatus Status { get; set; } = CustomerRebateStatus.Pending;
        public string? Notes { get; set; }
        public int? CreatedByUserId { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? CancellationOrReversalReason { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public byte[] RowVersion { get; set; } = [];

        public Booking Booking { get; set; } = null!;
        public Customer Customer { get; set; } = null!;
        public ICollection<RebateDisbursement> Disbursements { get; set; } = new List<RebateDisbursement>();
        public ICollection<FinancialEvidence> Evidence { get; set; } = new List<FinancialEvidence>();
    }

    public class RebateDisbursement
    {
        public int Id { get; set; }
        public int RebateId { get; set; }
        public int? FinanceAccountId { get; set; }
        public int? InstallmentId { get; set; }
        public CustomerRebateMethod Method { get; set; }
        public decimal Amount { get; set; }
        public DateTime AppliedAt { get; set; }
        public PaymentMethod? PaymentMethod { get; set; }
        public string? Reference { get; set; }
        public string IdempotencyKey { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public int? RecordedByUserId { get; set; }
        public string? RecordedByName { get; set; }
        public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
        public byte[] RowVersion { get; set; } = [];

        public CustomerRebate Rebate { get; set; } = null!;
        public FinanceAccount? FinanceAccount { get; set; }
        public Installment? Installment { get; set; }
        public ICollection<RebateDisbursementReversal> Reversals { get; set; } = new List<RebateDisbursementReversal>();
        public ICollection<FinancialEvidence> Evidence { get; set; } = new List<FinancialEvidence>();
    }

    public class RebateDisbursementReversal
    {
        public int Id { get; set; }
        public int DisbursementId { get; set; }
        public decimal Amount { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string IdempotencyKey { get; set; } = string.Empty;
        public int? ReversedByUserId { get; set; }
        public string? ReversedByName { get; set; }
        public DateTime ReversedAt { get; set; } = DateTime.UtcNow;

        public RebateDisbursement Disbursement { get; set; } = null!;
    }
}
