using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using DAMS.Application.DTOs.Auth;
using DAMS.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

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
            if (string.IsNullOrWhiteSpace(request.FullName) ||
                string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.Password))
            {
                throw new Exception("Full name, email, and password are required");
            }

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();

            // Check if email already exists
            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

            if (existingUser != null)
                throw new Exception("Email already exists");

            // Public registration can only create Client users.
            var clientRole = await _context.Roles
                .FirstOrDefaultAsync(r => r.Role_name.ToLower() == "client");
            if (clientRole == null)
                throw new Exception("Client role is not configured");

            // Hash password
            var hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);

            var user = new User
            {
                FullName = request.FullName,
                Email = normalizedEmail,
                Password = hashedPassword,
                RoleId = clientRole.RoleId,
                // Stated rather than left to the default. A client who has just chosen their own
                // password is signed in immediately, and that must not become dependent on which
                // UserAccountStatus happens to be zero.
                AccountStatus = UserAccountStatus.Active
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();
        }

        public async Task<AuthResponseDto?> LoginAsync(LoginRequestDto request)
        {
            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            var user = await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Email == normalizedEmail);
            if (user == null)
                return null;

            // Before the password is even looked at. An invited login has no stored password to
            // verify against — passing null to BCrypt.Verify would throw rather than reject —
            // and a disabled one has a password that must stop working. Both fall through to
            // the same "invalid credentials" the caller gets for a wrong password, so the
            // public response never says which of the three it was.
            if (user.AccountStatus != UserAccountStatus.Active || user.Password == null)
                return null;

            var isValid = BCrypt.Net.BCrypt.Verify(request.Password, user.Password);
            if (!isValid)
                return null;

            var role = user.Role;
            if (!await StaffLoginIsAllowedAsync(user.UserId, role.Role_name))
                return null;

            var accessToken = _tokenService.GenerateAccessToken(user, role.Role_name);
            var refreshToken = _tokenService.GenerateRefreshToken();

            user.RefreshToken = HashRefreshToken(refreshToken);
            user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(RefreshTokenDays);
            await _context.SaveChangesAsync();

            return new AuthResponseDto
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresInMinutes = AccessTokenMinutes
            };
        }

        public async Task<AuthResponseDto?> RefreshTokenAsync(RefreshTokenRequestDto request)
        {
            var refreshTokenHash = HashRefreshToken(request.RefreshToken);
            var user = await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.RefreshToken == refreshTokenHash);
            if (user == null)
                return null;

            if (!user.RefreshTokenExpiresAt.HasValue || user.RefreshTokenExpiresAt <= DateTime.UtcNow)
                return null;

            // A refresh token is a standing permission to keep minting access tokens, so a login
            // that has stopped being Active has to lose it. Disabling an account would otherwise
            // leave whoever holds its session working for another fifteen days.
            if (user.AccountStatus != UserAccountStatus.Active)
            {
                user.RefreshToken = null;
                user.RefreshTokenExpiresAt = null;
                await _context.SaveChangesAsync();
                return null;
            }

            var role = user.Role;
            if (!await StaffLoginIsAllowedAsync(user.UserId, role.Role_name))
            {
                user.RefreshToken = null;
                user.RefreshTokenExpiresAt = null;
                await _context.SaveChangesAsync();
                return null;
            }

            var accessToken = _tokenService.GenerateAccessToken(user, role.Role_name);
            var newRefreshToken = _tokenService.GenerateRefreshToken();

            user.RefreshToken = HashRefreshToken(newRefreshToken);
            user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(RefreshTokenDays);
            await _context.SaveChangesAsync();

            return new AuthResponseDto
            {
                AccessToken = accessToken,
                RefreshToken = newRefreshToken,
                ExpiresInMinutes = AccessTokenMinutes
            };
        }

        public async Task RevokeRefreshTokenAsync(string refreshToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
                return;

            var refreshTokenHash = HashRefreshToken(refreshToken);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.RefreshToken == refreshTokenHash);
            if (user == null)
                return;

            user.RefreshToken = null;
            user.RefreshTokenExpiresAt = null;
            await _context.SaveChangesAsync();
        }

        private static string HashRefreshToken(string refreshToken)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
            return Convert.ToBase64String(hash);
        }

        private async Task<bool> StaffLoginIsAllowedAsync(int userId, string roleName)
        {
            if (roleName is not ("Manager" or "Employee"))
                return true;

            return await _context.Employees
                .AnyAsync(e => e.UserId == userId && e.Status == Domain.Enums.EmployeeStatus.Active);
        }

    }
}
