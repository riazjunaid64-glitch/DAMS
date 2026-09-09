using System.ComponentModel.DataAnnotations;

namespace DAMS.Application.DTOs.Auth
{
    /// <summary>
    /// What a public client registration submits. Two fields, and deliberately no password.
    ///
    /// <para>
    /// The password field is gone rather than ignored. Anything a registration form can supply is
    /// supplied by whoever filled the form in — who is, at that moment, an unverified stranger who
    /// may have typed somebody else's address. The password is chosen later, on the verification
    /// page, by whoever can actually read the mail sent to it.
    /// </para>
    /// </summary>
    public class RegisterRequestDto
    {
        [Required]
        [StringLength(150, MinimumLength = 1)]
        public string FullName { get; set; } = null!;

        [Required]
        [EmailAddress]
        [StringLength(256, MinimumLength = 3)]
        public string Email { get; set; } = null!;
    }
}
