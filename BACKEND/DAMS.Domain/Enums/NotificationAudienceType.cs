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
        /// Kept so jobs saved before sales teams were removed still load as history. The
        /// migration cancelled every unfinished one, and resolving it is refused.
        /// </summary>
        Team = 5,
        CustomersInProject = 6,
        CustomersOfBookings = 7,
        CustomersWithOverdueInstallments = 8,
        EmployeesAssignedToLeads = 9
    }
}
