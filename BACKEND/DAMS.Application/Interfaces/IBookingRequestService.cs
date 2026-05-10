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

        Task<bool> HasPendingRequestForUnitAsync(int unitId);

        Task<Dictionary<string, int>> GetBookingRequestStatsAsync();
    }
}
