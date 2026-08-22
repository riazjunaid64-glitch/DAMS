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

        // The account the money was received in. Nullable on the wire so the service can
        // return the same "required" message the finance forms use instead of a raw 400.
        public int? FinanceAccountId { get; set; }

        [StringLength(500)]
        public string? PaymentReference { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }

        // Optional. Defaults to the current Pakistan business time when omitted, and can never be
        // in the future or before the committed opening-balance date.
        public DateTime? PaidAt { get; set; }
    }
}
