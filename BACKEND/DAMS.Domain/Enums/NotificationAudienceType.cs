namespace DAMS.Domain.Enums
{
    /// <summary>Who an admin-composed notification goes to.</summary>
    public enum NotificationAudienceType
    {
        SelectedUsers = 0,
        AllCustomers = 1,
        AllSalesEmployees = 2,
        AllManagers = 3,
        AllInternalStaff = 4,

        /// <summary>
        /// Kept so jobs saved before sales teams were removed still load. Resolving it
        /// now delivers to admins and sales managers. New sends must not use it.
        /// </summary>
        Team = 5,
        CustomersInProject = 6,
        CustomersOfBookings = 7,
        CustomersWithOverdueInstallments = 8,
        EmployeesAssignedToLeads = 9
    }
}
