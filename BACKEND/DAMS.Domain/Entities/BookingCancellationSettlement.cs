using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>
    /// The Admin's cancellation decision for a booking: how much cash the customer had actually
    /// paid, how much of it will be refunded, and how much the company retains. Append-only —
    /// one row per booking, never edited after insertion. The actual cash payout (if any) is a
    /// separate <see cref="BookingCancellationRefund"/> row, because "we decided to refund X" and
    /// "we actually paid X" are different events that can happen on different days.
    /// </summary>
    public class BookingCancellationSettlement
    {
        public int Id { get; set; }
        public int BookingId { get; set; }

        public decimal CustomerCashReceivedSnapshot { get; set; }
        public decimal RefundAmount { get; set; }
        public decimal RetainedAmount { get; set; }
        public CancellationRefundDecision RefundDecision { get; set; }

        // Null only when RefundAmount == 0. Points at the Customer Refunds Payable system account.
        public int? RefundPayableAccountId { get; set; }

        public string Reason { get; set; } = string.Empty;

        public string IdempotencyKey { get; set; } = string.Empty;

        public int CancelledByUserId { get; set; }
        public string CancelledByName { get; set; } = string.Empty;
        public DateTime CancelledAt { get; set; } = DateTime.UtcNow;

        public Booking Booking { get; set; } = null!;
        public FinanceAccount? RefundPayableAccount { get; set; }
        public BookingCancellationRefund? Refund { get; set; }
    }

    /// <summary>
    /// The actual cash/bank payout of a cancellation settlement's agreed refund. At most one per
    /// settlement, and its Amount always equals Settlement.RefundAmount — this feature does not
    /// support partial payout of an already-decided refund obligation.
    /// </summary>
    public class BookingCancellationRefund
    {
        public int Id { get; set; }
        public int SettlementId { get; set; }
        public int FinanceAccountId { get; set; }

        public decimal Amount { get; set; }
        public DateTime PaidAt { get; set; }
        public PaymentMethod PaymentMethod { get; set; }
        public string? PaymentReference { get; set; }
        public string? Notes { get; set; }

        public string IdempotencyKey { get; set; } = string.Empty;

        public int RecordedByUserId { get; set; }
        public string RecordedByName { get; set; } = string.Empty;
        public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

        public BookingCancellationSettlement Settlement { get; set; } = null!;
        public FinanceAccount FinanceAccount { get; set; } = null!;
    }
}
