using DAMS.Application.DTOs.BookingDtos;
using DAMS.Domain.Entities;

namespace DAMS.Application.Interfaces
{
    public interface IBookingService
    {
        /// <summary>Admin creates a booking for a walk-in / phone customer.</summary>
        Task<BookingResponseDto> CreateBookingAsync(CreateBookingDto dto, int adminUserId);

        /// <summary>
        /// Creates a booking from an approved booking request. Called by the
        /// booking-request approval flow. Sets the unit to Reserved.
        /// </summary>
        Task<BookingResponseDto> CreateBookingForApprovedRequestAsync(BookingRequest request, int customerId, int adminUserId);

        Task<BookingResponseDto?> GetBookingByIdAsync(int id);

        Task<BookingListDto> GetBookingsAsync(BookingFilterDto filter);

        Task<BookingResponseDto> CancelBookingAsync(int id, string? reason, int adminUserId);

        /// <summary>
        /// Sets the negotiated terms (sale price, discount, booking amount required, due date)
        /// on a booking that is still AwaitingBookingAmount. Required before booking-amount
        /// payments can be recorded for request-derived bookings.
        /// </summary>
        Task<BookingResponseDto> UpdateBookingFinancialsAsync(int id, UpdateBookingFinancialsDto dto, int adminUserId);

        /// <summary>
        /// Records a (possibly partial) booking-amount payment. When the received total
        /// reaches the required amount, the booking moves to PaymentPlanActive and the
        /// unit moves to OnPaymentPlan.
        /// </summary>
        Task<BookingResponseDto> RecordBookingAmountPaymentAsync(int bookingId, RecordBookingAmountPaymentDto dto, int adminUserId);

        /// <summary>Marks possession as handed over on an active payment plan.</summary>
        Task<BookingResponseDto> GivePossessionAsync(int id, DateTime? possessionDate, int adminUserId);

        /// <summary>
        /// Completes the sale once the booking amount and every installment are fully
        /// paid. Moves the unit to Sold.
        /// </summary>
        Task<BookingResponseDto> CompleteSaleAsync(int id, int adminUserId);

        Task<List<BookingPaymentDto>> GetBookingPaymentsAsync(int bookingId);

        /// <summary>
        /// Builds a render-ready receipt payload for a single payment (read-only).
        /// </summary>
        Task<PaymentReceiptDto> GetPaymentReceiptAsync(int bookingId, int paymentId);

        Task<List<BookingResponseDto>> GetBookingsByCustomerEmailAsync(string email, int? userId = null);

        Task<BookingResponseDto?> GetBookingByIdForCustomerEmailAsync(int id, string email, int? userId = null);

        Task<bool> CustomerOwnsBookingByEmailAsync(int bookingId, string email, int? userId = null);
    }
}
