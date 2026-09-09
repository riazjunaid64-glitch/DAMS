using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Domain.Identity;
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
        private readonly IClientEmailVerificationService? _clientVerification;
        private const int AccessTokenMinutes = 15;
        private const int RefreshTokenDays = 15;

        /// <param name="clientVerification">
        /// Optional so tests that only exercise sign-in do not have to stand up an email stack.
        /// When it is absent a registration still creates the pending account — the address is
        /// reserved and no password exists — but no link is sent, and the person has to use
        /// resend. The account is never left usable either way.
        /// </param>
        public AuthService(
            AppDbContext context,
            ITokenService tokenService,
            IClientEmailVerificationService? clientVerification = null)
        {
            _context = context;
            _tokenService = tokenService;
            _clientVerification = clientVerification;
        }

        /// <summary>
        /// Creates a public client registration in the one state it is safe to create it in:
        /// pending, with no password at all.
        ///
        /// <para>
        /// No password is accepted here, and that is the point of the whole workflow. If
        /// registration set one, somebody could register a buyer's address, choose a password,
        /// and still hold that password after the buyer clicked the confirmation link that turned
        /// up unexpectedly in their inbox. The password is established during verification, by
        /// whoever demonstrably reads the mail.
        /// </para>
        ///
        /// <para>
        /// Returns nothing and throws for nothing an anonymous caller could learn from — a taken
        /// address, a disabled account and a fresh one all leave through the same door, and the
        /// controller says the same sentence for all three.
        /// </para>
        /// </summary>
        public async Task RegisterAsync(RegisterRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.Email))
                throw new ArgumentException("Full name and email are required.");

            var normalizedEmail = EmailIdentity.Normalize(request.Email)!;

            // Public registration can only create Client users.
            var clientRole = await _context.Roles
                .FirstOrDefaultAsync(r => r.Role_name.ToLower() == "client");
            if (clientRole == null)
                throw new InvalidOperationException("Client role is not configured");

            var existingUserId = await _context.Users
                .Where(u => u.NormalizedEmail == normalizedEmail)
                .Select(u => (int?)u.UserId)
                .FirstOrDefaultAsync();

            if (existingUserId.HasValue)
            {
                // The address is taken. Saying so would turn registration into an account
                // enumeration oracle, so the caller is told nothing. Reissuing to an account that
                // is still pending is safe and useful — it is the same person retrying — and the
                // service itself refuses to send anything for an account in any other state.
                if (_clientVerification != null)
                    await _clientVerification.IssueAsync(existingUserId.Value);
                return;
            }

            var user = new User
            {
                FullName = request.FullName.Trim(),
                // The address as typed is kept for display and delivery; identity is decided by
                // the normalized column, which carries the unique index.
                Email = request.Email.Trim(),
                NormalizedEmail = normalizedEmail,
                Password = null,
                RoleId = clientRole.RoleId,
                AccountStatus = UserAccountStatus.PendingEmailVerification,
                EmailVerifiedAt = null
            };

            _context.Users.Add(user);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Two registrations for the same address arriving together: the application-level
                // check above passed for both, and the unique index on NormalizedEmail refused the
                // loser. That is the invariant working, not an error to report — the caller gets
                // the same neutral answer as the winner, and only one identity exists.
                _context.ChangeTracker.Clear();

                var winnerId = await _context.Users
                    .Where(u => u.NormalizedEmail == normalizedEmail)
                    .Select(u => (int?)u.UserId)
                    .FirstOrDefaultAsync();

                // No winner means the write failed for some other reason entirely. Answering
                // neutrally there would report a registration that does not exist, so the
                // original failure is left to surface.
                if (!winnerId.HasValue)
                    throw;

                if (_clientVerification != null)
                    await _clientVerification.IssueAsync(winnerId.Value);
                return;
            }

            if (_clientVerification != null)
                await _clientVerification.IssueAsync(user.UserId);
        }

        public async Task<AuthResponseDto?> LoginAsync(LoginRequestDto request)
        {
            var normalizedEmail = EmailIdentity.Normalize(request.Email);
            if (normalizedEmail == null)
                return null;

            var user = await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);
            if (user == null)
                return null;

            // Before the password is even looked at. An invited or pending login has no stored
            // password to verify against — passing null to BCrypt.Verify would throw rather than
            // reject — and a disabled one has a password that must stop working. All of them fall
            // through to the same "invalid credentials" the caller gets for a wrong password, so
            // the public response never says which it was.
            if (user.AccountStatus != UserAccountStatus.Active || user.Password == null)
                return null;

            var isValid = BCrypt.Net.BCrypt.Verify(request.Password, user.Password);
            if (!isValid)
                return null;

            var role = user.Role;
            if (!await StaffLoginIsAllowedAsync(user.UserId, role.Role_name))
                return null;

            // A client account is only a client account once somebody proved they hold the
            // mailbox. Active is not enough on its own: every client login that existed before
            // verification was introduced is Active and has never proven anything, and those are
            // exactly the accounts that must fail closed rather than fall through.
            if (!ClientVerificationSatisfied(user, role.Role_name))
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
                await ClearSessionAsync(user);
                return null;
            }

            var role = user.Role;
            if (!await StaffLoginIsAllowedAsync(user.UserId, role.Role_name))
            {
                await ClearSessionAsync(user);
                return null;
            }

            // The same verification gate as login, and for the same reason it has to be here
            // rather than only there: a session minted before this rule existed would otherwise
            // keep renewing itself indefinitely without the account ever proving its address.
            if (!ClientVerificationSatisfied(user, role.Role_name))
            {
                await ClearSessionAsync(user);
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

            await ClearSessionAsync(user);
        }

        private async Task ClearSessionAsync(User user)
        {
            user.RefreshToken = null;
            user.RefreshTokenExpiresAt = null;
            await _context.SaveChangesAsync();
        }

        private static string HashRefreshToken(string refreshToken)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
            return Convert.ToBase64String(hash);
        }

        /// <summary>
        /// Applies to client logins only. Staff accounts prove themselves through the invitation
        /// an Admin issued and the employment record behind it, which is a different proof of a
        /// different thing — pulling them into this gate would lock out every existing employee
        /// for no security gain.
        /// </summary>
        private static bool ClientVerificationSatisfied(User user, string roleName) =>
            !string.Equals(roleName, "Client", StringComparison.OrdinalIgnoreCase)
            || user.EmailVerifiedAt != null;

        private async Task<bool> StaffLoginIsAllowedAsync(int userId, string roleName)
        {
            if (roleName is not ("Manager" or "Employee"))
                return true;

            return await _context.Employees
                .AnyAsync(e => e.UserId == userId && e.Status == Domain.Enums.EmployeeStatus.Active);
        }

    }
}
