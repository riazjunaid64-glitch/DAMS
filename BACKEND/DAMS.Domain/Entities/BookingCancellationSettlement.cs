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

        // Free-text notes captured at cancellation time, independent of the refund decision — the
        // Admin can leave a note ("Manager approved full forfeiture") whether or not money moves.
        public string? Notes { get; set; }

        public string IdempotencyKey { get; set; } = string.Empty;

        public int CancelledByUserId { get; set; }
        public string CancelledByName { get; set; } = string.Empty;

        // The exact audit instant, in UTC — who/when detail only. Never use this for financial
        // period filtering: DAMS' business day is Pakistan time (UTC+5), so a cancellation made at
        // 00:30 PKT is still 19:30 UTC the PREVIOUS calendar day. CancellationDate below is what
        // reports use.
        public DateTime CancelledAt { get; set; } = DateTime.UtcNow;

        // The Pakistan business date this cancellation belongs to — what P&L, Trial Balance,
        // Balance Sheet and every other report use to place the retained income and the refund
        // liability, and to clear the customer deposit they come out of. Set once
        // at cancellation as PakistanTime.Today; never recomputed from CancelledAt.
        public DateTime CancellationDate { get; set; }

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
