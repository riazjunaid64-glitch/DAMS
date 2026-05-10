using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using DAMS.Application.DTOs.Auth;
using DAMS.Application.Services;
using DAMS.Application.Interfaces;
using System.Security.Claims;

namespace DAMS.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;


        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("register")]
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
[HttpPost("login")]
public IActionResult Login(LoginRequestDto request)
{
    try
    {
        var tokens = _authService.Login(request);

        if (tokens == null)
            return Unauthorized("Invalid credentials");

        return Ok(tokens);
    }
    catch (Exception ex)
    {
        return BadRequest(new { message = ex.Message });
    }
}

[HttpPost("refresh")]
public IActionResult Refresh(RefreshTokenRequestDto request)
{
    var tokens = _authService.RefreshToken(request);
    if (tokens == null)
        return Unauthorized("Invalid refresh token");

    return Ok(tokens);
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




    }
}
