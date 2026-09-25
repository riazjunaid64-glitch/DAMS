namespace DAMS.Application.Common
{
    /// <summary>
    /// The lead changed after the caller read it — another user, an automated action, or an
    /// external enquiry enriching it — so the write was refused instead of overwriting the
    /// newer values. Surfaced as 409 so a client can reload and decide, rather than as the
    /// generic 400 a validation failure produces.
    ///
    /// Still an <see cref="InvalidOperationException"/>, so a caller that only knows that type
    /// (the website booking-request form) shows its message instead of a server error.
    /// </summary>
    public class LeadConcurrencyException : InvalidOperationException
    {
        public LeadConcurrencyException(
            string message = "Someone else updated this lead while you were working on it. Reload and try again.")
            : base(message)
        {
        }
    }
}
