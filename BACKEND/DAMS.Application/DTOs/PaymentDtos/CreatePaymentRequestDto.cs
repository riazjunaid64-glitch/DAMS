using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.PaymentDtos
{
    public class CreatePaymentRequestDto
    {
        /// <summary>
        /// Booking ID this payment belongs to
        /// </summary>
        public int BookingId { get; set; }

        /// <summary>
        /// Installment ID - required for installment payments, null for down payment
        /// </summary>
        public int? InstallmentId { get; set; }

        /// <summary>
        /// Payment amount - must be > 0
        /// </summary>
        public decimal Amount { get; set; }

        /// <summary>
        /// Payment method (Cash, BankTransfer, Cheque, Online)
        /// </summary>
        public PaymentMethod PaymentMethod { get; set; }

        /// <summary>
        /// Optional payment reference (transaction ID, cheque number, etc.)
        /// </summary>
        public string? PaymentReference { get; set; }
    }
}
