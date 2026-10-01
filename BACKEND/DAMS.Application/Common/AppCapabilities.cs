namespace DAMS.Application.Common
{
    /// <summary>
    /// The same permission table the web app uses (<c>can(role, capability)</c>).
    /// Callers ask for a capability. They do not compare role names themselves.
    /// </summary>
    public static class AppCapabilities
    {
        public const string Customers = "customers";

        public static bool Can(string? role, string capability)
        {
            if (string.IsNullOrEmpty(role))
                return false;

            return capability switch
            {
                Customers => role is AppRoles.Admin or AppRoles.Accountant,
                _ => false
            };
        }
    }
}
