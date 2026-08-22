using System.ComponentModel.DataAnnotations;
using DAMS.Application.Interfaces;
using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.EmployeeDtos
{
    /// <summary>
    /// Whether an employee can sign in to DAMS, as the Admin screen needs to see it.
    /// <see cref="None"/> is deliberately not a <see cref="UserAccountStatus"/>: it means the
    /// employee record has no login at all, which is a different thing from a login that
    /// exists and is waiting, working or switched off. Employment status stays separate.
    /// </summary>
    public enum StaffAccountAccess
    {
        None = 0,
        Invited = 1,
        Active = 2,
        Disabled = 3
    }

    public class StaffDirectoryDto
    {
        public int EmployeeId { get; set; }
        public int? UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Role { get; set; }
        public int? TeamId { get; set; }
        public string? TeamName { get; set; }
        public EmployeeStatus Status { get; set; }
        public bool CanOwnLeads { get; set; }
    }

    public class StaffAccountDto : StaffDirectoryDto
    {
        public string JobTitle { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public DateTime JoinDate { get; set; }
        public bool IsTeamManager { get; set; }

        /// <summary>Whether this employee can sign in, and if not, why not.</summary>
        public StaffAccountAccess Access { get; set; }

        /// <summary>
        /// When the outstanding activation link stops working, so an Admin can tell a waiting
        /// invitation from a stale one. Null unless an invitation is currently outstanding.
        /// </summary>
        public DateTime? InvitationExpiresAt { get; set; }
    }

    /// <summary>
    /// The outcome of provisioning staff access. Creating the account and getting the
    /// activation email delivered are two different things: the account survives a failed
    /// send, so the Admin needs to be told which one happened. Carries no token or password.
    /// </summary>
    public class StaffAccountProvisionResult
    {
        public StaffAccountDto Account { get; set; } = null!;

        /// <summary>False when the login already had its own password — an active account is
        /// linked to the employee without being sent an activation link.</summary>
        public bool InvitationRequired { get; set; }

        public bool InvitationSent { get; set; }

        public DateTime? InvitationExpiresAt { get; set; }

        /// <summary>Safe to show an Admin. Set only when the invitation could not be sent.</summary>
        public string? InvitationError { get; set; }

        public static StaffAccountProvisionResult NoInvitationNeeded(StaffAccountDto account) =>
            new() { Account = account, InvitationRequired = false, InvitationSent = false };

        /// <summary>Narrows an invitation outcome to what an Admin screen may see.</summary>
        public static StaffAccountProvisionResult From(StaffAccountDto account, StaffInvitationResult invitation) =>
            new()
            {
                Account = account,
                InvitationRequired = true,
                InvitationSent = invitation.EmailSent,
                InvitationExpiresAt = invitation.ExpiresAt,
                InvitationError = invitation.EmailSent ? null : invitation.Error
            };
    }

    public class CreateStaffAccountDto
    {
        /// <summary>Connect an existing login instead of creating a new one.</summary>
        public int? ExistingUserId { get; set; }

        /// <summary>Connect an existing employee instead of creating a new record.</summary>
        public int? ExistingEmployeeId { get; set; }

        [Required, StringLength(200, MinimumLength = 2)]
        public string FullName { get; set; } = string.Empty;

        [Required, EmailAddress, StringLength(200)]
        public string Email { get; set; } = string.Empty;

        // No password field, by design. A new login is created with no password at all and
        // the employee chooses their own through the activation link; nobody at the company,
        // Admins included, is able to pick or see another person's password.

        /// <summary>Admin, Manager, or Employee.</summary>
        [Required, StringLength(30)]
        public string Role { get; set; } = string.Empty;

        public int? TeamId { get; set; }

        [Required, StringLength(100)]
        public string JobTitle { get; set; } = "Sales Executive";

        [Required, StringLength(100)]
        public string Department { get; set; } = "Sales";

        [Required, StringLength(50, MinimumLength = 7)]
        public string Phone { get; set; } = string.Empty;

        public DateTime? JoinDate { get; set; }
    }

    public class UpdateStaffAccountDto
    {
        [Required, StringLength(30)]
        public string Role { get; set; } = string.Empty;

        /// <summary>Send null to leave unchanged, or -1 to remove team membership.</summary>
        public int? TeamId { get; set; }

        public EmployeeStatus? Status { get; set; }

        // Role, team and employment status are the security-sensitive things an Admin may
        // change here. Resetting someone else's password is not one of them.
    }

    public class LinkableUserDto
    {
        public int UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }

    public class CustomerLookupDto
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? CNIC { get; set; }
    }
}
