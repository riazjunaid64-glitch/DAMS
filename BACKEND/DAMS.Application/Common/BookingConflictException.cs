namespace DAMS.Application.Common
{
    /// <summary>
    /// The unit the booking names was taken by someone else first: another booking holds it, or the
    /// database's one-live-booking-per-unit rule refused a second one. Surfaced as 409 so the screen
    /// can tell the person to pick another unit, rather than as the generic 400 of a form error or a
    /// server fault.
    ///
    /// Still an <see cref="InvalidOperationException"/>, so a caller that only knows that type (lead
    /// conversion) shows its message instead of a server error.
    /// </summary>
    public class BookingConflictException : InvalidOperationException
    {
        public BookingConflictException(string message) : base(message)
        {
        }

        public static BookingConflictException UnitTaken(string unitNumber) =>
            new($"Unit {unitNumber} was just booked by someone else. Pick another unit.");
    }
}
