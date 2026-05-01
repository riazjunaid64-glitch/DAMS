using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    public class Installment
    {
        public int Id { get; set; }

        public int BookingId { get; set; }

        public DateTime DueDate { get; set; }

        public decimal Amount { get; set; }

        public InstallmentStatus Status { get; set; } = InstallmentStatus.Pending;

        public DateTime? PaidDate { get; set; }

        // Navigation
        public Booking Booking { get; set; } = null!;
    }
}