using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    public class Expense : IWithholdingSubject
    {
        public int Id { get; set; }

        // Nullable: company-wide costs (salary, marketing, office) need not be tied to a project.
        public int? ProjectId { get; set; }

        // Nullable for legacy and system-generated expenses that predate account assignment.
        public int? FinanceAccountId { get; set; }

        /// <summary>The GROSS cost of the purchase — what the vendor invoiced, before any tax was
        /// withheld. This is the figure that belongs in the P&amp;L. Cash actually paid is
        /// <see cref="NetPaid"/>.</summary>
        public decimal Amount { get; set; }

        /// <summary>Category name as it stood when the expense was recorded. Kept even when
        /// <see cref="CategoryId"/> is set, so renaming or retiring a category never rewrites
        /// history — and so pre-existing free-text rows keep working unchanged.</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>Set when the expense was booked against a managed category. Null for rows
        /// entered before categories existed, or for one-off free-text heads.</summary>
        public int? CategoryId { get; set; }

        public string? Description { get; set; }

        /// <summary>Vendor name as it stood at entry — the snapshot for <see cref="VendorId"/>,
        /// or plain free-text when no vendor record was chosen.</summary>
        public string? Vendor { get; set; }

        public int? VendorId { get; set; }

        public DateTime Date { get; set; } = DateTime.UtcNow;

        // ── Withholding tax (s.153 and friends) ──
        // Every field here is a snapshot taken at entry. Editing the category's rate later must
        // never change tax that has already been withheld, reported or deposited.

        /// <summary>True when tax was withheld from this payment. False for historic rows, for
        /// non-applicable heads, and whenever the annual threshold has not yet been crossed.</summary>
        public bool WhtApplied { get; set; }

        /// <summary>Percentage applied, e.g. 4.00 for 4%.</summary>
        public decimal WhtRate { get; set; }

        public decimal WhtAmount { get; set; }

        /// <summary>Cash that actually left the account. Derived, never stored, so gross, tax and
        /// net can never drift apart.</summary>
        public decimal NetPaid => Amount - WhtAmount;

        /// <summary>True when the operator changed the rate or amount away from the computed
        /// default — the audit trail for why a figure differs from the rate table.</summary>
        public bool WhtRateOverridden { get; set; }

        public string? WhtOverrideReason { get; set; }

        /// <summary>Section snapshot ("153(1)(a)"), needed for the s.165 statement and vendor
        /// certificates without re-reading a category that may since have been edited.</summary>
        public string? WhtTaxSection { get; set; }

        public FilerStatus VendorFilerStatusAtEntry { get; set; } = FilerStatus.Unknown;

        // Admin who recorded this expense (no FK; resolved at read time like other audit fields).
        public int? CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Optimistic-concurrency token. Editing an expense moves the gross cost, the withheld tax
        /// and the paying account's balance all at once, so two admins with the same row open must
        /// not be able to silently overwrite one another — the loser is told to reload instead. The
        /// same protection every other mutable financial record already carries.
        /// </summary>
        public byte[] RowVersion { get; set; } = [];

        // Navigation
        public Project? Project { get; set; }

        public FinanceAttachment? Attachment { get; set; }

        public FinanceAccount? FinanceAccount { get; set; }

        public ExpenseCategory? ExpenseCategory { get; set; }

        public Vendor? VendorAccount { get; set; }
    }
}
