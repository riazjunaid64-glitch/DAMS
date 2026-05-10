using DAMS.Application.DTOs.BookingDtos;

namespace DAMS.Application.Interfaces
{
    public interface IBookingService
    {
        Task<BookingResponseDto> CreateBookingAsync(int clientUserId, CreateBookingRequestDto dto);

        Task<List<BookingResponseDto>> GetBookingsForClientAsync(int clientUserId);
    }
}
