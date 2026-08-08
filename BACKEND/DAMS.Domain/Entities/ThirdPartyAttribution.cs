namespace DAMS.Domain.Entities
{
    /// <summary>Attribution may exist on a lead or customer, but only a booking-owned commission can become payable.</summary>
    public class ThirdPartyAttribution
    {
        public int Id { get; set; }
        public int PartnerId { get; set; }
        public int? LeadId { get; set; }
        public int? CustomerId { get; set; }
        public int? BookingId { get; set; }
        public string RelationshipType { get; set; } = "Introducer";
        public DateTime? IntroducedAt { get; set; }
        public string? SourceDetails { get; set; }
        public string? Notes { get; set; }
        public bool IsPrimary { get; set; }
        public decimal AllocationPercent { get; set; } = 100m;
        public int? AssignedByUserId { get; set; }
        public string? AssignedByName { get; set; }
        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
        public byte[] RowVersion { get; set; } = [];

        public ThirdPartyPartner Partner { get; set; } = null!;
        public Lead? Lead { get; set; }
        public Customer? Customer { get; set; }
        public Booking? Booking { get; set; }
        public ICollection<BookingCommission> Commissions { get; set; } = new List<BookingCommission>();
    }
}
