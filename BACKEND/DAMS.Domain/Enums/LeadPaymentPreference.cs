namespace DAMS.Domain.Enums
{
    /// <summary>How the person would like to pay, as far as they have said.</summary>
    public enum LeadPaymentPreference
    {
        Unknown = 0,
        Installments = 1,
        NeedsDetails = 2,
        Cash = 3
    }
}
