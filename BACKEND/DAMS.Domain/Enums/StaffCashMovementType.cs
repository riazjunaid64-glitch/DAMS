namespace DAMS.Domain.Enums
{
    public enum StaffCashMovementType
    {
        /// <summary>Money moves from a company cash/bank account into the staff float.</summary>
        FundsGiven = 1,

        /// <summary>Unspent money moves from the staff float back to a company account.</summary>
        FundsReturned = 2
    }
}
