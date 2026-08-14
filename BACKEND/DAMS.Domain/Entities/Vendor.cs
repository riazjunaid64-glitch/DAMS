using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>
    /// A supplier or contractor paid through expenses. Linking an expense to a vendor is what
    /// makes filer-status-aware rates and annual threshold tracking possible; expenses may still
    /// carry a free-text vendor name instead, which is how every pre-existing row behaves.
    /// </summary>
    public class Vendor
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Ntn { get; set; }
        public string? Cnic { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }

        public FilerStatus FilerStatus { get; set; } = FilerStatus.Unknown;

        /// <summary>When the filer status was last verified against the FBR Active Taxpayer List.
        /// Maintained by hand — there is no live ATL integration.</summary>
        public DateTime? FilerStatusCheckedAt { get; set; }

        public string? Notes { get; set; }
        public bool IsActive { get; set; } = true;
        public int? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public byte[] RowVersion { get; set; } = [];

        public ICollection<Expense> Expenses { get; set; } = new List<Expense>();

        public ICollection<AssetPurchase> AssetPurchases { get; set; } = new List<AssetPurchase>();
    }
}
