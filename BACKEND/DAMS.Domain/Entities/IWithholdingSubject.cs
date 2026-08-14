using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>
    /// A payment to a supplier that withholding tax can be deducted from.
    /// <para>
    /// Implemented by both <see cref="Expense"/> and <see cref="AssetPurchase"/> so one calculation
    /// serves both. They are opposite sides of the accounts — an expense consumes value, a purchase
    /// converts it — but to FBR they are the same event: money paid to a supplier under a tax
    /// section. That matters practically, not just tidily: the annual threshold is one allowance per
    /// supplier per section, so a vendor who sells the company cement AND furniture has to be
    /// aggregated across both or the deduction is short.
    /// </para>
    /// </summary>
    public interface IWithholdingSubject
    {
        int Id { get; }

        /// <summary>Gross — what the supplier invoiced, before anything was withheld.</summary>
        decimal Amount { get; set; }

        int? CategoryId { get; set; }

        int? VendorId { get; set; }

        DateTime Date { get; set; }

        bool WhtApplied { get; set; }

        decimal WhtRate { get; set; }

        decimal WhtAmount { get; set; }

        bool WhtRateOverridden { get; set; }

        string? WhtOverrideReason { get; set; }

        string? WhtTaxSection { get; set; }

        FilerStatus VendorFilerStatusAtEntry { get; set; }
    }
}
