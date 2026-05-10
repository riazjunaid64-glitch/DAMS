using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    public class Payment
    {
        public int Id { get; set; }

        public int BookingId { get; set; }

        public int? InstallmentId { get; set; }

        public decimal Amount { get; set; }

        public PaymentMethod PaymentMethod { get; set; }

        public string? PaymentReference { get; set; }

        public DateTime PaidAt { get; set; } = DateTime.UtcNow;

        public Booking Booking { get; set; } = null!;

        public Installment? Installment { get; set; }
    }
}
