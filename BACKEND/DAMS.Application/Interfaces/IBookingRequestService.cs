using DAMS.Application.DTOs.BookingRequestDtos;

namespace DAMS.Application.Interfaces
{
    public interface IBookingRequestService
    {
        Task<BookingRequestResponseDto> CreateBookingRequestAsync(CreateBookingRequestDto dto, int? userId);

        Task<BookingRequestResponseDto?> GetBookingRequestByIdAsync(int id);

        Task<BookingRequestListDto> GetBookingRequestsAsync(BookingRequestFilterDto filter);

        Task<List<BookingRequestResponseDto>> GetMyBookingRequestsAsync(int userId);

        Task<BookingRequestResponseDto> ApproveBookingRequestAsync(int bookingRequestId, int adminUserId);

        Task<BookingRequestResponseDto> RejectBookingRequestAsync(int bookingRequestId, int adminUserId, string? rejectionReason);

        /// <summary>
        /// The requesting customer withdraws their own pending request, releasing
        /// the unit back to the market.
        /// </summary>
        Task<BookingRequestResponseDto> CancelBookingRequestAsync(int bookingRequestId, int userId);

        Task<bool> HasPendingRequestForUnitAsync(int unitId);

        Task<Dictionary<string, int>> GetBookingRequestStatsAsync();
    }
}
