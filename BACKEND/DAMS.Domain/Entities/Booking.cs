using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    public class Booking
    {
        public int Id { get; set; }

        public int ClientId { get; set; }   // User
        public int UnitId { get; set; }

        public decimal TotalPrice { get; set; }
        public decimal DownPayment { get; set; }
        public decimal RemainingAmount { get; set; }

        public DateTime BookingDate { get; set; } = DateTime.UtcNow;

        public BookingStatus Status { get; set; }

        // Navigation
        public User Client { get; set; } = null!;
        public Unit Unit { get; set; } = null!;

        public ICollection<Installment> Installments { get; set; } = new List<Installment>();
        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    }
}