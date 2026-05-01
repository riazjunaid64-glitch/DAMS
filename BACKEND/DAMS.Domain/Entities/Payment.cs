using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    public class Payment
    {
        public int Id { get; set; }

        public int BookingId { get; set; }

        public int? InstallmentId { get; set; } // NULL = Down Payment

        public decimal Amount { get; set; }

        public DateTime PaymentDate { get; set; } = DateTime.UtcNow;

        public PaymentMethod PaymentMethod { get; set; }

        // Navigation
        public Booking Booking { get; set; } = null!;
        public Installment? Installment { get; set; }
    }
}