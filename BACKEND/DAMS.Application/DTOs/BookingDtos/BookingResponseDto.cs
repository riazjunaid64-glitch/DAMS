using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.BookingDtos
{
    public class BookingResponseDto
    {
        public int Id { get; set; }

        public int ClientId { get; set; }

        public int UnitId { get; set; }

        public decimal TotalPrice { get; set; }

        public decimal DownPayment { get; set; }

        public decimal RemainingAmount { get; set; }

        public decimal AmountPaid { get; set; }

        public DateTime BookingDate { get; set; }

        public BookingStatus Status { get; set; }

        // Client Info
        public string ClientName { get; set; } = string.Empty;

        public string ClientEmail { get; set; } = string.Empty;

        // Unit Info
        public string UnitNumber { get; set; } = string.Empty;

        public string UnitType { get; set; } = string.Empty;

        public decimal UnitPrice { get; set; }
    }
}
