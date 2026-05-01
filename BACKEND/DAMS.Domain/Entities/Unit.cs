using DAMS.Domain.Enums;
namespace DAMS.Domain.Entities
{
    public class Unit
    {
        public int Id { get; set; }

        public int ProjectId { get; set; }

        public string UnitNumber { get; set; } = string.Empty;

        public string UnitType { get; set; } = string.Empty;

        public int FloorNumber { get; set; }

        public decimal Size { get; set; }

        public decimal Price { get; set; }

        public string Status { get; set; } = UnitStatus.Available;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        // Navigation
        public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
        public Project Project { get; set; } = null!;
        public ICollection<UnitMedia> MediaFiles { get; set; } = new List<UnitMedia>();
    }
}