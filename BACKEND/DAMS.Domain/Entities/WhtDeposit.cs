namespace DAMS.Domain.Entities
{
    /// <summary>
    /// Tax withheld from vendors and later deposited with FBR against a challan (CPR).
    /// <para>
    /// Withholding splits one payment in two: the expense is booked gross, but only the net
    /// leaves the bank. The withheld remainder is money the business is holding on FBR's behalf,
    /// so it stays in the account balance until it is deposited. This record is that deposit —
    /// it reduces the account balance without being a business expense, which is why it is not a
    /// row in the Expenses table and never appears in the P&amp;L.
    /// </para>
    /// </summary>
    public class WhtDeposit
    {
        public int Id { get; set; }

        /// <summary>The account the money was paid to FBR from.</summary>
        public int FinanceAccountId { get; set; }

        public decimal Amount { get; set; }

        public DateTime DepositDate { get; set; } = DateTime.UtcNow;

        /// <summary>Computerised Payment Receipt / challan number issued by the bank or FBR.</summary>
        public string? ChallanNumber { get; set; }

        /// <summary>The withholding period this challan settles. Informational: deposits are not
        /// matched line-by-line to individual expenses.</summary>
        public DateTime? PeriodFrom { get; set; }
        public DateTime? PeriodTo { get; set; }

        public string? Notes { get; set; }

        public int? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public byte[] RowVersion { get; set; } = [];

        public FinanceAccount? FinanceAccount { get; set; }
    }
}
