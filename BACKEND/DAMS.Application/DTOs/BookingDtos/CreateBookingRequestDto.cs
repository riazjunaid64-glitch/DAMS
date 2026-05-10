using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.BookingDtos
{
    public class CreateBookingRequestDto
    {
        public int UnitId { get; set; }

        public decimal DownPaymentAmount { get; set; }

        public PaymentMethod DownPaymentMethod { get; set; }

        public string? PaymentReference { get; set; }
    }
}
