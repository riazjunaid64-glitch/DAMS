namespace DAMS.Domain.Enums
{
    public enum LeadIntakeHoldStatus
    {
        /// <summary>Waiting for a person to decide which lead the enquiry belongs to.</summary>
        Open = 0,

        /// <summary>Added to the lead a person chose.</summary>
        Resolved = 1,

        /// <summary>Deliberately not added to any lead, for example spam.</summary>
        Dismissed = 2
    }
}
