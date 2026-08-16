using System.ComponentModel.DataAnnotations;
using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.BookingDtos
{
    public class CancelBookingDto
    {
        [Required, StringLength(500, MinimumLength = 1)]
        public string Reason { get; set; } = string.Empty;

        // A stale-data precondition, NOT the authoritative amount: the server always recomputes
        // actual customer cash received from Payment rows and rejects if it no longer matches.
        [Range(0, double.MaxValue)]
        public decimal ExpectedCustomerCashReceived { get; set; }

        [Range(0, double.MaxValue)]
        public decimal RefundAmount { get; set; }

        public CancellationRefundDecision RefundDecision { get; set; } = CancellationRefundDecision.None;

        [Required, StringLength(80, MinimumLength = 1)]
        public string IdempotencyKey { get; set; } = string.Empty;

        // Base64 RowVersion captured when the cancellation dialog was opened.
        public string? ConcurrencyToken { get; set; }

        // PayNow only.
        public int? RefundFinanceAccountId { get; set; }
        public PaymentMethod? RefundPaymentMethod { get; set; }
        [StringLength(200)]
        public string? RefundPaymentReference { get; set; }
        public DateTime? RefundPaidAt { get; set; }
        [StringLength(2000)]
        public string? RefundNotes { get; set; }
    }
}
