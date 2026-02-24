using DAMS.Domain.Entities;
using DAMS.Infrastructure.Data;
using DAMS.Application.DTOs.Auth;
using DAMS.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace DAMS.Application.Services
{
    public class AuthService : IAuthService
    {
        private readonly AppDbContext _context;
        private readonly ITokenService _tokenService;
        private const int AccessTokenMinutes = 15;
        private const int RefreshTokenDays = 15;

        public AuthService(AppDbContext context, ITokenService tokenService)
        {
            _context = context;
            _tokenService = tokenService;
        }


        public async Task RegisterAsync(RegisterRequestDto request)
        {
            // Check if email already exists
            var existingUser = _context.Users
                .FirstOrDefault(u => u.Email == request.Email);

            if (existingUser != null)
                throw new Exception("Email already exists");

            // Hash password
            var hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);

            var user = new User
            {
                FullName = request.FullName,
                Email = request.Email,
                Password = hashedPassword,
                RoleId = request.RoleId
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();
        }

        public AuthResponseDto? Login(LoginRequestDto request)
        {
            var user = _context.Users.FirstOrDefault(u => u.Email == request.Email);
            if (user == null)
                return null;

            var isValid = BCrypt.Net.BCrypt.Verify(request.Password, user.Password);
            if (!isValid)
                return null;

            var role = _context.Roles.First(r => r.RoleId == user.RoleId);

            var accessToken = _tokenService.GenerateAccessToken(user, role.Role_name);
            var refreshToken = _tokenService.GenerateRefreshToken();

            user.RefreshToken = refreshToken;
            user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(RefreshTokenDays);
            _context.SaveChanges();

            return new AuthResponseDto
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresInMinutes = AccessTokenMinutes
            };
        }

        public AuthResponseDto? RefreshToken(RefreshTokenRequestDto request)
        {
            var user = _context.Users.FirstOrDefault(u => u.RefreshToken == request.RefreshToken);
            if (user == null)
                return null;

            if (!user.RefreshTokenExpiresAt.HasValue || user.RefreshTokenExpiresAt <= DateTime.UtcNow)
                return null;

            var role = _context.Roles.First(r => r.RoleId == user.RoleId);

            var accessToken = _tokenService.GenerateAccessToken(user, role.Role_name);
            var newRefreshToken = _tokenService.GenerateRefreshToken();

            user.RefreshToken = newRefreshToken;
            user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(RefreshTokenDays);
            _context.SaveChanges();

            return new AuthResponseDto
            {
                AccessToken = accessToken,
                RefreshToken = newRefreshToken,
                ExpiresInMinutes = AccessTokenMinutes
            };
        }

    }
}
