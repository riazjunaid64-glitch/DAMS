namespace DAMS.Domain.Entities
{
    /// <summary>
    /// One staff activation invitation. Only a cryptographic hash of the emailed token is
    /// stored, so reading the database can never reconstruct a working activation link.
    /// Acceptance and revocation are recorded rather than deleted — the row is the record of
    /// who was granted access, by whom, and whether they took it up.
    /// </summary>
    public class StaffInvitation
    {
        public int Id { get; set; }

        /// <summary>The login being activated.</summary>
        public int UserId { get; set; }

        public User User { get; set; } = null!;

        /// <summary>The Admin who granted DAMS access.</summary>
        public int InvitedByUserId { get; set; }

        public User InvitedByUser { get; set; } = null!;

        /// <summary>Hash of the token that was emailed. The raw token is never persisted.</summary>
        public string TokenHash { get; set; } = null!;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime ExpiresAt { get; set; }

        /// <summary>Set once the employee has chosen their password, which makes the
        /// invitation single-use.</summary>
        public DateTime? AcceptedAt { get; set; }

        /// <summary>Set when an Admin withdraws the invitation, or when a resend supersedes it.</summary>
        public DateTime? RevokedAt { get; set; }
    }
}
