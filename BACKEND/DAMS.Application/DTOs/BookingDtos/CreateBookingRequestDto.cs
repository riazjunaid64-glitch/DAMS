using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.BookingDtos
{
    public class CreateBookingRequestDto
    {
        /// <summary>
        /// Client User ID - must be a valid user with Client role
        /// </summary>
        public int ClientId { get; set; }

        /// <summary>
        /// Unit ID - unit must be Available
        /// </summary>
        public int UnitId { get; set; }

        /// <summary>
        /// Down payment amount - must be > 0 and <= total unit price
        /// </summary>
        public decimal DownPayment { get; set; }

        /// <summary>
        /// Payment method for the down payment
        /// </summary>
        public PaymentMethod DownPaymentMethod { get; set; } = PaymentMethod.Cash;

        /// <summary>
        /// Optional payment reference for the down payment
        /// </summary>
        public string? PaymentReference { get; set; }
    }
} 
