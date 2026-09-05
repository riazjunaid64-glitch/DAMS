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
        public ICollection<RebateCreditAllocation> Allocations { get; set; } = new List<RebateCreditAllocation>();
        public ICollection<FinancialEvidence> Evidence { get; set; } = new List<FinancialEvidence>();
    }

    /// <summary>
    /// Where a booking-level customer credit lands on the installment schedule.
    /// <para>
    /// A balance reduction or credit note is entered against the booking, not against any one
    /// installment, but the schedule is the only route DAMS has for collecting what the customer
    /// owes. Without an allocation the plan keeps demanding the full price while the balance says
    /// something smaller: the last installment can never be settled, the sale can never be
    /// completed, and Overdue over-states the debt by exactly the rebate.
    /// </para>
    /// <para>
    /// This is bookkeeping, not a financial record — the money lives in the
    /// <see cref="RebateDisbursement"/> and its reversals, and these rows are rebuilt from those by
    /// <c>BookingCreditPolicy.Allocate</c> whenever anything moves. So they can never total more
    /// than the credit they come from, and never double-count it: allocations are read only by the
    /// per-installment views, never by the booking-level credit total.
    /// </para>
    /// </summary>
    public class RebateCreditAllocation
    {
        public int Id { get; set; }
        public int DisbursementId { get; set; }
        public int InstallmentId { get; set; }
        public decimal Amount { get; set; }

        public RebateDisbursement Disbursement { get; set; } = null!;
        public Installment Installment { get; set; } = null!;
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
