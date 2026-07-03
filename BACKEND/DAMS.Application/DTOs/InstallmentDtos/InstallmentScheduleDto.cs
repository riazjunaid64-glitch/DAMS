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

        public List<InstallmentScheduleItemDto> Items { get; set; } = new();
    }
}
