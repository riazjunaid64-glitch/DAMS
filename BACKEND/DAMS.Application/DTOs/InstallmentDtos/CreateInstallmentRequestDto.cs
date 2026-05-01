namespace DAMS.Application.DTOs.InstallmentDtos
{
    public class CreateInstallmentRequestDto
    {
        /// <summary>
        /// Booking ID this installment belongs to
        /// </summary>
        public int BookingId { get; set; }

        /// <summary>
        /// Due date - must be in the future
        /// </summary>
        public DateTime DueDate { get; set; }

        /// <summary>
        /// Installment amount - must be > 0
        /// Total of all installments must equal booking.RemainingAmount
        /// </summary>
        public decimal Amount { get; set; }
    }
}
