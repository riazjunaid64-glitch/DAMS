namespace DAMS.Application.Common
{
    /// <summary>
    /// Another request for the same contact held its lock for longer than intake waits. Nothing
    /// was written, and trying again shortly will succeed — so an API surfaces this as 503 with
    /// Retry-After, not as a 400 a webhook sender would take as "never retry" and drop.
    ///
    /// Still an <see cref="InvalidOperationException"/>, so a caller that only knows that type
    /// (the website booking-request form) shows its message instead of a server error.
    /// </summary>
    public class LeadIntakeBusyException : InvalidOperationException
    {
        public const int RetryAfterSeconds = 5;

        public LeadIntakeBusyException()
            : base("Another enquiry for the same contact is still being processed. Try again in a moment.")
        {
        }
    }
}
