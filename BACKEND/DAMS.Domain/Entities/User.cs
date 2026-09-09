using DAMS.Domain.Enums;

namespace  DAMS.Domain.Entities
{
    public class User
    {
        public int UserId { get; set; }

        // Foreign Key to Role table
        public int RoleId { get; set; }
        public Role Role { get; set; } = null!;
        public string FullName { get; set; } = null!;

        public string Email { get; set; } = null!;

        // The canonical comparison key for Email, under DAMS.Domain.Identity.EmailIdentity.
        // Email above stays whatever the person typed, for display and delivery; this is what
        // decides identity, and a unique index on it is what stops two concurrent registrations
        // from creating two logins for the same mailbox.
        public string? NormalizedEmail { get; set; }

        // Null until an invited staff member chooses their own password. An Admin never sets
        // it, so an Invited account has no credential to verify against.
        public string? Password { get; set; }

        // Whether this login may authenticate. Employee.Status stays a separate, employment
        // concern; a login can be Disabled while the employment record is still Active.
        public UserAccountStatus AccountStatus { get; set; } = UserAccountStatus.Active;

        // When the person holding this mailbox proved they hold it, by redeeming a verification
        // link. Null means DAMS has never proven it — which is the truth for every account that
        // existed before client verification, and must stay the truth rather than being
        // backfilled from the fact that the row is old.
        public DateTime? EmailVerifiedAt { get; set; }

        public string? RefreshToken { get; set; }
        public DateTime? RefreshTokenExpiresAt { get; set; }
    }
}
