namespace DAMS.Domain.Entities
{
    /// <summary>
    /// When the alert scan last evaluated a lead for each time-based rule. A lead that stays
    /// overdue stays eligible, so without this the oldest few hundred would fill every scan's
    /// batch for ever and nobody behind them would be looked at. It records that the lead was
    /// evaluated, not that anyone was notified — a lead with nobody to tell must not block the
    /// queue either. Kept apart from <see cref="Lead"/> so a scan never bumps the lead's
    /// RowVersion under a user who is editing it.
    /// </summary>
    public class LeadAlertCheck
    {
        public int LeadId { get; set; }

        /// <summary>The <see cref="Lead.AssignedAt"/> of the assignment last evaluated for first
        /// contact. A new assignment is a new deadline for its new owner, so once the lead's
        /// value moves on it is evaluated again. Stores the assignment rather than the scan time
        /// so the answer never depends on two clocks agreeing.</summary>
        public DateTime? FirstContactAssignmentCheckedAt { get; set; }

        /// <summary>Inactivity is re-evaluated once per day, matching its daily alert.</summary>
        public DateTime? InactivityCheckedAt { get; set; }

        public Lead Lead { get; set; } = null!;
    }
}
