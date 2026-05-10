using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.BookingDtos
{
    public class BookingPaymentResponseDto
    {
        public int Id { get; set; }

        public decimal Amount { get; set; }

        public PaymentMethod PaymentMethod { get; set; }

        public string? PaymentReference { get; set; }

        public DateTime PaidAt { get; set; }

        public int? InstallmentId { get; set; }
    }

    public class BookingResponseDto
    {
        public int Id { get; set; }

        public int ClientId { get; set; }

        public int UnitId { get; set; }

        public string UnitNumber { get; set; } = string.Empty;

        public int ProjectId { get; set; }

        public string ProjectName { get; set; } = string.Empty;

        public BookingStatus Status { get; set; }

        public DateTime BookingDate { get; set; }

        public decimal UnitPriceAtBooking { get; set; }

        public decimal DownPaymentAmount { get; set; }

        public List<BookingPaymentResponseDto> Payments { get; set; } = new();
    }
}
