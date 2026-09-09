using System.ComponentModel.DataAnnotations;
using DAMS.Application.Services;

namespace DAMS.Application.DTOs.Auth
{
    /// <summary>
    /// What the verification page sends to turn an emailed link into a usable account. The token
    /// identifies the credential and therefore the account, so there is no email, user or role for
    /// the caller to name — and nothing they could name that would be believed.
    /// </summary>
    public class VerifyClientEmailRequestDto
    {
        /// <summary>The secret from the emailed verification link.</summary>
        [Required]
        [StringLength(200, MinimumLength = 1)]
        public string Token { get; set; } = null!;

        /// <summary>
        /// The password the customer chooses for themselves — the first this account has ever had.
        /// <para>
        /// Only the floor is stated here, because only the floor means the same thing in both
        /// places. The ceiling is bcrypt's 72 <em>bytes</em>, which no character count can
        /// express: a StringLength cap would answer "a maximum length of 72" to somebody whose
        /// accented or emoji password was nowhere near 72 characters. The service measures the
        /// encoded length and owns that boundary alone.
        /// </para>
        /// </summary>
        [Required]
        [MinLength(ClientEmailVerificationService.MinPasswordLength)]
        public string Password { get; set; } = null!;

        /// <summary>
        /// The same password again. Unlike staff activation, the customer-facing flow carries the
        /// confirmation to the server as well: the story asks for it, and checking it here means a
        /// mistyped confirmation cannot spend a single-use token on a password nobody meant.
        /// </summary>
        [Required]
        public string ConfirmPassword { get; set; } = null!;
    }

    /// <summary>Asks for a fresh verification email. Answered identically whether or not the
    /// address is registered.</summary>
    public class ResendClientVerificationRequestDto
    {
        [Required]
        [EmailAddress]
        [StringLength(256, MinimumLength = 3)]
        public string Email { get; set; } = null!;
    }
}
