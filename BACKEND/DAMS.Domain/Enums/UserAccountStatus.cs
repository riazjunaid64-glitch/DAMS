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

        Disabled = 2,

        /// <summary>
        /// A public client registration whose email address has not been proven yet. The account
        /// exists so the address is reserved, but it has no usable password and may neither sign
        /// in nor refresh until the person holding that mailbox redeems a verification link and
        /// chooses the password themselves.
        /// <para>
        /// Appended rather than inserted: the three values above are already stored as integers
        /// in <c>Users.AccountStatus</c>, and renumbering them would silently re-label every
        /// existing login.
        /// </para>
        /// </summary>
        PendingEmailVerification = 3
    }
}
