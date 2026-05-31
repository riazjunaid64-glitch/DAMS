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
        /// Records a (possibly partial) booking-amount payment. When the received total
        /// reaches the required amount, the booking moves to PaymentPlanActive and the
        /// unit moves to OnPaymentPlan.
        /// </summary>
        Task<BookingResponseDto> RecordBookingAmountPaymentAsync(int bookingId, RecordBookingAmountPaymentDto dto, int adminUserId);

        Task<List<BookingPaymentDto>> GetBookingPaymentsAsync(int bookingId);
    }
}
