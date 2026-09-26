namespace DAMS.Domain.Enums
{
    /// <summary>The DAMS module that raised a notification. Purely informational — routing
    /// and permissions come from the related record, never from this.</summary>
    public enum NotificationModule
    {
        System = 0,
        Leads = 1,
        Bookings = 2,
        Payments = 3,
        Customers = 4,
        Projects = 5,
        Employees = 6,
        Finance = 7,
        Integrations = 8
    }
}
