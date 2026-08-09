using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    public class CommissionRule
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime EffectiveFrom { get; set; }
        public DateTime? EffectiveTo { get; set; }
        public int? PartnerId { get; set; }
        public string? PartnerType { get; set; }
        public int? ProjectId { get; set; }
        public string? UnitCategory { get; set; }
        public CustomerSource? BookingSource { get; set; }
        public int? BookingId { get; set; }
        public FinancialCalculationType CalculationType { get; set; }
        public decimal? PercentageRate { get; set; }
        public decimal? FixedAmount { get; set; }
        public FinancialCalculationBasis CalculationBasis { get; set; }
        public decimal? MinimumCommission { get; set; }
        public decimal? MaximumCommission { get; set; }
        public string? EligibilityCondition { get; set; }
        public CommissionEarningCondition EarningCondition { get; set; }
        public decimal? MinimumCollectionPercent { get; set; }
        public int Priority { get; set; }
        public bool RequiresApproval { get; set; } = true;
        public string? Notes { get; set; }
        public int? CreatedByUserId { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public byte[] RowVersion { get; set; } = [];

        public ThirdPartyPartner? Partner { get; set; }
        public Project? Project { get; set; }
        public Booking? Booking { get; set; }
        public ICollection<BookingCommission> Commissions { get; set; } = new List<BookingCommission>();
        public ICollection<CommissionRuleRevision> Revisions { get; set; } = new List<CommissionRuleRevision>();
    }
}
