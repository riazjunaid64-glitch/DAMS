using DAMS.Application.DTOs.Auth;

namespace DAMS.Application.Interfaces
{
    public interface IAuthService
    {
        Task RegisterAsync(RegisterRequestDto request);
        Task<AuthResponseDto?> LoginAsync(LoginRequestDto request);
        Task<AuthResponseDto?> RefreshTokenAsync(RefreshTokenRequestDto request);
        /// <summary>True when this refresh token still identified a login and that session was ended.</summary>
        Task<bool> RevokeRefreshTokenAsync(string refreshToken);

        /// <summary>
        /// Ends every access token for this login. Used when logout can see who is signed in
        /// but the refresh cookie is an older one that this account has already replaced.
        /// </summary>
        Task RevokeUserSessionAsync(int userId);
    }
}
