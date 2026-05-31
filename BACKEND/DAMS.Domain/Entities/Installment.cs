using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    public class Installment
    {
        public int Id { get; set; }

        public int BookingId { get; set; }

        public int SequenceNumber { get; set; }

        public DateTime DueDate { get; set; }

        public InstallmentType Type { get; set; } = InstallmentType.Regular;

        public decimal Amount { get; set; }

        public InstallmentStatus Status { get; set; } = InstallmentStatus.Pending;

        public DateTime? PaidAt { get; set; }

        public string? Notes { get; set; }

        public Booking Booking { get; set; } = null!;

        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    }
}
