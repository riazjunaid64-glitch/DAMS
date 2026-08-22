namespace DAMS.Domain.Enums
{
    /// <summary>
    /// Whether a login may authenticate. Separate from <see cref="EmployeeStatus"/>, which
    /// describes employment rather than access.
    /// </summary>
    public enum UserAccountStatus
    {
        /// <summary>Deliberately 0 so neither the CLR nor a SQL default can turn an existing
        /// login into an un-activated one.</summary>
        Active = 0,

        /// <summary>Access granted by an Admin, but the employee has not yet chosen a password.</summary>
        Invited = 1,

        Disabled = 2
    }
}
