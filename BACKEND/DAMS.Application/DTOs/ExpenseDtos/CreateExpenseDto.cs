namespace DAMS.Application.DTOs.ExpenseDtos
{
    public class CreateExpenseDto
    {
        public int? ProjectId { get; set; }

        public int? FinanceAccountId { get; set; }

        /// <summary>Gross — what the vendor invoiced, before tax is withheld.</summary>
        public decimal Amount { get; set; }

        /// <summary>Free-text head. Ignored when <see cref="CategoryId"/> is supplied, because the
        /// category's own name is snapshotted instead.</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>Preferred over <see cref="Category"/>: links the expense to the managed rate
        /// table so withholding can be computed.</summary>
        public int? CategoryId { get; set; }

        public string? Description { get; set; }

        /// <summary>Free-text vendor. Ignored when <see cref="VendorId"/> is supplied.</summary>
        public string? Vendor { get; set; }

        public int? VendorId { get; set; }

        public DateTime? Date { get; set; }

        // ── Withholding ──
        // Omit all three and the server computes the tax itself. That keeps the API honest for
        // callers that are not the expense form, and stops a client from silently under-deducting.

        /// <summary>Rate the operator confirmed on screen. When it differs from the rate table,
        /// the expense is flagged as overridden.</summary>
        public decimal? WhtRate { get; set; }

        /// <summary>Tax amount the operator confirmed, typically straight off the vendor invoice.
        /// Takes precedence over <see cref="WhtRate"/>; the stored rate is back-computed from it.</summary>
        public decimal? WhtAmount { get; set; }

        public string? WhtOverrideReason { get; set; }
    }
}
