namespace DAMS.Domain.Entities
{
    /// <summary>
    /// The name of one attempt to create or change a booking's installment plan, written in the same
    /// transaction as the plan itself.
    /// <para>
    /// The database's retrying execution strategy replays a transaction whose commit acknowledgement
    /// was lost. Finding its own attempt here is how a replay knows the first execution committed, and
    /// that it must not run again — even after someone else has since changed the plan, which is why
    /// this is a row of its own rather than a stamp on the booking (the next plan would overwrite it).
    /// Rows are never edited or deleted.
    /// </para>
    /// </summary>
    public class InstallmentPlanAttempt
    {
        public int Id { get; set; }

        public int BookingId { get; set; }

        /// <summary>Unique across all bookings.</summary>
        public string AttemptKey { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Booking Booking { get; set; } = null!;
    }
}
