using System.ComponentModel.DataAnnotations;
using DAMS.Application.Services;

namespace DAMS.Application.DTOs.Auth
{
    /// <summary>
    /// What an invited employee sends to finish setting up their login. Deliberately only two
    /// fields: the token identifies the invitation, and therefore the account, so there is no
    /// user, employee, email or role for the caller to name — and nothing they could name that
    /// would be believed.
    /// </summary>
    public class ActivateStaffAccountRequestDto
    {
        /// <summary>The secret from the emailed activation link.</summary>
        [Required]
        [StringLength(200, MinimumLength = 1)]
        public string Token { get; set; } = null!;

        /// <summary>
        /// The password the employee chooses for themselves. There is no confirmation field:
        /// re-typing is something the sign-up form checks, not something the server can.
        /// </summary>
        [Required]
        [StringLength(StaffInvitationService.MaxPasswordBytes,
            MinimumLength = StaffInvitationService.MinPasswordLength)]
        public string Password { get; set; } = null!;
    }
}
