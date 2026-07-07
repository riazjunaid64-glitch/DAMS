namespace DAMS.Application.Common
{
    /// <summary>
    /// Business dates (due dates, "today", overdue cut-offs) are interpreted in
    /// Pakistan Standard Time (UTC+5, no daylight saving), not UTC, so an
    /// installment due on July 5 becomes overdue at local midnight rather than
    /// 5 AM the next day.
    /// </summary>
    public static class PakistanTime
    {
        private static readonly TimeSpan Offset = TimeSpan.FromHours(5);

        public static DateTime Now => DateTime.UtcNow + Offset;

        public static DateTime Today => Now.Date;
    }
}
