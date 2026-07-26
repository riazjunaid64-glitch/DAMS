namespace DAMS.Domain.Enums
{
    /// <summary>
    /// The life of one attempt to put one notification on one channel. <see cref="Sent"/>
    /// means the provider accepted it; <see cref="Delivered"/> is only ever set from a
    /// verified provider signal, never from a successful send.
    /// </summary>
    public enum NotificationDeliveryStatus
    {
        Pending = 0,
        Scheduled = 1,
        Processing = 2,
        Sent = 3,
        Delivered = 4,
        Failed = 5,
        Retrying = 6,
        Bounced = 7,
        Expired = 8,
        Cancelled = 9,
        /// <summary>The recipient's preferences or an admin rule switched this channel off.</summary>
        Skipped = 10,
        /// <summary>No address or no active subscription to send to.</summary>
        Unavailable = 11
    }
}
