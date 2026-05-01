using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    public class Booking
    {
        public int Id { get; set; }

        public int ClientId { get; set; }   // User
        public int UnitId { get; set; }

        public decimal TotalPrice { get; set; }      // Snapshot from Unit.Price at booking time
        public decimal DownPayment { get; set; }
        public decimal RemainingAmount { get; set; }   // TotalPrice - DownPayment
        public decimal AmountPaid { get; set; } = 0;   // Track total payments

        public DateTime BookingDate { get; set; } = DateTime.UtcNow;

        public BookingStatus Status { get; set; } = BookingStatus.Active;

        // Data Safety - Soft Delete
        public bool IsDeleted { get; set; } = false;
        public DateTime? DeletedAt { get; set; }

        // Navigation
        public User Client { get; set; } = null!;
        public Unit Unit { get; set; } = null!;

        public ICollection<Installment> Installments { get; set; } = new List<Installment>();
        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    }
}