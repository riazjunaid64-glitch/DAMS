using System.ComponentModel.DataAnnotations;
using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.BookingDtos
{
    /// <summary>
    /// Admin-driven booking creation for walk-in / phone customers.
    /// Either link an existing customer via <see cref="CustomerId"/> or
    /// provide new customer details to create one.
    /// </summary>
    public class CreateBookingDto
    {
        [Required]
        public int UnitId { get; set; }

        [Required]
        public CustomerSource Source { get; set; } = CustomerSource.WalkIn;

        // Existing customer (preferred). If null, NewCustomer must be supplied.
        public int? CustomerId { get; set; }

        public NewCustomerForBookingDto? NewCustomer { get; set; }

        // Optional financial terms. Defaults to unit list price when omitted.
        public decimal? AgreedSalePrice { get; set; }

        public decimal? DiscountAmount { get; set; }

        [StringLength(500)]
        public string? DiscountReason { get; set; }

        public decimal? BookingAmountRequired { get; set; }

        public DateTime? BookingAmountDueDate { get; set; }

        public int? AssignedSalesUserId { get; set; }

        [StringLength(1000)]
        public string? CustomerNotes { get; set; }

        [StringLength(1000)]
        public string? InternalNotes { get; set; }
    }

    public class NewCustomerForBookingDto
    {
        [Required]
        [StringLength(200, MinimumLength = 2)]
        public string FullName { get; set; } = string.Empty;

        [StringLength(200)]
        public string? FatherName { get; set; }

        [Required]
        [StringLength(50, MinimumLength = 7)]
        public string Phone { get; set; } = string.Empty;

        [StringLength(50)]
        public string? CNIC { get; set; }

        [EmailAddress]
        [StringLength(200)]
        public string? Email { get; set; }

        [StringLength(500)]
        public string? Address { get; set; }

        [StringLength(500)]
        public string? SourceNotes { get; set; }
    }
}
