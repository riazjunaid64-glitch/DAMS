namespace DAMS.Domain.Entities
{
    /// <summary>
    /// A reusable expense head with its withholding-tax configuration. Expenses snapshot the
    /// rate they were entered at (<see cref="Expense.WhtRate"/>), so correcting a rate here
    /// never rewrites tax that has already been withheld and filed.
    /// </summary>
    public class ExpenseCategory
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string? Description { get; set; }

        // ── Withholding configuration ──
        /// <summary>False for heads withheld elsewhere (salaries run through payroll under s.149)
        /// or withheld at source by the supplier (utilities under s.235).</summary>
        public bool IsWhtApplicable { get; set; } = true;

        /// <summary>Percentage, not a fraction: 4.00 means 4%.</summary>
        public decimal FilerRate { get; set; }

        public decimal NonFilerRate { get; set; }

        /// <summary>Annual per-vendor exemption in rupees. 0 = withhold from the first rupee.</summary>
        public decimal AnnualThreshold { get; set; }

        /// <summary>Income Tax Ordinance reference, e.g. "153(1)(a)". Also groups categories for
        /// threshold aggregation, because the statutory threshold is per section, not per head.</summary>
        public string? TaxSection { get; set; }

        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public int? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public byte[] RowVersion { get; set; } = [];

        public ICollection<Expense> Expenses { get; set; } = new List<Expense>();

        /// <summary>Fixed-asset purchases classified under this head. Purchases share the expense
        /// rate table because the statutory threshold is one allowance per supplier per section,
        /// covering everything they were paid — capital and revenue alike.</summary>
        public ICollection<AssetPurchase> AssetPurchases { get; set; } = new List<AssetPurchase>();
    }
}
