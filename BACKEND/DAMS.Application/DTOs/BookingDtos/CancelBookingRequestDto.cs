namespace DAMS.Application.DTOs.BookingDtos
{
    public class CancelBookingRequestDto
    {
        /// <summary>
        /// Reason for cancellation (optional)
        /// </summary>
        public string? Reason { get; set; }
    }
}
