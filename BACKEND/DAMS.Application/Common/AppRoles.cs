namespace DAMS.Application.Common
{
    /// <summary>
    /// The login roles the API authorises against. CRM code keeps using <see cref="LeadRoles"/>,
    /// which deliberately leaves Accountant out.
    /// </summary>
    public static class AppRoles
    {
        public const string Admin = "Admin";
        public const string Client = "Client";
        public const string Manager = "Manager";
        public const string Employee = "Employee";
        public const string Accountant = "Accountant";

        /// <summary>Everything outside the Lead CRM: bookings, finance, projects, customers, employees.</summary>
        public const string AdminOrAccountant = Admin + "," + Accountant;
    }
}
