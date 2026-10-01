using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.BookingDtos
{
    /// <summary>One row of the Bookings tab on the customer page: what the booking is and its three money figures.</summary>
    public class CustomerBookingDto
    {
        public int Id { get; set; }

        public string BookingReference { get; set; } = string.Empty;

        public BookingStatus Status { get; set; }

        public string UnitNumber { get; set; } = string.Empty;

        public string ProjectName { get; set; } = string.Empty;

        public DateTime BookingDate { get; set; }

        /// <summary>Agreed price after any discount.</summary>
        public decimal NetPrice { get; set; }

        /// <summary>Money received — the same figure as Collected on the booking page.</summary>
        public decimal Collected { get; set; }

        /// <summary>What is left to pay — the same figure as Outstanding on the booking page; 0 once cancelled.</summary>
        public decimal Outstanding { get; set; }
    }
}
