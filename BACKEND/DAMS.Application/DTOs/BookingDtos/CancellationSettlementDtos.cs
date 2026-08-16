using System.ComponentModel.DataAnnotations;
using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.BookingDtos
{
    /// <summary>Pays a previously-deferred (PayLater) cancellation refund. The amount is never
    /// accepted here — it always comes from the settlement's already-decided RefundAmount.</summary>
    public class PayCancellationRefundDto
    {
        [Required]
        public int FinanceAccountId { get; set; }

        [Required]
        public PaymentMethod PaymentMethod { get; set; }

        [StringLength(200)]
        public string? PaymentReference { get; set; }

        public DateTime? PaidAt { get; set; }

        [StringLength(2000)]
        public string? Notes { get; set; }

        [Required, StringLength(80, MinimumLength = 1)]
        public string IdempotencyKey { get; set; } = string.Empty;
    }

    public class BookingCancellationSettlementDto
    {
        public int Id { get; set; }
        public decimal CustomerCashReceivedSnapshot { get; set; }
        public decimal RefundAmount { get; set; }
        public decimal RetainedAmount { get; set; }
        public CancellationRefundDecision RefundDecision { get; set; }
        public CancellationRefundStatus RefundStatus { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public DateTime CancelledAt { get; set; }
        public int CancelledByUserId { get; set; }
        public string CancelledByName { get; set; } = string.Empty;
        public int? RefundPayableAccountId { get; set; }
        public string? RefundPayableAccountName { get; set; }
        public BookingCancellationRefundDto? Refund { get; set; }
    }

    public class BookingCancellationRefundDto
    {
        public int Id { get; set; }
        public decimal Amount { get; set; }
        public int FinanceAccountId { get; set; }
        public string FinanceAccountName { get; set; } = string.Empty;
        public PaymentMethod PaymentMethod { get; set; }
        public string? PaymentReference { get; set; }
        public DateTime PaidAt { get; set; }
        public string? Notes { get; set; }
        public int RecordedByUserId { get; set; }
        public string RecordedByName { get; set; } = string.Empty;
        public DateTime RecordedAt { get; set; }
    }
}
