using System.ComponentModel.DataAnnotations;

namespace DAMS.Application.DTOs.BookingDtos
{
    /// <summary>
    /// Lets an admin set the negotiated terms (sale price, discount, and the booking
    /// amount required) on a booking that is still AwaitingBookingAmount. This is the
    /// step that unblocks recording booking-amount payments for request-derived bookings,
    /// which are created with BookingAmountRequired = 0.
    /// </summary>
    public class UpdateBookingFinancialsDto
    {
        [Range(0.01, double.MaxValue, ErrorMessage = "Agreed sale price must be greater than zero.")]
        public decimal AgreedSalePrice { get; set; }

        // Discount is entered as a percentage of the agreed sale price (0–100).
        [Range(0, 100, ErrorMessage = "Discount percent must be between 0 and 100.")]
        public decimal DiscountPercent { get; set; }

        [StringLength(500)]
        public string? DiscountReason { get; set; }

        [Range(0.01, double.MaxValue, ErrorMessage = "Booking amount required must be greater than zero.")]
        public decimal BookingAmountRequired { get; set; }

        public DateTime? BookingAmountDueDate { get; set; }
    }
}
