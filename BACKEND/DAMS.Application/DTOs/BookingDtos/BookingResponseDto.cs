using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.BookingDtos
{
    public class BookingResponseDto
    {
        public int Id { get; set; }

        public string BookingReference { get; set; } = string.Empty;

        public int CustomerId { get; set; }

        public string CustomerName { get; set; } = string.Empty;

        public string CustomerPhone { get; set; } = string.Empty;

        public int UnitId { get; set; }

        public string UnitNumber { get; set; } = string.Empty;

        public string UnitType { get; set; } = string.Empty;

        public int ProjectId { get; set; }

        public string ProjectName { get; set; } = string.Empty;

        public int? BookingRequestId { get; set; }

        public CustomerSource Source { get; set; }

        public BookingStatus Status { get; set; }

        public decimal ListPrice { get; set; }

        public decimal AgreedSalePrice { get; set; }

        public decimal DiscountAmount { get; set; }

        public string? DiscountReason { get; set; }

        public decimal BookingAmountRequired { get; set; }

        public decimal BookingAmountReceived { get; set; }

        public decimal TotalInstallmentAmount { get; set; }

        public DateTime BookingDate { get; set; }

        public DateTime? BookingAmountDueDate { get; set; }

        public DateTime? BookingAmountConfirmedDate { get; set; }

        public DateTime? PossessionDate { get; set; }

        public DateTime? CompletionDate { get; set; }

        public string? CustomerNotes { get; set; }

        public string? InternalNotes { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }
}
