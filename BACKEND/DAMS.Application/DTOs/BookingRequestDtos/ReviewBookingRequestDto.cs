using System.ComponentModel.DataAnnotations;

namespace DAMS.Application.DTOs.BookingRequestDtos
{
    public class RejectBookingRequestDto
    {
        public int BookingRequestId { get; set; }

        [StringLength(500)]
        public string? RejectionReason { get; set; }
    }
}
