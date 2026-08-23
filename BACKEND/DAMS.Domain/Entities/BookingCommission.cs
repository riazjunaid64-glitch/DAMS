using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    public class BookingCommission
    {
        public int Id { get; set; }
        public int BookingId { get; set; }
        public int PartnerId { get; set; }
        public int? AttributionId { get; set; }
        public int? RuleId { get; set; }
        public int? RuleRevisionId { get; set; }
        public bool IsManual { get; set; }
        public string? ManualReason { get; set; }

        // Immutable calculation snapshot.
        public string PartnerNameSnapshot { get; set; } = string.Empty;
        public string PartnerTypeSnapshot { get; set; } = string.Empty;
        public string PartnerInternalCodeSnapshot { get; set; } = string.Empty;
        public decimal AllocationPercentSnapshot { get; set; } = 100m;
        public string? RuleNameSnapshot { get; set; }
        public int? RulePrioritySnapshot { get; set; }
        public decimal? MinimumCommissionSnapshot { get; set; }
        public decimal? MaximumCommissionSnapshot { get; set; }
        public FinancialCalculationType CalculationType { get; set; }
        public decimal? PercentageRate { get; set; }
        public decimal? FixedAmount { get; set; }
        public FinancialCalculationBasis CalculationBasis { get; set; }
        public decimal BasisAmount { get; set; }
        public decimal CalculatedAmount { get; set; }
        public decimal AdjustmentAmount { get; set; }
        public string? AdjustmentReason { get; set; }
        public decimal FinalAmount { get; set; }

        public BookingCommissionStatus Status { get; set; } = BookingCommissionStatus.Pending;
        public int? CreatedByUserId { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? CancellationOrReversalReason { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public byte[] RowVersion { get; set; } = [];

        public Booking Booking { get; set; } = null!;
        public ThirdPartyPartner Partner { get; set; } = null!;
        public ThirdPartyAttribution? Attribution { get; set; }
        public CommissionRule? Rule { get; set; }
        public CommissionRuleRevision? RuleRevision { get; set; }
        public ICollection<CommissionPayout> Payouts { get; set; } = new List<CommissionPayout>();
        public ICollection<FinancialEvidence> Evidence { get; set; } = new List<FinancialEvidence>();
    }
}
