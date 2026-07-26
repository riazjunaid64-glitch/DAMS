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
        Team = 5,
        CustomersInProject = 6,
        CustomersOfBookings = 7,
        CustomersWithOverdueInstallments = 8,
        EmployeesAssignedToLeads = 9
    }
}
