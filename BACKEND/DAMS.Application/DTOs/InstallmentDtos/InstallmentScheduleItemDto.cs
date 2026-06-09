using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.InstallmentDtos
{
    public class InstallmentScheduleItemDto
    {
        public int Id { get; set; }

        public int SequenceNumber { get; set; }

        public InstallmentType Type { get; set; }

        public DateTime DueDate { get; set; }

        public decimal Amount { get; set; }

        public InstallmentStatus Status { get; set; }

        public decimal AmountPaid { get; set; }

        public decimal RemainingBalance { get; set; }

        public bool IsOverdue { get; set; }

        public DateTime? PaidAt { get; set; }

        public string? Notes { get; set; }
    }
}
