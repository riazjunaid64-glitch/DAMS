using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.BookingDtos
{
    public class BookingPaymentDto
    {
        public int Id { get; set; }

        public int BookingId { get; set; }

        public int? InstallmentId { get; set; }

        /// <summary>Which installment this went to (0 is the possession row); null for the booking amount.</summary>
        public int? InstallmentSequence { get; set; }

        public InstallmentType? InstallmentType { get; set; }

        public int? FinanceAccountId { get; set; }

        /// <summary>The account the money was received in; null on payments recorded before accounts existed.</summary>
        public string? AccountName { get; set; }

        /// <summary>Who recorded the payment.</summary>
        public string? RecordedByName { get; set; }

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
