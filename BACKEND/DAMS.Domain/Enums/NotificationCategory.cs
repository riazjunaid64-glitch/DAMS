namespace DAMS.Domain.Enums
{
    /// <summary>
    /// The grouping a notification belongs to. Categories are what users see in the inbox
    /// filters and what their per-category preferences switch on and off, so they are
    /// deliberately coarse — one row per thing a person would reasonably want to mute.
    /// </summary>
    public enum NotificationCategory
    {
        PaymentsAndReceipts = 0,
        BookingUpdates = 1,
        InstallmentReminders = 2,
        LeadAssignments = 3,
        FollowUps = 4,
        SiteVisits = 5,
        Mentions = 6,
        EmployeeTasks = 7,
        ProjectUpdates = 8,
        Announcements = 9,
        AccountAndSecurity = 10,
        ManagerEscalations = 11,
        Integrations = 12
    }
}
