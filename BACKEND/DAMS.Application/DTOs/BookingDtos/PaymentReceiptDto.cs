using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.BookingDtos
{
    /// <summary>
    /// Flat, render-ready payload for an official payment receipt. Joins the payment
    /// with its booking, customer, unit, project and (when applicable) installment, and
    /// resolves the admin who recorded it ("Received By"). No new data is stored — this
    /// is a read-only projection used purely for printing/PDF.
    /// </summary>
    public class PaymentReceiptDto
    {
        // --- Receipt header ---
        public int PaymentId { get; set; }

        public string? ReceiptNumber { get; set; }

        public DateTime PaidAt { get; set; }

        // Admin who recorded the payment (Payment.RecordedByUserId -> User.FullName).
        public string? ReceivedByName { get; set; }

        public string BookingReference { get; set; } = string.Empty;

        // --- Customer ---
        public string CustomerName { get; set; } = string.Empty;

        public string? FatherName { get; set; }

        public string? CustomerPhone { get; set; }

        public string? CustomerCnic { get; set; }

        public string? CustomerAddress { get; set; }

        // --- Property ---
        public string ProjectName { get; set; } = string.Empty;

        public string UnitType { get; set; } = string.Empty;

        public string UnitNumber { get; set; } = string.Empty;

        // Derived from the unit number prefix (e.g. "A-606a" -> "A") when present.
        public string? Block { get; set; }

        public int FloorNumber { get; set; }

        public decimal UnitSize { get; set; }

        // --- Payment ---
        public PaymentType Type { get; set; }

        public PaymentMethod PaymentMethod { get; set; }

        public string? PaymentReference { get; set; }

        public decimal Amount { get; set; }

        // Installment context (only set for installment payments).
        public int? InstallmentSequence { get; set; }

        public InstallmentType? InstallmentType { get; set; }
    }
}
