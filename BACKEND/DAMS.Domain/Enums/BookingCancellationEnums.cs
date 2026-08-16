namespace DAMS.Domain.Enums
{
    /// <summary>The Admin's original decision at the moment of cancellation. Immutable — even
    /// once a PayLater refund is eventually paid, this still reads PayLater.</summary>
    public enum CancellationRefundDecision
    {
        None = 0,
        PayNow = 1,
        PayLater = 2
    }

    /// <summary>Derived, not stored: computed from RefundAmount and whether a
    /// BookingCancellationRefund exists.</summary>
    public enum CancellationRefundStatus
    {
        NotRequired = 0,
        Pending = 1,
        Paid = 2
    }
}
