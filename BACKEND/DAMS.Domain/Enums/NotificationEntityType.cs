namespace DAMS.Domain.Enums
{
    /// <summary>
    /// What a notification points at. The pair (type, id) is what the deep-link builder and
    /// the authorisation check both work from, so a notification can relate to any DAMS
    /// record without the platform knowing anything about that record's module.
    /// </summary>
    public enum NotificationEntityType
    {
        None = 0,
        Lead = 1,
        LeadFollowUp = 2,
        LeadSiteVisit = 3,
        LeadComment = 4,
        Customer = 5,
        BookingRequest = 6,
        Booking = 7,
        Payment = 8,
        Installment = 9,
        Project = 10,
        Unit = 11,
        EmployeeTask = 12,
        Announcement = 13,
        Account = 14,
        LeadIntakeHold = 15
    }
}
