namespace DAMS.Domain.Entities
{
    /// <summary>
    /// A single-use credential proving that whoever redeems it can read the mailbox a public
    /// client registered with.
    ///
    /// <para>
    /// Deliberately its own table rather than a reuse of <see cref="StaffInvitation"/>. Staff
    /// provisioning starts with an Admin who already vouches for the person; client registration
    /// starts with an anonymous form submission that nobody has vouched for. They differ in who
    /// may issue one, what proves the account may be opened, what the account is allowed to reach
    /// afterwards, and how a stranger's attempt has to be answered. Sharing one table would mean
    /// one lookup that has to be right for both, which is exactly the shape of bug this split
    /// prevents.
    /// </para>
    ///
    /// <para>
    /// Only <see cref="TokenHash"/> is stored. The raw token exists in memory long enough to
    /// build the emailed link and nowhere else, so a database read cannot reconstruct a working
    /// credential.
    /// </para>
    /// </summary>
    public class ClientEmailVerification
    {
        public int Id { get; set; }

        /// <summary>The one login this credential can ever open.</summary>
        public int UserId { get; set; }

        /// <summary>SHA-256 of the emailed token, base64. Never the token itself.</summary>
        public string TokenHash { get; set; } = null!;

        public DateTime CreatedAt { get; set; }

        public DateTime ExpiresAt { get; set; }

        /// <summary>Set the moment the credential is spent. Non-null means it can never work again.</summary>
        public DateTime? VerifiedAt { get; set; }

        /// <summary>
        /// Set when a newer verification email supersedes this one, or when the account's access
        /// is withdrawn. Non-null means it can never work again.
        /// </summary>
        public DateTime? RevokedAt { get; set; }

        public User User { get; set; } = null!;
    }
}
