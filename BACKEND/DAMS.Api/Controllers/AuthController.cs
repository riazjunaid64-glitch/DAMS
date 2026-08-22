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
        private readonly IAuthService _authService;
        private readonly IStaffInvitationService _invitations;

        public AuthController(IAuthService authService, IStaffInvitationService invitations)
        {
            _authService = authService;
            _invitations = invitations;
        }

        [HttpPost("register")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> Register(RegisterRequestDto request)
        {
            try
            {
                await _authService.RegisterAsync(request);
                return Ok(new { message = "Registration completed successfully. You can now sign in." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
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
