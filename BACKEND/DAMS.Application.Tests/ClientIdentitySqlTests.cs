using DAMS.Application.DTOs.Auth;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using DAMS.Application.Services.Notifications;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Domain.Identity;
using DAMS.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// The identity invariants that only a real database can prove.
///
/// <para>
/// Two of them are unprovable in memory by definition. A unique index is enforced by the storage
/// engine, and the in-memory provider has none — so "two concurrent registrations produce one
/// identity" would pass there whether or not the index existed. Serialisable isolation is the
/// same: the in-memory provider ignores transactions entirely, so a redemption race would be
/// serialised by the test rather than by the guarantee under test.
/// </para>
///
/// <para>
/// Skipped unless <c>DAMS_SQLSERVER_TEST_CONNECTION</c> is set, in line with the rest of the
/// SQL-backed suite.
/// </para>
/// </summary>
public sealed class ClientIdentitySqlTests
{
    [SqlServerFact]
    public async Task Concurrent_registrations_of_one_address_produce_exactly_one_identity()
    {
        await using var database = await SqlIdentityTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        // Eight simultaneous attempts on the same address, in mixed case so the canonical
        // comparison is what has to hold rather than a byte-for-byte match. Each runs on its own
        // context, as eight separate requests would.
        async Task RegisterAsync(string typedEmail)
        {
            await using var context = new AppDbContext(options);
            var service = new AuthService(context, new FixedTokenService());
            await service.RegisterAsync(new RegisterRequestDto
            {
                FullName = "Concurrent Buyer",
                Email = typedEmail
            });
        }

        var attempts = new[]
        {
            "race@example.com", "RACE@example.com", "Race@Example.com", " race@example.com ",
            "rAcE@example.COM", "RACE@EXAMPLE.COM", "race@Example.com", "Race@example.COM"
        };

        // No attempt may fail. Losing the race is not an error the caller should ever see: it
        // means somebody else's insert already established the identity, which is the correct
        // outcome and gets the same neutral answer as winning.
        await Task.WhenAll(attempts.Select(RegisterAsync));

        await using (var db = new AppDbContext(options))
        {
            Assert.Equal(1, await db.Users.CountAsync(u => u.NormalizedEmail == "RACE@EXAMPLE.COM"));

            // And the one that exists is pending with no password — eight racing requests must not
            // between them have produced a usable account.
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.NormalizedEmail == "RACE@EXAMPLE.COM");
            Assert.Equal(UserAccountStatus.PendingEmailVerification, user.AccountStatus);
            Assert.Null(user.Password);
            Assert.Null(user.EmailVerifiedAt);
        }
    }

    [SqlServerFact]
    public async Task Concurrent_redemptions_of_one_token_verify_exactly_once()
    {
        await using var database = await SqlIdentityTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        var email = new CapturingEmailSender();
        await using (var context = new AppDbContext(options))
        {
            var auth = new AuthService(
                context, new FixedTokenService(),
                new ClientEmailVerificationService(
                    context, new NotificationSettingsStore(context), email, TimeProvider.System));

            context.NotificationSettings.Add(new NotificationSetting
            {
                Key = DAMS.Application.Common.NotificationSettingKeys.PublicBaseUrl,
                Value = "https://dams.example"
            });
            await context.SaveChangesAsync();

            await auth.RegisterAsync(new RegisterRequestDto { FullName = "Racer", Email = "racer@example.com" });
        }

        var token = ExtractToken(email.Sent.Single().TextBody);
        Assert.NotNull(token);

        // The same link opened twice at once — a double-click, or a mail client prefetching it.
        // Each side chooses a different password, so whichever loses must not have left its
        // password behind.
        async Task<ClientVerificationResult> RedeemAsync(string password)
        {
            await using var context = new AppDbContext(options);
            var service = new ClientEmailVerificationService(
                context, new NotificationSettingsStore(context), new CapturingEmailSender(), TimeProvider.System);
            try
            {
                return await service.VerifyAsync(token!, password);
            }
            catch (Exception)
            {
                // A serialisable deadlock that outlives the retry strategy is a loss, not a
                // success. It is treated as a refusal so the count assertion below still holds.
                return ClientVerificationResult.Invalid();
            }
        }

        var outcomes = await Task.WhenAll(
            RedeemAsync("first-side-password-1"), RedeemAsync("second-side-password-2"));

        Assert.Single(outcomes.Where(o => o.Verified));

        await using (var db = new AppDbContext(options))
        {
            Assert.Equal(1, await db.ClientEmailVerifications.CountAsync(v => v.VerifiedAt != null));

            var user = await db.Users.AsNoTracking().SingleAsync(u => u.NormalizedEmail == "RACER@EXAMPLE.COM");
            Assert.Equal(UserAccountStatus.Active, user.AccountStatus);
            Assert.NotNull(user.EmailVerifiedAt);

            // Exactly one of the two passwords took, and the account is openable with it.
            var first = BCrypt.Net.BCrypt.Verify("first-side-password-1", user.Password);
            var second = BCrypt.Net.BCrypt.Verify("second-side-password-2", user.Password);
            Assert.True(first ^ second);
        }
    }

    [SqlServerFact]
    public async Task The_migration_backfills_identity_without_verifying_or_relinking_anything()
    {
        await using var database = await SqlIdentityTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);

        // Migrate up to the state just before this work, then seed data that looks like
        // production: a client with a live session, a customer sharing their address but linked to
        // nobody, and a staff login that must come through untouched.
        await using (var db = new AppDbContext(options))
            await db.GetService<IMigrator>()
                .MigrateAsync("20260906233827_AddPaymentAttemptKey");

        await using (var db = new AppDbContext(options))
        {
            await db.Database.ExecuteSqlRawAsync(@"
SET IDENTITY_INSERT [Users] ON;
INSERT INTO [Users] ([UserId],[RoleId],[FullName],[Email],[Password],[AccountStatus],[RefreshToken],[RefreshTokenExpiresAt])
VALUES (9001, 2, N'Legacy Client', N'Legacy@Example.com', N'$2a$11$abcdefghijklmnopqrstuv', 0, N'a-live-session-hash', DATEADD(day, 10, SYSUTCDATETIME())),
       (9002, 1, N'Real Admin',    N'admin@dams.test',    N'$2a$11$abcdefghijklmnopqrstuv', 0, N'an-admin-session',   DATEADD(day, 10, SYSUTCDATETIME()));
SET IDENTITY_INSERT [Users] OFF;

INSERT INTO [Customers] ([FullName],[Phone],[Email],[Status],[Source],[CreatedAt])
VALUES (N'Historic Buyer', N'03001234567', N'legacy@example.com', 0, 0, SYSUTCDATETIME());
");
        }

        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        await using (var db = new AppDbContext(options))
        {
            var client = await db.Users.AsNoTracking().SingleAsync(u => u.UserId == 9001);

            // Canonicalised, but not judged: the address is now comparable and still never proven.
            Assert.Equal("LEGACY@EXAMPLE.COM", client.NormalizedEmail);
            Assert.Equal("Legacy@Example.com", client.Email);
            Assert.Null(client.EmailVerifiedAt);

            // Fails closed, and its standing fifteen-day session is gone — otherwise the account
            // would refresh straight past the new requirement.
            Assert.Equal(UserAccountStatus.PendingEmailVerification, client.AccountStatus);
            Assert.Null(client.RefreshToken);
            Assert.Null(client.RefreshTokenExpiresAt);

            // Staff are not swept into the client lifecycle, and keep working.
            var admin = await db.Users.AsNoTracking().SingleAsync(u => u.UserId == 9002);
            Assert.Equal(UserAccountStatus.Active, admin.AccountStatus);
            Assert.Equal("an-admin-session", admin.RefreshToken);

            // The shortcut the migration must never take: the customer's address matches the
            // client's exactly, and it is still owned by nobody.
            var customer = await db.Customers.AsNoTracking().SingleAsync();
            Assert.Null(customer.UserId);
        }
    }

    [SqlServerFact]
    public async Task The_migration_refuses_to_choose_between_two_logins_sharing_an_address()
    {
        await using var database = await SqlIdentityTestDatabase.CreateAsync();
        var options = Options(database.ConnectionString);

        await using (var db = new AppDbContext(options))
            await db.GetService<IMigrator>()
                .MigrateAsync("20260906233827_AddPaymentAttemptKey");

        await using (var db = new AppDbContext(options))
        {
            await db.Database.ExecuteSqlRawAsync(@"
INSERT INTO [Users] ([RoleId],[FullName],[Email],[Password],[AccountStatus])
VALUES (2, N'First',  N'Ambiguous@Example.com', N'$2a$11$abcdefghijklmnopqrstuv', 0),
       (2, N'Second', N'ambiguous@example.com', N'$2a$11$abcdefghijklmnopqrstuv', 0);
");
        }

        await using (var db = new AppDbContext(options))
        {
            var failure = await Assert.ThrowsAnyAsync<SqlException>(() => db.Database.MigrateAsync());

            // Actionable, and specific about what a human has to decide. Silently merging or
            // deleting one of them would move somebody's bookings to another login.
            Assert.Contains("more than one login shares the same email address", failure.Message);
            Assert.Contains("AMBIGUOUS@EXAMPLE.COM", failure.Message);
            Assert.Contains("will not merge, delete or rename accounts", failure.Message);
        }

        // And nothing was changed on the way to refusing.
        await using (var db = new AppDbContext(options))
        {
            var names = await db.Database
                .SqlQueryRaw<string>("SELECT [FullName] AS [Value] FROM [Users] ORDER BY [FullName]")
                .ToListAsync();
            Assert.Equal(["First", "Second"], names);
        }
    }

    private static string? ExtractToken(string body)
    {
        const string marker = "#token=";
        var start = body.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            return null;

        start += marker.Length;
        var end = body.IndexOfAny(['\r', '\n', ' '], start);
        return Uri.UnescapeDataString(end < 0 ? body[start..] : body[start..end]);
    }

    private static DbContextOptions<AppDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options;

    private sealed class CapturingEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public string ProviderName => "capture";

        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            lock (Sent)
                Sent.Add(message);
            return Task.FromResult(new EmailSendResult { Success = true });
        }
    }

    private sealed class FixedTokenService : ITokenService
    {
        public string GenerateAccessToken(User user, string roleName) => "access-token";
        public string GenerateRefreshToken() => "refresh-token";
    }

    /// <summary>A throwaway database per test, named so the drop can refuse anything else.</summary>
    private sealed class SqlIdentityTestDatabase : IAsyncDisposable
    {
        private const string Prefix = "DamsIdentityTests_";

        private readonly string _masterConnection;
        private readonly string _databaseName;
        public string ConnectionString { get; }

        private SqlIdentityTestDatabase(string masterConnection, string databaseName, string connectionString)
        {
            _masterConnection = masterConnection;
            _databaseName = databaseName;
            ConnectionString = connectionString;
        }

        public static async Task<SqlIdentityTestDatabase> CreateAsync()
        {
            var configured = Environment.GetEnvironmentVariable("DAMS_SQLSERVER_TEST_CONNECTION")
                ?? throw new InvalidOperationException("DAMS_SQLSERVER_TEST_CONNECTION is required.");
            var databaseName = $"{Prefix}{Guid.NewGuid():N}";
            var master = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" };
            var test = new SqlConnectionStringBuilder(configured) { InitialCatalog = databaseName };

            await using var connection = new SqlConnection(master.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand($"CREATE DATABASE [{databaseName}]", connection);
            await command.ExecuteNonQueryAsync();

            return new SqlIdentityTestDatabase(master.ConnectionString, databaseName, test.ConnectionString);
        }

        public async ValueTask DisposeAsync()
        {
            if (!_databaseName.StartsWith(Prefix, StringComparison.Ordinal)
                || _databaseName.Length != Prefix.Length + 32)
                throw new InvalidOperationException("Refusing to drop an unexpected SQL test database.");

            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(_masterConnection);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}];",
                connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
