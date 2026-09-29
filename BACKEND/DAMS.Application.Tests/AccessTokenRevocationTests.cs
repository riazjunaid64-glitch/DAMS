using System.Net;
using System.Net.Http.Headers;
using DAMS.Api.Security;
using DAMS.Application.Common;
using DAMS.Application.DTOs.Auth;
using DAMS.Application.DTOs.EmployeeDtos;
using DAMS.Application.Interfaces;
using DAMS.Application.Security;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Domain.Identity;
using DAMS.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// A disabled, demoted, or signed-out login used to keep working until the access token expired.
/// These tests use the token issued before the change and require the very next request to fail.
/// </summary>
public sealed class AccessTokenRevocationTests : IClassFixture<AccessTokenRevocationTests.ApiFactory>
{
    private readonly ApiFactory _factory;

    public AccessTokenRevocationTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task DisablingAManager_RejectsTheOldTokenOnLeadsAndFinanceImmediately()
    {
        var seeded = await SeedManagerAsync(userId: 80);
        var client = Client(seeded.Token);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/leads")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/finance/vendors")).StatusCode);

        await UsingStaff(staff => staff.DisableAccessAsync(AdminActor(), seeded.EmployeeId));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/leads")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/finance/vendors")).StatusCode);
    }

    [Fact]
    public async Task DemotingAManager_RejectsTheOldTokenOnTheOrganisationDashboard()
    {
        var seeded = await SeedManagerAsync(userId: 81);
        var client = Client(seeded.Token);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/lead-dashboard/organisation")).StatusCode);

        await UsingStaff(staff => staff.UpdateAsync(
            AdminActor(),
            seeded.EmployeeId,
            new UpdateStaffAccountDto { Role = LeadRoles.Employee, Status = EmployeeStatus.Active }));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/lead-dashboard/organisation")).StatusCode);
    }

    [Fact]
    public async Task ReEnabling_LetsANewLoginIn_AndKeepsTheOldTokenRejected()
    {
        var seeded = await SeedManagerAsync(userId: 82);
        var client = Client(seeded.Token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/leads")).StatusCode);

        await UsingStaff(staff => staff.DisableAccessAsync(AdminActor(), seeded.EmployeeId));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/leads")).StatusCode);

        await UsingStaff(staff => staff.EnableAccessAsync(AdminActor(), seeded.EmployeeId));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/leads")).StatusCode);

        var renewed = TokenFor(seeded.UserId, LeadRoles.Manager);
        var renewedClient = Client(renewed);
        Assert.Equal(HttpStatusCode.OK, (await renewedClient.GetAsync("/api/leads")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/leads")).StatusCode);
    }

    [Fact]
    public async Task ChangingThePassword_RejectsTheTokenIssuedBeforeIt()
    {
        var seeded = await SeedManagerAsync(userId: 83);
        var client = Client(seeded.Token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/Auth/profile")).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(candidate => candidate.UserId == seeded.UserId);
            user.Password = "a-new-hash";
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/Auth/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client(TokenFor(seeded.UserId, LeadRoles.Manager)).GetAsync("/api/Auth/profile")).StatusCode);
    }

    [Fact]
    public async Task LoggingOut_RejectsTheAccessTokenThatWasStillInTheBrowser()
    {
        var seeded = await SeedManagerAsync(userId: 84);
        var client = Client(seeded.Token);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(candidate => candidate.UserId == seeded.UserId);
            user.RefreshToken = "session";
            await db.SaveChangesAsync();
            user.RefreshToken = null;
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/Auth/profile")).StatusCode);
    }

    [Fact]
    public async Task Logout_WithTheCurrentRefreshCookie_RejectsTheAccessToken()
    {
        var session = await SignInManagerAsync(userId: 86);

        var client = Client(session.SecondAccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/Auth/profile")).StatusCode);

        var logout = await PostLogoutAsync(session.SecondRefreshToken, bearer: null);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/Auth/profile")).StatusCode);
    }

    [Fact]
    public async Task Logout_WithAStaleRefreshCookie_StillRejectsThatAccessToken()
    {
        var session = await SignInManagerAsync(userId: 87);

        var first = Client(session.FirstAccessToken);
        var second = Client(session.SecondAccessToken);
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/Auth/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/Auth/profile")).StatusCode);

        var logout = await PostLogoutAsync(session.FirstRefreshToken, session.FirstAccessToken);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/Auth/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/Auth/profile")).StatusCode);
    }

    [Fact]
    public async Task ACachedStamp_IsDroppedWhenTheLoginChanges_SoTheNextRequestReloadsIt()
    {
        var seeded = await SeedManagerAsync(userId: 85);
        var client = Client(seeded.Token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/Auth/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/Auth/profile")).StatusCode);

        await UsingStaff(staff => staff.DisableAccessAsync(AdminActor(), seeded.EmployeeId));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/Auth/profile")).StatusCode);
    }

    [Fact]
    public void TheSessionCache_ServesTheSecondLookup_AndMissesAfterInvalidate()
    {
        var cache = new MemoryAccessSessionCache(new MemoryCache(new MemoryCacheOptions()));
        var stamp = new AccessSessionStamp(3, UserAccountStatus.Active, "Manager", true);

        Assert.False(cache.TryGet(7, out _));
        cache.Set(7, stamp);

        Assert.True(cache.TryGet(7, out var first));
        Assert.Equal(stamp, first);
        Assert.True(cache.TryGet(7, out var second));
        Assert.Equal(stamp, second);

        cache.Invalidate(7);
        Assert.False(cache.TryGet(7, out _));
    }

    [Fact]
    public async Task ReplacingARefreshToken_DoesNotEndTheSession_ClearingItDoes()
    {
        var cache = new MemoryAccessSessionCache(new MemoryCache(new MemoryCacheOptions()));
        var database = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(database)
            .AddInterceptors(new AccessSessionSaveInterceptor(cache))
            .Options;

        await using (var db = new AppDbContext(options))
        {
            db.Database.EnsureCreated();
            db.Users.Add(new User
            {
                UserId = 9,
                RoleId = 3,
                FullName = "Sales",
                Email = "sales@dams.test",
                NormalizedEmail = EmailIdentity.Normalize("sales@dams.test"),
                AccountStatus = UserAccountStatus.Active,
                RefreshToken = "old",
                TokenVersion = 4
            });
            db.SaveChanges();
        }

        await using (var db = new AppDbContext(options))
        {
            var user = db.Users.Single(candidate => candidate.UserId == 9);
            user.RefreshToken = "new";
            db.SaveChanges();
            Assert.Equal(4, user.TokenVersion);
        }

        cache.Set(9, new AccessSessionStamp(4, UserAccountStatus.Active, "Manager", true));
        await using (var db = new AppDbContext(options))
        {
            var user = db.Users.Single(candidate => candidate.UserId == 9);
            user.RefreshToken = null;
            db.SaveChanges();
            Assert.Equal(5, user.TokenVersion);
        }

        Assert.False(cache.TryGet(9, out _));
    }

    private async Task<SignedInManager> SignInManagerAsync(int userId)
    {
        var seeded = await SeedManagerAsync(userId);
        const string password = "correct-password-1";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(candidate => candidate.UserId == seeded.UserId);
            user.Password = BCrypt.Net.BCrypt.HashPassword(password);
            await db.SaveChangesAsync();
        }

        using var login = _factory.Services.CreateScope();
        var auth = login.ServiceProvider.GetRequiredService<IAuthService>();
        var email = $"manager{userId}@dams.test";
        var first = await auth.LoginAsync(new LoginRequestDto { Email = email, Password = password });
        var second = await auth.LoginAsync(new LoginRequestDto { Email = email, Password = password });
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first!.RefreshToken, second!.RefreshToken);
        return new SignedInManager(first.AccessToken, first.RefreshToken, second.AccessToken, second.RefreshToken);
    }

    private async Task<HttpResponseMessage> PostLogoutAsync(string refreshToken, string? bearer)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/Auth/logout");
        if (bearer != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.TryAddWithoutValidation("Cookie", "refreshToken=" + Uri.EscapeDataString(refreshToken));
        return await _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
            .SendAsync(request);
    }

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private string TokenFor(int userId, string roleName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = db.Users.Single(candidate => candidate.UserId == userId);
        return scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateAccessToken(user, roleName);
    }

    private async Task UsingStaff(Func<IStaffManagementService, Task> action)
    {
        using var scope = _factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<IStaffManagementService>());
    }

    private static LeadUserContext AdminActor() => new()
    {
        UserId = 1,
        Role = LeadRoles.Admin,
        DisplayName = "Admin"
    };

    private async Task<SeededManager> SeedManagerAsync(int userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        if (!await db.Users.AnyAsync(candidate => candidate.UserId == 1))
        {
            db.Users.Add(new User
            {
                UserId = 1,
                RoleId = 1,
                FullName = "Admin",
                Email = "admin@dams.test",
                NormalizedEmail = EmailIdentity.Normalize("admin@dams.test"),
                Password = "hash",
                AccountStatus = UserAccountStatus.Active
            });
        }

        var user = new User
        {
            UserId = userId,
            RoleId = 3,
            FullName = "Manager " + userId,
            Email = $"manager{userId}@dams.test",
            NormalizedEmail = EmailIdentity.Normalize($"manager{userId}@dams.test"),
            Password = "hash",
            AccountStatus = UserAccountStatus.Active,
            RefreshToken = "live-session"
        };
        db.Users.Add(user);
        var employee = new Employee
        {
            User = user,
            FullName = user.FullName,
            JobTitle = "Sales",
            Department = "Sales",
            Phone = "03" + userId.ToString("D9"),
            JoinDate = new DateTime(2026, 1, 1),
            Status = EmployeeStatus.Active
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        var token = scope.ServiceProvider.GetRequiredService<ITokenService>()
            .GenerateAccessToken(user, LeadRoles.Manager);
        return new SeededManager(user.UserId, employee.Id, token);
    }

    private sealed record SeededManager(int UserId, int EmployeeId, string Token);

    private sealed record SignedInManager(
        string FirstAccessToken, string FirstRefreshToken, string SecondAccessToken, string SecondRefreshToken);

    public sealed class ApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=(test);Database=unused;");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = new string('k', 64),
                    ["Jwt:Issuer"] = "dams-tests",
                    ["Jwt:Audience"] = "dams-tests",
                    ["Notifications:AllowedPushEndpointHosts:0"] = "push.test",
                }));

            builder.ConfigureServices(services =>
            {
                var remove = services.Where(descriptor =>
                    descriptor.ServiceType == typeof(AppDbContext) ||
                    descriptor.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                    descriptor.ServiceType == typeof(DbContextOptions) ||
                    (descriptor.ServiceType.FullName != null && descriptor.ServiceType.FullName.Contains("DbContextPool")) ||
                    (descriptor.ServiceType.FullName != null && descriptor.ServiceType.FullName.Contains("ScopedDbContextLease")))
                    .ToList();
                foreach (var descriptor in remove)
                    services.Remove(descriptor);

                services.AddDbContext<AppDbContext>((sp, options) =>
                    options.UseInMemoryDatabase("access-revocation-tests")
                        .AddInterceptors(sp.GetRequiredService<AccessSessionSaveInterceptor>()));

                foreach (var hosted in services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService)).ToList())
                    services.Remove(hosted);
            });
        }
    }
}
