using System.Net;
using System.Net.Http.Headers;
using DAMS.Api.Security;
using DAMS.Application.Common;
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
/// The in-memory revocation tests never open a SQL transaction, so they cannot see the production
/// order: save the new token version, keep the cache until commit, then drop it. These run on the
/// same SQL Server the CI job uses.
/// </summary>
public sealed class AccessTokenSqlServerTests
{
    [SqlServerFact]
    public async Task DisablingAManager_ThroughARealSqlTransaction_RejectsTheOldTokenOnTheNextRequest()
    {
        await using var database = await SqlServerProductionInvariantTests.CreateDatabaseAsync();
        await using (var migrate = new AppDbContext(SqlOptions(database.ConnectionString)))
            await migrate.Database.MigrateAsync();

        await using var factory = new SqlApiFactory(database.ConnectionString);
        int adminId;
        int userId;
        int employeeId;
        int versionBefore;
        string token;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var admin = new User
            {
                RoleId = 1,
                FullName = "SQL Admin",
                Email = "sql-revoke-admin@dams.test",
                NormalizedEmail = EmailIdentity.Normalize("sql-revoke-admin@dams.test"),
                Password = "hash",
                AccountStatus = UserAccountStatus.Active
            };
            db.Users.Add(admin);
            await db.SaveChangesAsync();

            var user = new User
            {
                RoleId = 3,
                FullName = "SQL Manager",
                Email = "sql-revoke-manager@dams.test",
                NormalizedEmail = EmailIdentity.Normalize("sql-revoke-manager@dams.test"),
                Password = "hash",
                AccountStatus = UserAccountStatus.Active
            };
            var employee = new Employee
            {
                User = user,
                FullName = user.FullName,
                JobTitle = "Sales",
                Department = "Sales",
                Phone = "03001234001",
                JoinDate = new DateTime(2026, 1, 1),
                Status = EmployeeStatus.Active
            };
            db.Add(employee);
            await db.SaveChangesAsync();
            adminId = admin.UserId;
            userId = user.UserId;
            employeeId = employee.Id;
            versionBefore = user.TokenVersion;
            token = scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateAccessToken(user, LeadRoles.Manager);
        }

        Assert.NotEqual(adminId, userId);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/leads")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/finance/vendors")).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IStaffManagementService>().DisableAccessAsync(
                new LeadUserContext { UserId = adminId, Role = LeadRoles.Admin, DisplayName = "SQL admin" },
                employeeId);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/leads")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/finance/vendors")).StatusCode);

        using var verify = factory.Services.CreateScope();
        var stored = await verify.ServiceProvider.GetRequiredService<AppDbContext>().Users
            .AsNoTracking()
            .SingleAsync(candidate => candidate.UserId == userId);
        Assert.Equal(UserAccountStatus.Disabled, stored.AccountStatus);
        Assert.True(stored.TokenVersion > versionBefore);
    }

    [SqlServerFact]
    public async Task ARolledBackAccessChange_LeavesTheCachedSession_AndACommitDropsIt()
    {
        await using var database = await SqlServerProductionInvariantTests.CreateDatabaseAsync();
        var cache = new MemoryAccessSessionCache(new MemoryCache(new MemoryCacheOptions()));
        var sessions = new AccessSessionSaveInterceptor(cache);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(database.ConnectionString)
            .AddInterceptors(sessions, new AccessSessionTransactionInterceptor(sessions))
            .Options;

        await using (var migrate = new AppDbContext(options))
            await migrate.Database.MigrateAsync();

        int userId;
        await using (var db = new AppDbContext(options))
        {
            var user = new User
            {
                RoleId = 3,
                FullName = "SQL Rollback",
                Email = "sql-rollback@dams.test",
                NormalizedEmail = EmailIdentity.Normalize("sql-rollback@dams.test"),
                Password = "hash",
                AccountStatus = UserAccountStatus.Active
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            await db.Entry(user).ReloadAsync();
            userId = user.UserId;
        }

        var stamp = new AccessSessionStamp(0, UserAccountStatus.Active, LeadRoles.Manager, true);
        cache.Set(userId, stamp);

        await using (var db = new AppDbContext(options))
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var user = await db.Users.SingleAsync(candidate => candidate.UserId == userId);
            user.AccountStatus = UserAccountStatus.Disabled;
            await db.SaveChangesAsync();

            Assert.True(cache.TryGet(userId, out var during));
            Assert.Equal(stamp, during);

            await transaction.RollbackAsync();
        }

        Assert.True(cache.TryGet(userId, out var afterRollback));
        Assert.Equal(stamp, afterRollback);
        await using (var db = new AppDbContext(options))
        {
            var user = await db.Users.AsNoTracking().SingleAsync(candidate => candidate.UserId == userId);
            Assert.Equal(UserAccountStatus.Active, user.AccountStatus);
            Assert.Equal(0, user.TokenVersion);
        }

        await using (var db = new AppDbContext(options))
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var user = await db.Users.SingleAsync(candidate => candidate.UserId == userId);
            user.AccountStatus = UserAccountStatus.Disabled;
            await db.SaveChangesAsync();
            Assert.True(cache.TryGet(userId, out _));
            await transaction.CommitAsync();
        }

        Assert.False(cache.TryGet(userId, out _));
        await using (var db = new AppDbContext(options))
        {
            var user = await db.Users.AsNoTracking().SingleAsync(candidate => candidate.UserId == userId);
            Assert.Equal(UserAccountStatus.Disabled, user.AccountStatus);
            Assert.Equal(1, user.TokenVersion);
        }
    }

    private static DbContextOptions<AppDbContext> SqlOptions(string connectionString) =>
        new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;

    private sealed class SqlApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;

        public SqlApiFactory(string connectionString) => _connectionString = connectionString;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
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
                    options.UseSqlServer(_connectionString, sql => sql.EnableRetryOnFailure(
                            maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null))
                        .AddInterceptors(
                            ProjectListCacheTransactionInterceptor.Instance,
                            ProjectListCacheMediaInterceptor.Instance,
                            sp.GetRequiredService<AccessSessionSaveInterceptor>(),
                            sp.GetRequiredService<AccessSessionTransactionInterceptor>()));

                foreach (var hosted in services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService)).ToList())
                    services.Remove(hosted);
            });
        }
    }
}
