namespace DAMS.Domain.Enums
{
    /// <summary>
    /// A delivery channel. Flags so a notification can record the set it was created for in
    /// one column, while each channel still gets its own delivery row and its own outcome.
    /// New channels (WhatsApp, SMS, mobile push) take the next free bit and plug in an
    /// <c>INotificationChannelSender</c>; nothing in the business modules changes.
    /// </summary>
    [Flags]
    public enum NotificationChannel
    {
        None = 0,
        InApp = 1,
        Email = 2,
        WebPush = 4
    }
}
