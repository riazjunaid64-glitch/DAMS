namespace DAMS.Domain.Enums
{
    /// <summary>
    /// Where a lead sits in the sales pipeline. Kept deliberately separate from
    /// <see cref="LeadAssignmentState"/>, <see cref="LeadQualification"/> and the closure reason
    /// so one field never has to carry several different business meanings.
    /// </summary>
    public enum LeadStage
    {
        New = 0,
        FirstContactPending = 1,
        Contacted = 2,
        Qualified = 3,
        SiteVisitScheduled = 4,
        SiteVisitCompleted = 5,
        Negotiation = 6,
        DocumentsInProgress = 7,
        BookingPending = 8,
        Won = 9,
        Lost = 10,
        Dormant = 11
    }
}
