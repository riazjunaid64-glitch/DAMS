using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.PaymentDtos
{
    public class PaymentResponseDto
    {
        public int Id { get; set; }

        public int BookingId { get; set; }

        public int? InstallmentId { get; set; }

        public decimal Amount { get; set; }

        public DateTime PaymentDate { get; set; }

        public PaymentMethod PaymentMethod { get; set; }

        public string? PaymentReference { get; set; }

        /// <summary>
        /// Is this a down payment? (InstallmentId == null)
        /// </summary>
        public bool IsDownPayment { get; set; }
    }
}
