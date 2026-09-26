namespace DAMS.Domain.Enums
{
    /// <summary>
    /// Every business event DAMS can notify about. Values are grouped in tens and are
    /// permanent — an admin rule, a template and years of delivery history are all keyed on
    /// the stored number, so renumbering an existing member silently rewrites the past.
    /// Add new events at the end of their group.
    /// </summary>
    public enum NotificationType
    {
        // Payments and receipts
        PaymentReceipt = 0,

        // Bookings
        BookingRequestReceived = 10,
        BookingApproved = 11,
        BookingRejected = 12,
        BookingCancelled = 13,
        PossessionGiven = 14,
        SaleCompleted = 15,

        // Installments
        InstallmentDue = 20,
        InstallmentOverdue = 21,

        // Leads
        LeadCreated = 30,
        LeadAssigned = 31,
        LeadReassigned = 32,
        LeadStageChanged = 33,
        LeadConverted = 34,
        LeadClosed = 35,
        LeadInactive = 36,
        FirstContactDue = 37,
        FirstContactOverdue = 38,
        LeadHeldForReview = 39,

        // Follow-ups
        FollowUpAssigned = 40,
        FollowUpDue = 41,
        FollowUpOverdue = 42,
        FollowUpMissed = 43,

        // Site visits
        SiteVisitScheduled = 50,
        SiteVisitUpdated = 51,
        SiteVisitReminder = 52,
        SiteVisitMissed = 53,

        // Collaboration
        UserMentioned = 60,
        EmployeeTaskAssigned = 70,

        // Projects
        ProjectUpdated = 80,

        // Broadcast and account
        AdminAnnouncement = 90,
        AccountSecurity = 100,

        // Supervision
        ManagerAttentionRequired = 110
    }
}
