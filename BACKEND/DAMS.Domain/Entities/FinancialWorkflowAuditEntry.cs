using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>Append-only audit history for partner, commission, payout, rebate, and evidence decisions.</summary>
    public class FinancialWorkflowAuditEntry
    {
        public long Id { get; set; }
        public int? PartnerId { get; set; }
        public int? CustomerId { get; set; }
        public int? BookingId { get; set; }
        public int? CommissionRuleId { get; set; }
        public int? CommissionId { get; set; }
        public int? PayoutId { get; set; }
        public int? RebateId { get; set; }
        public int? RebateDisbursementId { get; set; }
        public FinancialWorkflowAction Action { get; set; }
        public BookingCommissionStatus? PreviousCommissionStatus { get; set; }
        public BookingCommissionStatus? NewCommissionStatus { get; set; }
        public CustomerRebateStatus? PreviousRebateStatus { get; set; }
        public CustomerRebateStatus? NewRebateStatus { get; set; }
        public decimal? PreviousAmount { get; set; }
        public decimal? NewAmount { get; set; }
        public string? Reason { get; set; }
        public int? PerformedByUserId { get; set; }
        public string? PerformedByName { get; set; }
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

        public ThirdPartyPartner? Partner { get; set; }
        public Customer? Customer { get; set; }
        public Booking? Booking { get; set; }
        public CommissionRule? CommissionRule { get; set; }
        public BookingCommission? Commission { get; set; }
        public CommissionPayout? Payout { get; set; }
        public CustomerRebate? Rebate { get; set; }
        public RebateDisbursement? RebateDisbursement { get; set; }
    }
}
