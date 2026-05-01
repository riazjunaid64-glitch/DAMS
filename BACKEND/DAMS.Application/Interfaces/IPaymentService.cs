using DAMS.Application.DTOs.PaymentDtos;

namespace DAMS.Application.Interfaces
{
    public interface IPaymentService
    {
        /// <summary>
        /// Process payment with validation:
        /// - Amount > 0
        /// - Total payments must not exceed total booking price
        /// - InstallmentId required for installment payments (null for down payment)
        /// - Installments marked as Paid with PaidDate when fully paid
        /// - Booking marked as Completed if total paid == total price
        /// </summary>
        Task<PaymentResponseDto> ProcessPaymentAsync(CreatePaymentRequestDto request);

        /// <summary>
        /// Get all payments for a booking (excluding soft-deleted)
        /// </summary>
        Task<List<PaymentResponseDto>> GetBookingPaymentsAsync(int bookingId);

        /// <summary>
        /// Get payment by ID
        /// </summary>
        Task<PaymentResponseDto?> GetPaymentByIdAsync(int paymentId);

        /// <summary>
        /// Calculate total amount paid for a booking
        /// </summary>
        Task<decimal> GetTotalPaidAsync(int bookingId);

        /// <summary>
        /// Validate payment doesn't exceed booking total
        /// </summary>
        Task<bool> ValidatePaymentAmountAsync(int bookingId, decimal paymentAmount);

        /// <summary>
        /// Get down payments for a booking (payments with null InstallmentId)
        /// </summary>
        Task<List<PaymentResponseDto>> GetDownPaymentsAsync(int bookingId);
    }
}
