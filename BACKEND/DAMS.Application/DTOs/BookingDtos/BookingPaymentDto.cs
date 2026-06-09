using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.BookingDtos
{
    public class BookingPaymentDto
    {
        public int Id { get; set; }

        public int BookingId { get; set; }

        public int? InstallmentId { get; set; }

        public PaymentType Type { get; set; }

        public decimal Amount { get; set; }

        public PaymentMethod PaymentMethod { get; set; }

        public string? PaymentReference { get; set; }

        public string? ReceiptNumber { get; set; }

        public string? Notes { get; set; }

        public DateTime PaidAt { get; set; }
    }
}
