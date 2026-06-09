using System.ComponentModel.DataAnnotations;

namespace DAMS.Application.DTOs.BookingDtos
{
    public class CancelBookingDto
    {
        [StringLength(500)]
        public string? Reason { get; set; }
    }
}
