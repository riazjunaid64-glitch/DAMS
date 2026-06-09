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

        public InstallmentFrequency? InstallmentFrequency { get; set; }

        public int? NumberOfInstallments { get; set; }

        public decimal PossessionAmount { get; set; }

        public DateTime? PossessionDueDate { get; set; }

        public DateTime? InstallmentPlanGeneratedAt { get; set; }

        public int? InstallmentPlanGeneratedByUserId { get; set; }

        public DateTime? PossessionDate { get; set; }

        public DateTime? CompletionDate { get; set; }

        public string? CustomerNotes { get; set; }

        public string? InternalNotes { get; set; }

        // --- Application Form snapshot (printed official form) ---
        // These capture exactly what is written on the Floria Heights "Application Form".
        public string? SerialNo { get; set; }

        public string? ApartmentCategory { get; set; }

        public string? Tower { get; set; }

        public bool IsCorner { get; set; }

        public decimal? PricePerSft { get; set; }

        public decimal? DiscountPercent { get; set; }

        public string? ReferenceId { get; set; }

        public string? PaymentThrough { get; set; }

        // Which box is ticked under "Amount Received": Booking | Confirmation | LumSum.
        public string? ApplicationPaymentType { get; set; }

        public decimal? ApplicationAmountReceived { get; set; }

        public DateTime? ApplicationDate { get; set; }

        // Next of kin / nominee details from the Application Form.
        public string? NextOfKinName { get; set; }

        public string? NextOfKinRelation { get; set; }

        public string? NextOfKinContact { get; set; }

        public string? NextOfKinCnic { get; set; }

        public DateTime? NextOfKinDob { get; set; }

        public string? NextOfKinAddress { get; set; }

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
