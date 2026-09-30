namespace DAMS.Domain.Enums
{
    public enum BookingTermsChangeSource
    {
        /// <summary>The Set terms / Edit terms form on a booking that is awaiting its booking amount.</summary>
        Terms = 0,

        /// <summary>Generating or regenerating the installment plan with a different price or discount.</summary>
        Plan = 1
    }
}
