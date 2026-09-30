using DAMS.Domain.Entities;
using DAMS.Domain.Enums;

namespace DAMS.Application.Common
{
    /// <summary>
    /// The rules for changing a booking's commercial terms that the Set / Edit terms form and the
    /// installment plan both apply, so the two doors into the price cannot drift apart.
    /// </summary>
    internal static class BookingTermsRules
    {
        /// <summary>
        /// A discount reason that was not sent is kept; one that was sent replaces it, and blank clears it.
        /// The forms only show the reason while there is a discount, so "not sent" must never mean "erase".
        /// </summary>
        public static string? ResolveDiscountReason(string? existing, string? sent) =>
            sent is null ? existing : string.IsNullOrWhiteSpace(sent) ? null : sent.Trim();

        /// <summary>The booking amount is due today or later — a due date already gone cannot be promised.</summary>
        public static void EnsureDueDateNotPast(DateTime? dueDate)
        {
            if (dueDate.HasValue && dueDate.Value.Date < PakistanTime.Today)
                throw new InvalidOperationException("The due-by date must be today or later.");
        }

        /// <summary>Whether any tracked commercial term differs from what is on the booking now, so an unchanged save leaves no history.</summary>
        public static bool Changed(
            Booking booking, decimal newAgreedSalePrice, decimal? newDiscountPercent, string? newDiscountReason,
            decimal newBookingAmountRequired, DateTime? newBookingAmountDueDate) =>
            newAgreedSalePrice != booking.AgreedSalePrice
            || (newDiscountPercent ?? 0m) != (booking.DiscountPercent ?? 0m)
            || newDiscountReason != booking.DiscountReason
            || newBookingAmountRequired != booking.BookingAmountRequired
            || newBookingAmountDueDate?.Date != booking.BookingAmountDueDate?.Date;

        /// <summary>The audit row for a change from the terms currently on <paramref name="booking"/> to the new ones.</summary>
        public static BookingTermsHistory History(
            Booking booking, BookingTermsChangeSource source, int? changedByUserId,
            decimal newAgreedSalePrice, decimal? newDiscountPercent, string? newDiscountReason,
            decimal newBookingAmountRequired, DateTime? newBookingAmountDueDate) => new()
        {
            BookingId = booking.Id,
            Source = source,
            ChangedAt = DateTime.UtcNow,
            ChangedByUserId = changedByUserId,
            OldAgreedSalePrice = booking.AgreedSalePrice,
            NewAgreedSalePrice = newAgreedSalePrice,
            OldDiscountPercent = booking.DiscountPercent,
            NewDiscountPercent = newDiscountPercent,
            OldDiscountReason = booking.DiscountReason,
            NewDiscountReason = newDiscountReason,
            OldBookingAmountRequired = booking.BookingAmountRequired,
            NewBookingAmountRequired = newBookingAmountRequired,
            OldBookingAmountDueDate = booking.BookingAmountDueDate,
            NewBookingAmountDueDate = newBookingAmountDueDate
        };

        /// <summary>The message the loser of a race gets, in place of a server error.</summary>
        public const string ChangedByAnotherUser =
            "This booking was just changed by someone else. Refresh and try again.";
    }
}
