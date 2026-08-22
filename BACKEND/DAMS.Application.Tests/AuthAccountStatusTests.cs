using DAMS.Application.DTOs.Auth;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// What <see cref="UserAccountStatus"/> means at the front door. A login that exists is not the
/// same thing as a login that may be used: an invited one has no password yet, a disabled one
/// has a password that must stop working, and neither may hold a session open by refreshing.
/// Every rejection here is the same public answer as a wrong password, on purpose.
/// </summary>
public sealed class AuthAccountStatusTests
{
    private const string RightPassword = "correct-password-1";

    // ── Login ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_active_client_with_the_right_password_can_sign_in()
    {
        await using var h = await Harness.CreateAsync();

        var result = await h.Auth.LoginAsync(Login("client@dams.test", RightPassword));

        Assert.NotNull(result);
        Assert.Equal("access-token", result.AccessToken);
        Assert.Equal("refresh-token", result.RefreshToken);
    }

    [Fact]
    public async Task An_invited_login_cannot_sign_in_and_no_null_password_reaches_bcrypt()
    {
        await using var h = await Harness.CreateAsync();

        // Whatever is typed, there is nothing to verify it against. The point of the test is
        // that this returns rather than throwing, which is what BCrypt.Verify(_, null) does.
        Assert.Null(await h.Auth.LoginAsync(Login("invited@dams.test", RightPassword)));
        Assert.Null(await h.Auth.LoginAsync(Login("invited@dams.test", "")));
    }

    [Fact]
    public async Task A_disabled_login_cannot_sign_in_even_with_its_real_password()
    {
        await using var h = await Harness.CreateAsync();

        Assert.Null(await h.Auth.LoginAsync(Login("disabled@dams.test", RightPassword)));

        // Refused by status, not because the credential rotted — it still verifies.
        var user = await h.ReloadAsync("disabled@dams.test");
        Assert.True(BCrypt.Net.BCrypt.Verify(RightPassword, user.Password));
    }

    [Fact]
    public async Task An_active_staff_login_still_needs_an_active_employment_record()
    {
        await using var h = await Harness.CreateAsync();

        // The rule that predates account status: the employee is Active, so this works.
        Assert.NotNull(await h.Auth.LoginAsync(Login("manager@dams.test", RightPassword)));

        await h.SetEmployeeStatusAsync("manager@dams.test", EmployeeStatus.Terminated);

        Assert.Null(await h.Auth.LoginAsync(Login("manager@dams.test", RightPassword)));
    }

    // ── Refresh ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_active_eligible_login_can_refresh_normally()
    {
        await using var h = await Harness.CreateAsync();
        var session = await h.GiveSessionAsync("client@dams.test");

        var result = await h.Auth.RefreshTokenAsync(new RefreshTokenRequestDto { RefreshToken = session });

        Assert.NotNull(result);
        Assert.Equal("access-token", result.AccessToken);
    }

    [Theory]
    [InlineData(UserAccountStatus.Invited)]
    [InlineData(UserAccountStatus.Disabled)]
    public async Task A_login_that_is_no_longer_active_cannot_refresh_and_loses_its_session(
        UserAccountStatus status)
    {
        await using var h = await Harness.CreateAsync();
        var session = await h.GiveSessionAsync("client@dams.test");

        // The session was minted while the account still worked; the account changed after.
        await h.SetAccountStatusAsync("client@dams.test", status);

        Assert.Null(await h.Auth.RefreshTokenAsync(new RefreshTokenRequestDto { RefreshToken = session }));

        // Not merely refused this time: the stored session is gone, so the fifteen-day
        // refresh window cannot be used again either.
        var user = await h.ReloadAsync("client@dams.test");
        Assert.Null(user.RefreshToken);
        Assert.Null(user.RefreshTokenExpiresAt);
    }

    [Fact]
    public async Task A_staff_login_whose_employment_ended_still_loses_its_session_on_refresh()
    {
        await using var h = await Harness.CreateAsync();
        var session = await h.GiveSessionAsync("manager@dams.test");
        await h.SetEmployeeStatusAsync("manager@dams.test", EmployeeStatus.Inactive);

        Assert.Null(await h.Auth.RefreshTokenAsync(new RefreshTokenRequestDto { RefreshToken = session }));

        var user = await h.ReloadAsync("manager@dams.test");
        Assert.Null(user.RefreshToken);
    }

    // ── Public registration ─────────────────────────────────────────────────────

    [Fact]
    public async Task Public_registration_creates_an_active_client_who_can_sign_in_at_once()
    {
        await using var h = await Harness.CreateAsync();

        await h.Auth.RegisterAsync(new RegisterRequestDto
        {
            FullName = "Bilal Buyer",
            Email = "  Bilal@Example.COM ",
            Password = "buyer-chosen-pass-2"
        });

        var user = await h.ReloadAsync("bilal@example.com");
        Assert.Equal(UserAccountStatus.Active, user.AccountStatus);
        Assert.Equal(h.ClientRoleId, user.RoleId);

        // Their own password, hashed, and immediately usable — registration is not an invitation.
        Assert.True(BCrypt.Net.BCrypt.Verify("buyer-chosen-pass-2", user.Password));
        Assert.NotNull(await h.Auth.LoginAsync(Login("bilal@example.com", "buyer-chosen-pass-2")));
    }

    private static LoginRequestDto Login(string email, string password) =>
        new() { Email = email, Password = password };

    /// <summary>
    /// Four logins covering the states that matter — an active client, an invited staff member
    /// with no password at all, a disabled one, and an active manager with employment behind
    /// it — over the real <see cref="AuthService"/> and a token service that returns fixed
    /// strings, because what a JWT contains is not what these tests are about.
    /// </summary>
    private sealed class Harness : IAsyncDisposable
    {
        public AppDbContext Db { get; }
        public AuthService Auth { get; }
        public int ClientRoleId => 2;

        private Harness(AppDbContext db)
        {
            Db = db;
            Auth = new AuthService(db, new FixedTokenService());
        }

        public static async Task<Harness> CreateAsync()
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var h = new Harness(db);

            db.Roles.AddRange(
                new Role { RoleId = 1, Role_name = "Admin" },
                new Role { RoleId = 2, Role_name = "Client" },
                new Role { RoleId = 3, Role_name = "Manager" });

            var hashed = BCrypt.Net.BCrypt.HashPassword(RightPassword);
            var client = NewUser(2, "Cara Client", "client@dams.test", UserAccountStatus.Active, hashed);
            var invited = NewUser(3, "Sana Sales", "invited@dams.test", UserAccountStatus.Invited, null);
            var disabled = NewUser(3, "Zara Left", "disabled@dams.test", UserAccountStatus.Disabled, hashed);
            var manager = NewUser(3, "Mona Manager", "manager@dams.test", UserAccountStatus.Active, hashed);
            db.Users.AddRange(client, invited, disabled, manager);
            await db.SaveChangesAsync();

            // Only the staff logins have employment records; the client deliberately has none.
            db.Employees.AddRange(
                NewEmployee(invited), NewEmployee(disabled), NewEmployee(manager));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            return h;
        }

        /// <summary>Signs the login in, and hands back the raw refresh token it was given.</summary>
        public async Task<string> GiveSessionAsync(string email)
        {
            var result = await Auth.LoginAsync(Login(email, RightPassword));
            Assert.NotNull(result);
            Db.ChangeTracker.Clear();
            return result.RefreshToken;
        }

        public async Task SetAccountStatusAsync(string email, UserAccountStatus status)
        {
            var user = await Db.Users.FirstAsync(u => u.Email == email);
            user.AccountStatus = status;
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task SetEmployeeStatusAsync(string email, EmployeeStatus status)
        {
            var employee = await Db.Employees.FirstAsync(e => e.Email == email);
            employee.Status = status;
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task<User> ReloadAsync(string email)
        {
            Db.ChangeTracker.Clear();
            return await Db.Users.AsNoTracking().FirstAsync(u => u.Email == email);
        }

        private static User NewUser(
            int roleId, string name, string email, UserAccountStatus status, string? password) => new()
        {
            RoleId = roleId,
            FullName = name,
            Email = email,
            Password = password,
            AccountStatus = status
        };

        private static Employee NewEmployee(User user) => new()
        {
            FullName = user.FullName,
            UserId = user.UserId,
            Email = user.Email,
            JobTitle = "Sales Executive",
            JoinDate = new DateTime(2026, 1, 1),
            Status = EmployeeStatus.Active
        };

        private sealed class FixedTokenService : ITokenService
        {
            public string GenerateAccessToken(User user, string roleName) => "access-token";
            public string GenerateRefreshToken() => "refresh-token";
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
