using System.ComponentModel.DataAnnotations;
using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.BookingDtos
{
    public class RecordBookingAmountPaymentDto
    {
        [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
        public decimal Amount { get; set; }

        [Required]
        public PaymentMethod PaymentMethod { get; set; }

        [StringLength(500)]
        public string? PaymentReference { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }

        // Optional. Defaults to now (UTC) when omitted.
        public DateTime? PaidAt { get; set; }
    }
}
