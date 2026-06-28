using DAMS.Application.DTOs.Auth;

namespace DAMS.Application.Interfaces
{
    public interface IAuthService
    {
        Task RegisterAsync(RegisterRequestDto request);
        Task<AuthResponseDto?> LoginAsync(LoginRequestDto request);
        Task<AuthResponseDto?> RefreshTokenAsync(RefreshTokenRequestDto request);
    }
}
