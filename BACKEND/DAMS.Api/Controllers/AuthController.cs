using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using DAMS.Application.DTOs.Auth;
using DAMS.Application.Interfaces;
using System.Security.Claims;

namespace DAMS.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private const int RefreshTokenDays = 15;
        /// <summary>
        /// The one sentence every registration and resend answers with, whatever actually
        /// happened. A response that varies — "email already exists" against "verification sent" —
        /// turns these two endpoints into a way to test which addresses have DAMS accounts, which
        /// is worth knowing to anybody planning to target a buyer.
        /// </summary>
        private const string NeutralVerificationMessage =
            "If the address can be registered, verification instructions have been sent. "
            + "Check your inbox to confirm the address and choose your password.";

        private readonly IAuthService _authService;
        private readonly IStaffInvitationService _invitations;
        private readonly IClientEmailVerificationService _clientVerification;

        public AuthController(
            IAuthService authService,
            IStaffInvitationService invitations,
            IClientEmailVerificationService clientVerification)
        {
            _authService = authService;
            _invitations = invitations;
            _clientVerification = clientVerification;
        }

        /// <summary>
        /// Opens a public client registration. Nothing usable is created: the account exists in
        /// <c>PendingEmailVerification</c> with no password, and stays that way until somebody
        /// proves they can read the address by spending the emailed link.
        ///
        /// <para>
        /// Rate limited on the same bucket as login, because this endpoint sends mail to an
        /// address the caller chose — without a limit it is a way to use DAMS to flood somebody
        /// else's inbox.
        /// </para>
        /// </summary>
        [HttpPost("register")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> Register(RegisterRequestDto request)
        {
            try
            {
                await _authService.RegisterAsync(request);
            }
            catch (ArgumentException ex)
            {
                // Malformed input the caller can fix, and which says nothing about any account.
                return BadRequest(new { message = ex.Message });
            }

            return Ok(new { message = NeutralVerificationMessage });
        }

        /// <summary>
        /// Sends another verification link. Answers identically for an unknown address, an
        /// address whose account is already active, and one genuinely waiting — the service
        /// decides silently whether anything is actually sent.
        /// </summary>
        [HttpPost("resend-verification")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> ResendVerification(
            ResendClientVerificationRequestDto request, CancellationToken cancellationToken)
        {
            await _clientVerification.ResendAsync(request.Email, cancellationToken);
            return Ok(new { message = NeutralVerificationMessage });
        }

        /// <summary>
        /// Where a customer trades their emailed link for the password they will actually sign in
        /// with. Anonymous by necessity — they have no usable account yet — and rate limited on
        /// the login bucket, because a link is a credential and this is where one is spent.
        /// Verifying is not signing in: no cookie and no access token comes back.
        /// </summary>
        [HttpPost("verify-email")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> VerifyEmail(
            VerifyClientEmailRequestDto request, CancellationToken cancellationToken)
        {
            // Checked before the token is spent. A mistyped confirmation would otherwise consume a
            // single-use credential on a password the customer did not mean to set, leaving them
            // locked out of an account they just proved they own.
            if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
                return BadRequest(new { message = "The two passwords do not match." });

            var result = await _clientVerification.VerifyAsync(request.Token, request.Password, cancellationToken);

            // The service already decided what an anonymous caller may be told; the controller
            // repeats it rather than adding detail of its own.
            return result.Verified
                ? Ok(new { message = "Your email address is confirmed. Sign in with the password you just chose." })
                : BadRequest(new { message = result.Error });
        }

        /// <summary>
        /// Where an invited employee trades their activation link for a password of their own.
        /// Anonymous by necessity — they have no account to sign in with yet — and rate limited
        /// on the same bucket as login, because a link is a credential and this is where one is
        /// spent. Activating is not signing in: no cookie and no access token comes back.
        /// </summary>
        [HttpPost("activate-staff")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> ActivateStaff(
            ActivateStaffAccountRequestDto request,
            CancellationToken cancellationToken)
        {
            var result = await _invitations.ActivateAsync(request.Token, request.Password, cancellationToken);

            // The service already decided what an anonymous caller may be told; the controller
            // repeats it rather than adding detail of its own.
            return result.Activated
                ? Ok(new { message = "Your account is ready. Sign in with your email and the password you just chose." })
                : BadRequest(new { message = result.Error });
        }

        [HttpPost("login")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> Login(LoginRequestDto request)
        {
            try
            {
                var tokens = await _authService.LoginAsync(request);

                if (tokens == null)
                    return Unauthorized(new { message = "Invalid credentials" });

                SetRefreshCookie(tokens.RefreshToken);

                return Ok(new { accessToken = tokens.AccessToken, expiresInMinutes = tokens.ExpiresInMinutes });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("refresh")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> Refresh()
        {
            var refreshToken = Request.Cookies["refreshToken"];
            if (string.IsNullOrEmpty(refreshToken))
                return Unauthorized(new { message = "No refresh token" });

            var tokens = await _authService.RefreshTokenAsync(new RefreshTokenRequestDto { RefreshToken = refreshToken });
            if (tokens == null)
            {
                Response.Cookies.Delete("refreshToken");
                return Unauthorized(new { message = "Invalid or expired refresh token" });
            }

            SetRefreshCookie(tokens.RefreshToken);

            return Ok(new { accessToken = tokens.AccessToken, expiresInMinutes = tokens.ExpiresInMinutes });
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var refreshToken = Request.Cookies["refreshToken"];
            if (!string.IsNullOrWhiteSpace(refreshToken))
                await _authService.RevokeRefreshTokenAsync(refreshToken);

            Response.Cookies.Delete("refreshToken", new CookieOptions { Path = "/api/Auth" });
            return Ok();
        }

        [Authorize(Roles = "Admin")]
        [HttpGet("admin-only")]
        public IActionResult AdminOnly()
        {
            return Ok("Only Admin can access this");
        }

        [Authorize]
        [HttpGet("profile")]
        public IActionResult Profile()
        {
            return Ok(new
            {
                userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                email = User.FindFirst(ClaimTypes.Email)?.Value,
                firstName = User.FindFirst(ClaimTypes.Name)?.Value,
                role = User.FindFirst(ClaimTypes.Role)?.Value
            });
        }

        private void SetRefreshCookie(string refreshToken)
        {
            Response.Cookies.Append("refreshToken", refreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                SameSite = Request.IsHttps ? SameSiteMode.None : SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddDays(RefreshTokenDays),
                Path = "/api/Auth"
            });
        }
    }
}
