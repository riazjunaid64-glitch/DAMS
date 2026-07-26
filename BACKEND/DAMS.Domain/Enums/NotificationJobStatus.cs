namespace DAMS.Domain.Enums
{
    public enum NotificationJobStatus
    {
        Scheduled = 0,
        Processing = 1,
        Sent = 2,
        PartiallyFailed = 3,
        Failed = 4,
        Cancelled = 5
    }
}
