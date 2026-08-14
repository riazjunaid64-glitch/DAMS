using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>
    /// Something lasting the company bought and still owns — a desk, a laptop, a plot of land.
    /// <para>
    /// The distinction from <see cref="Expense"/> is the whole point of this entity. An expense is
    /// value consumed: the money is gone and profit falls. A purchase is value converted: cash
    /// becomes furniture, the company is exactly as wealthy as before, and profit does not move.
    /// So this never appears in the Profit &amp; Loss. It moves two balance-sheet accounts in
    /// opposite directions: <see cref="FinanceAccountId"/> down, <see cref="AssetAccountId"/> up.
    /// </para>
    /// <para>
    /// Depreciation is deliberately absent. Spreading the cost over a useful life requires deciding
    /// how many years each class of asset lasts, which is the client's accountant's call. The asset
    /// is carried here at what was paid for it, indefinitely, until that decision is made.
    /// </para>
    /// </summary>
    public class AssetPurchase : IWithholdingSubject
    {
        public int Id { get; set; }

        /// <summary>Nullable: a head-office desk belongs to no particular project.</summary>
        public int? ProjectId { get; set; }

        /// <summary>The fixed-asset account whose balance rises by the GROSS amount — Office
        /// Equipment, Cost of Plot, and so on.</summary>
        public int AssetAccountId { get; set; }

        /// <summary>The bank or cash account the money left. Required, unlike the equivalent on
        /// <see cref="Expense"/>: there is no legacy asset purchase, and an unassigned one would
        /// credit nothing while still debiting the asset, tipping the balance sheet over.</summary>
        public int FinanceAccountId { get; set; }

        /// <summary>The GROSS price — what the supplier invoiced, before any tax was withheld. The
        /// asset is carried at this figure, because that is what it cost the company; the shortfall
        /// between it and <see cref="NetPaid"/> is owed to FBR, not a discount.</summary>
        public decimal Amount { get; set; }

        /// <summary>What was actually bought, in the operator's words — "3 office desks",
        /// "Dell Latitude 5540". Distinct from <see cref="Category"/>, which is the tax head.</summary>
        public string ItemName { get; set; } = string.Empty;

        public string? Description { get; set; }

        /// <summary>Tax head name as it stood at entry, snapshotted for the same reason
        /// <see cref="Expense.Category"/> is: renaming or retiring a head must never rewrite what a
        /// past purchase says it was taxed under.</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>The managed head this was classified under. Purchases share the expense rate
        /// table rather than owning a parallel one, because the statutory threshold aggregates per
        /// supplier per section across everything they were paid.</summary>
        public int? CategoryId { get; set; }

        /// <summary>Supplier name as it stood at entry — the snapshot for <see cref="VendorId"/>,
        /// or plain free text when no vendor record was chosen.</summary>
        public string? Vendor { get; set; }

        public int? VendorId { get; set; }

        public DateTime Date { get; set; } = DateTime.UtcNow;

        // ── Withholding tax ──
        // Snapshots, exactly as on Expense: editing a head's rate later must never restate tax that
        // has already been withheld and deposited.

        public bool WhtApplied { get; set; }

        /// <summary>Percentage applied, e.g. 4.00 for 4%.</summary>
        public decimal WhtRate { get; set; }

        public decimal WhtAmount { get; set; }

        /// <summary>Cash that actually left the account. Derived, never stored, so the price, the
        /// tax and the payment can never drift apart.</summary>
        public decimal NetPaid => Amount - WhtAmount;

        public bool WhtRateOverridden { get; set; }

        public string? WhtOverrideReason { get; set; }

        public string? WhtTaxSection { get; set; }

        public FilerStatus VendorFilerStatusAtEntry { get; set; } = FilerStatus.Unknown;

        public int? CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public byte[] RowVersion { get; set; } = [];

        // Navigation
        public Project? Project { get; set; }

        public FinanceAccount? AssetAccount { get; set; }

        public FinanceAccount? FinanceAccount { get; set; }

        public ExpenseCategory? ExpenseCategory { get; set; }

        public Vendor? VendorAccount { get; set; }

        public FinanceAttachment? Attachment { get; set; }
    }
}
