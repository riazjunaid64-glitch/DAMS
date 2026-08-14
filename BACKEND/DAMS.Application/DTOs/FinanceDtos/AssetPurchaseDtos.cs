using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.FinanceDtos
{
    public class CreateAssetPurchaseDto
    {
        public int? ProjectId { get; set; }

        /// <summary>The fixed-asset account the value lands in — Office Equipment, Cost of Plot,
        /// and so on.</summary>
        public int AssetAccountId { get; set; }

        /// <summary>The bank or cash account that paid.</summary>
        public int FinanceAccountId { get; set; }

        /// <summary>Gross — the supplier's invoice total, before tax is withheld. The asset is
        /// capitalised at this figure.</summary>
        public decimal Amount { get; set; }

        /// <summary>What was bought, in plain words: "3 office desks".</summary>
        public string ItemName { get; set; } = string.Empty;

        public string? Description { get; set; }

        /// <summary>Tax head from the managed rate table, shared with expenses. Required for a new
        /// purchase: without it there is no rate behind the payment.</summary>
        public int? CategoryId { get; set; }

        /// <summary>Free-text head. Ignored when <see cref="CategoryId"/> is supplied.</summary>
        public string Category { get; set; } = string.Empty;

        public string? Vendor { get; set; }

        public int? VendorId { get; set; }

        public DateTime? Date { get; set; }

        // ── Withholding ──
        // Omit all three and the server computes the tax itself, exactly as for an expense.
        public decimal? WhtRate { get; set; }

        public decimal? WhtAmount { get; set; }

        public string? WhtOverrideReason { get; set; }
    }

    public sealed class UpdateAssetPurchaseDto : CreateAssetPurchaseDto
    {
    }

    public sealed class AssetPurchaseResponseDto
    {
        public int Id { get; set; }
        public int? ProjectId { get; set; }
        public string? ProjectName { get; set; }

        public int AssetAccountId { get; set; }
        public string? AssetAccountName { get; set; }

        public int FinanceAccountId { get; set; }
        public string? FinanceAccountName { get; set; }
        public string? AccountHolderName { get; set; }

        /// <summary>Gross — what the asset is carried at.</summary>
        public decimal Amount { get; set; }

        public string ItemName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Category { get; set; } = string.Empty;
        public int? CategoryId { get; set; }
        public string? Vendor { get; set; }
        public int? VendorId { get; set; }
        public DateTime Date { get; set; }

        public bool WhtApplied { get; set; }
        public decimal WhtRate { get; set; }
        public decimal WhtAmount { get; set; }

        /// <summary>Cash that left the account: gross − withheld.</summary>
        public decimal NetPaid { get; set; }

        public bool WhtRateOverridden { get; set; }
        public string? WhtOverrideReason { get; set; }
        public string? WhtTaxSection { get; set; }
        public FilerStatus VendorFilerStatusAtEntry { get; set; }

        public DateTime CreatedAt { get; set; }
        public FinanceAttachmentDto? Attachment { get; set; }
    }

    /// <summary>One row of the Fixed Assets table on the finance dashboard.</summary>
    public sealed class AssetPurchaseLineDto
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public string ProjectName { get; set; } = "General";
        public int AssetAccountId { get; set; }
        public string AssetAccountName { get; set; } = string.Empty;
        public int FinanceAccountId { get; set; }
        public string? FinanceAccountName { get; set; }
        public string? AccountHolderName { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int? CategoryId { get; set; }
        public string? Description { get; set; }
        public string? Vendor { get; set; }
        public int? VendorId { get; set; }
        public decimal Amount { get; set; }
        public decimal WhtAmount { get; set; }
        public decimal WhtRate { get; set; }
        public decimal NetPaid { get; set; }
        public string? WhtTaxSection { get; set; }
        public int? ProjectId { get; set; }
        public FinanceAttachmentDto? Attachment { get; set; }
    }
}
