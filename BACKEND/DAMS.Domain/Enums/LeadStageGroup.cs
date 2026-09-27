namespace DAMS.Domain.Enums
{
    /// <summary>
    /// The four-step view of the pipeline a salesperson works with: New, In Progress, Won, Lost.
    /// Derived from <see cref="LeadStage"/> and never stored, so the detailed stage history stays
    /// intact for managers and reporting.
    /// </summary>
    public enum LeadStageGroup
    {
        New = 0,
        InProgress = 1,
        Won = 2,
        Lost = 3,

        /// <summary>Appended so existing stored values of the groups above keep their meaning.</summary>
        Dormant = 4
    }
}
