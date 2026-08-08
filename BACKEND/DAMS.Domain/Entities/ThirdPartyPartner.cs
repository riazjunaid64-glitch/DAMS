namespace DAMS.Domain.Entities
{
    public class ThirdPartyPartner
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string PartnerType { get; set; } = string.Empty;
        public string? ContactPerson { get; set; }
        public string? Phone { get; set; }
        public string? NormalizedPhone { get; set; }
        public string? Email { get; set; }
        public string? NormalizedEmail { get; set; }
        public string? Address { get; set; }
        public string? Cnic { get; set; }
        public string? NormalizedCnic { get; set; }
        public string? Ntn { get; set; }
        public string? NormalizedNtn { get; set; }
        public string? RegistrationNumber { get; set; }
        public string InternalCode { get; set; } = string.Empty;
        public string? BankName { get; set; }
        public string? AccountTitle { get; set; }
        public string? AccountNumber { get; set; }
        public string? Iban { get; set; }
        public string? Notes { get; set; }
        public bool IsActive { get; set; } = true;
        public int? CreatedByUserId { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public byte[] RowVersion { get; set; } = [];

        public ICollection<ThirdPartyAttribution> Attributions { get; set; } = new List<ThirdPartyAttribution>();
        public ICollection<CommissionRule> CommissionRules { get; set; } = new List<CommissionRule>();
        public ICollection<BookingCommission> Commissions { get; set; } = new List<BookingCommission>();
    }
}
