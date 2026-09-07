using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.InstallmentDtos
{
    public class InstallmentScheduleDto
    {
        public int BookingId { get; set; }

        public string BookingReference { get; set; } = string.Empty;

        public BookingStatus BookingStatus { get; set; }

        public decimal AgreedSalePrice { get; set; }

        public decimal DiscountAmount { get; set; }

        public decimal DiscountPercent { get; set; }

        public decimal BookingAmountReceived { get; set; }

        public decimal PossessionAmount { get; set; }

        /// <summary>Amount allocated across regular installments (after booking amount and possession).</summary>
        public decimal InstallmentPool { get; set; }

        public InstallmentFrequency? Frequency { get; set; }

        public int? NumberOfInstallments { get; set; }

        public DateTime? InstallmentStartDate { get; set; }

        public DateTime? PossessionDueDate { get; set; }

        public DateTime? GeneratedAt { get; set; }

        public bool HasSchedule { get; set; }

        public bool CanGenerate { get; set; }

        public bool CanRegenerate { get; set; }

        public decimal ScheduleTotal { get; set; }

        public decimal SchedulePaid { get; set; }

        public decimal ScheduleRemaining { get; set; }

        /// <summary>
        /// What the customer owes that this schedule does NOT demand — normally zero. It becomes
        /// positive when a credit the plan was built smaller by is reversed, and the plan has to be
        /// regenerated before any further receipt can be taken (the payment service refuses one
        /// while this is non-zero, because that receipt would pin the plan and strand the amount).
        /// </summary>
        public decimal UnscheduledBalance { get; set; }

        public List<InstallmentScheduleItemDto> Items { get; set; } = new();
    }
}
