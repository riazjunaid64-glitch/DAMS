using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.InstallmentDtos
{
    public class InstallmentResponseDto
    {
        public int Id { get; set; }

        public int BookingId { get; set; }

        public DateTime DueDate { get; set; }

        public decimal Amount { get; set; }

        public InstallmentStatus Status { get; set; }

        public DateTime? PaidDate { get; set; }

        /// <summary>
        /// Is this installment overdue? (Status != Paid && DueDate < today)
        /// </summary>
        public bool IsOverdue { get; set; }
    }
}
