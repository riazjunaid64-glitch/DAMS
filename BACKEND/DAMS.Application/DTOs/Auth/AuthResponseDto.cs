namespace DAMS.Application.DTOs.Auth
{
    public class AuthResponseDto
    {
        public string AccessToken { get; set; } = null!;
        public string RefreshToken { get; set; } = null!;
        public int ExpiresInMinutes { get; set; }
    }
}
