using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>
    /// One change to a booking's commercial terms: who made it, when, and the terms before and after.
    /// <para>
    /// Written for every Set / Edit terms and for every change of price or discount that goes through
    /// the installment plan. It is an audit trail only — nothing reads it back into a booking, and
    /// rows are never edited or deleted.
    /// </para>
    /// </summary>
    public class BookingTermsHistory
    {
        public int Id { get; set; }

        public int BookingId { get; set; }

        /// <summary>Where the change was made.</summary>
        public BookingTermsChangeSource Source { get; set; }

        /// <summary>The audit instant, in UTC.</summary>
        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

        public int? ChangedByUserId { get; set; }

        public decimal OldAgreedSalePrice { get; set; }
        public decimal NewAgreedSalePrice { get; set; }

        public decimal? OldDiscountPercent { get; set; }
        public decimal? NewDiscountPercent { get; set; }

        public string? OldDiscountReason { get; set; }
        public string? NewDiscountReason { get; set; }

        public decimal OldBookingAmountRequired { get; set; }
        public decimal NewBookingAmountRequired { get; set; }

        public DateTime? OldBookingAmountDueDate { get; set; }
        public DateTime? NewBookingAmountDueDate { get; set; }

        public Booking Booking { get; set; } = null!;
    }
}
