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

        /// <summary>
        /// The Pakistan business date a UTC instant falls on. Between 19:00 and 23:59 UTC the
        /// two disagree, so anything that dates an accounting event from a stored timestamp must
        /// come through here rather than taking <c>.Date</c> off the raw UTC value.
        /// </summary>
        public static DateTime ToBusinessDate(DateTime utc) => (AsUtc(utc) + Offset).Date;

        /// <summary>
        /// The same instant on the Pakistan clock. Fixed UTC+5, because Pakistan does not
        /// observe daylight saving, so a missing OS timezone must not fall back to UTC.
        /// </summary>
        public static DateTime ToLocal(DateTime utc) =>
            DateTime.SpecifyKind(AsUtc(utc) + Offset, DateTimeKind.Unspecified);

        private static DateTime AsUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        public static DateTime StartOfBusinessDateUtc(DateTime businessDate) =>
            DateTime.SpecifyKind(businessDate.Date - Offset, DateTimeKind.Utc);
    }
}
