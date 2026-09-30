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

        /// <summary>The proof file attached to this payment, or null. Only filled for Admin / Accountant views.</summary>
        public PaymentProofDto? Proof { get; set; }
    }

    /// <summary>What a screen needs to show a proof link without another call.</summary>
    public class PaymentProofDto
    {
        public int Id { get; set; }

        public string FileName { get; set; } = string.Empty;

        public long FileSize { get; set; }
    }
}
