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

        // Null until an invited staff member chooses their own password. An Admin never sets
        // it, so an Invited account has no credential to verify against.
        public string? Password { get; set; }

        // Whether this login may authenticate. Employee.Status stays a separate, employment
        // concern; a login can be Disabled while the employment record is still Active.
        public UserAccountStatus AccountStatus { get; set; } = UserAccountStatus.Active;

        public string? RefreshToken { get; set; }
        public DateTime? RefreshTokenExpiresAt { get; set; }
    }
}
