using DAMS.Application.DTOs.Auth;

namespace DAMS.Application.Interfaces
{
    public interface IAuthService
    {
        Task RegisterAsync(RegisterRequestDto request);
        AuthResponseDto? Login(LoginRequestDto request);
        AuthResponseDto? RefreshToken(RefreshTokenRequestDto request);
    }
}
