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

        // Extra customer details used to fill the printed Application Form.
        public string? CustomerFatherName { get; set; }

        public string? CustomerCnic { get; set; }

        public string? CustomerEmail { get; set; }

        public string? CustomerAddress { get; set; }

        public DateTime? CustomerDateOfBirth { get; set; }

        public string? CustomerNationality { get; set; }

        public string? CustomerOccupation { get; set; }

        public string? CustomerWhatsapp { get; set; }

        public int UnitId { get; set; }

        public string UnitNumber { get; set; } = string.Empty;

        public string UnitType { get; set; } = string.Empty;

        public int UnitFloorNumber { get; set; }

        public decimal UnitSize { get; set; }

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

        public decimal BookingAmountRemaining => Math.Max(0m, BookingAmountRequired - BookingAmountReceived);

        public bool IsBookingAmountFullyPaid => BookingAmountRequired > 0m && BookingAmountReceived >= BookingAmountRequired;

        public decimal TotalInstallmentAmount { get; set; }

        public decimal InstallmentPaid { get; set; }

        public decimal InstallmentRemaining { get; set; }

        public bool HasInstallmentSchedule { get; set; }

        public DateTime BookingDate { get; set; }

        public DateTime? BookingAmountDueDate { get; set; }

        public DateTime? BookingAmountConfirmedDate { get; set; }

        public DateTime? PossessionDate { get; set; }

        public DateTime? CompletionDate { get; set; }

        public string? CustomerNotes { get; set; }

        public string? InternalNotes { get; set; }

        // --- Application Form snapshot ---
        public string? SerialNo { get; set; }

        public string? ApartmentCategory { get; set; }

        public string? Tower { get; set; }

        public bool IsCorner { get; set; }

        public decimal? PricePerSft { get; set; }

        public decimal? DiscountPercent { get; set; }

        public string? ReferenceId { get; set; }

        public string? PaymentThrough { get; set; }

        public string? ApplicationPaymentType { get; set; }

        public decimal? ApplicationAmountReceived { get; set; }

        public DateTime? ApplicationDate { get; set; }

        public string? NextOfKinName { get; set; }

        public string? NextOfKinRelation { get; set; }

        public string? NextOfKinContact { get; set; }

        public string? NextOfKinCnic { get; set; }

        public DateTime? NextOfKinDob { get; set; }

        public string? NextOfKinAddress { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public List<BookingPaymentDto> Payments { get; set; } = new();
    }
}
