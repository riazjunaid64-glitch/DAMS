namespace DAMS.Domain.Entities
{
    /// <summary>
    /// The moment a unit sale became revenue. Written once, at possession/handover, and never
    /// again — completion does not recognise a second time.
    /// <para>
    /// It exists because a booking's CURRENT status cannot answer a question about the past. A
    /// booking that reaches possession today would, read by status alone, appear as revenue in
    /// every report period ever run — including periods that closed months ago. This row pins the
    /// recognition to the business date it actually happened on, so historical reports stay
    /// historical.
    /// </para>
    /// <para>
    /// One row per booking, enforced by a unique index on <see cref="BookingId"/>: the recognition
    /// is the event, so a duplicate would be a duplicate sale.
    /// </para>
    /// </summary>
    public class BookingSaleRecognition
    {
        public int Id { get; set; }

        public int BookingId { get; set; }

        /// <summary>
        /// The Pakistan business date the sale belongs to — what P&amp;L, Trial Balance and Balance
        /// Sheet use to place the revenue, the deposit clearing and the receivable. Never
        /// recomputed from <see cref="RecognizedAt"/>, which is a raw UTC instant and can land on
        /// the wrong calendar day around Pakistan midnight.
        /// </summary>
        public DateTime RecognitionDate { get; set; }

        /// <summary>AgreedSalePrice − DiscountAmount at the moment of recognition.</summary>
        public decimal NetSaleValue { get; set; }

        /// <summary>The audit instant, in UTC. Who/when detail only — never a reporting date.</summary>
        public DateTime RecognizedAt { get; set; } = DateTime.UtcNow;

        public int? RecognizedByUserId { get; set; }

        public Booking Booking { get; set; } = null!;
    }
}
