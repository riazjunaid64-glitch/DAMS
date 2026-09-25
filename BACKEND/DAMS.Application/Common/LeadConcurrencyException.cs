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
        // Not "someone else": the change may equally be an enquiry enriching the lead or the
        // alert scan marking a follow-up missed.
        public const string DefaultMessage = "This lead changed while you were working on it. Reload and try again.";

        public LeadConcurrencyException(string message = DefaultMessage)
            : base(message)
        {
        }
    }
}
