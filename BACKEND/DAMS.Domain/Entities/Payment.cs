using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    public class Payment
    {
        public int Id { get; set; }

        public int BookingId { get; set; }

        public int? InstallmentId { get; set; }

        // Distinguishes booking-amount payments from installment payments.
        public PaymentType Type { get; set; } = PaymentType.BookingAmount;

        public decimal Amount { get; set; }

        public PaymentMethod PaymentMethod { get; set; }

        public string? PaymentReference { get; set; }

        // Optional free-text note recorded by the admin against this payment.
        public string? Notes { get; set; }

        // Admin user who recorded the payment (audit).
        public int? RecordedByUserId { get; set; }

        public DateTime PaidAt { get; set; } = DateTime.UtcNow;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Booking Booking { get; set; } = null!;

        public Installment? Installment { get; set; }
    }
}
