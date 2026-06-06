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

        [Range(0, double.MaxValue)]
        public decimal DiscountAmount { get; set; }

        [StringLength(500)]
        public string? DiscountReason { get; set; }

        [Range(0.01, double.MaxValue, ErrorMessage = "Booking amount required must be greater than zero.")]
        public decimal BookingAmountRequired { get; set; }

        public DateTime? BookingAmountDueDate { get; set; }
    }
}
