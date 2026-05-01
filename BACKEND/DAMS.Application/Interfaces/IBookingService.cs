using DAMS.Application.DTOs.BookingDtos;

namespace DAMS.Application.Interfaces
{
    public interface IBookingService
    {
        /// <summary>
        /// Create a new booking with validation:
        /// - Unit must be Available
        /// - Only one active booking allowed per unit
        /// - Down payment > 0 and <= unit price
        /// - Unit status updated to Reserved
        /// </summary>
        Task<BookingResponseDto> CreateBookingAsync(CreateBookingRequestDto request);

        /// <summary>
        /// Get booking by ID with full details (unit, client, installments, payments)
        /// </summary>
        Task<BookingResponseDto?> GetBookingByIdAsync(int bookingId);

        /// <summary>
        /// Get all bookings for a client (non-deleted)
        /// </summary>
        Task<List<BookingResponseDto>> GetClientBookingsAsync(int clientId);

        /// <summary>
        /// Cancel booking and make unit Available again
        /// (Optional) Cannot cancel if fully paid
        /// </summary>
        Task<bool> CancelBookingAsync(int bookingId, string? reason = null);

        /// <summary>
        /// Mark booking as Completed if total paid == total price
        /// Called after each payment is processed
        /// </summary>
        Task<bool> CompleteBookingIfPaidAsync(int bookingId);
    }
}
