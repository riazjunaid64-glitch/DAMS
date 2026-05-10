using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    public class Booking
    {
        public int Id { get; set; }

        public int ClientId { get; set; }

        public int UnitId { get; set; }

        public BookingStatus Status { get; set; } = BookingStatus.Pending;

        public DateTime BookingDate { get; set; } = DateTime.UtcNow;

        public decimal UnitPriceAtBooking { get; set; }

        public decimal DownPaymentAmount { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public User Client { get; set; } = null!;

        public Unit Unit { get; set; } = null!;

        public ICollection<Installment> Installments { get; set; } = new List<Installment>();

        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    }
}
