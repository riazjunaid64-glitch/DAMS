using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>
    /// One dated movement of what the company OWES a partner on a booking commission.
    /// <para>
    /// A commission is a cost of the sale on the day it is agreed, not on the day the money happens
    /// to leave the bank. Deriving the expense from payouts made the formal statements say a
    /// commission agreed in June and paid in September was a September cost, and — worse — that a
    /// commission agreed and never yet paid was no cost at all and no liability either. These rows
    /// are that missing half: they raise the expense and the Commission Payable when the obligation
    /// arises, and the payout then simply settles the payable.
    /// </para>
    /// <para>
    /// Append-only and signed, like every other dated financial source in DAMS: a correction is a
    /// new row for the difference, never an edit of an old one, so a period that has already been
    /// reported keeps reporting what it reported. <see cref="AccruedOn"/> is a PAKISTAN business
    /// date and is subject to the same bounds as every other posting date — never in the future,
    /// never before the committed opening balances.
    /// </para>
    /// </summary>
    public class CommissionAccrual
    {
        public int Id { get; set; }

        public int CommissionId { get; set; }

        /// <summary>Signed: positive raises the obligation, negative releases it.</summary>
        public decimal Amount { get; set; }

        /// <summary>The Pakistan business date the obligation moved on.</summary>
        public DateTime AccruedOn { get; set; }

        public CommissionAccrualKind Kind { get; set; }

        public string? Reason { get; set; }

        public int? RecordedByUserId { get; set; }

        public string? RecordedByName { get; set; }

        public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

        public BookingCommission Commission { get; set; } = null!;
    }
}
