using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    public class Booking
    {
        public int Id { get; set; }

        // Human-readable identifier for staff and customers (e.g. BK-000123).
        public string BookingReference { get; set; } = string.Empty;

        public int CustomerId { get; set; }

        public int UnitId { get; set; }

        // Set when this booking originated from an approved website booking request.
        public int? BookingRequestId { get; set; }

        public CustomerSource Source { get; set; } = CustomerSource.Other;

        public int? AssignedSalesUserId { get; set; }

        public BookingStatus Status { get; set; } = BookingStatus.AwaitingBookingAmount;

        // --- Financial snapshot ---
        public decimal ListPrice { get; set; }

        public decimal AgreedSalePrice { get; set; }

        public decimal DiscountAmount { get; set; }

        public string? DiscountReason { get; set; }

        public decimal BookingAmountRequired { get; set; }

        public decimal BookingAmountReceived { get; set; }

        public decimal TotalInstallmentAmount { get; set; }

        // --- Dates ---
        public DateTime BookingDate { get; set; } = DateTime.UtcNow;

        public DateTime? BookingAmountDueDate { get; set; }

        public DateTime? BookingAmountConfirmedDate { get; set; }

        public DateTime? InstallmentPlanStartDate { get; set; }

        public DateTime? PossessionDate { get; set; }

        public DateTime? CompletionDate { get; set; }

        public string? CustomerNotes { get; set; }

        public string? InternalNotes { get; set; }

        public int? CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        // Navigation
        public Customer Customer { get; set; } = null!;

        public Unit Unit { get; set; } = null!;

        public BookingRequest? BookingRequest { get; set; }

        public ICollection<Installment> Installments { get; set; } = new List<Installment>();

        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    }
}
