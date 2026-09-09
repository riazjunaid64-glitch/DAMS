using DAMS.Application.Common;
using DAMS.Application.DTOs.Auth;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using DAMS.Application.Services.Notifications;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Domain.Identity;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// The client email-verification credential, end to end.
///
/// <para>
/// The attack this whole workflow exists to stop is short: register with a buyer's email address,
/// choose a password, sign in, read their bookings. Every test here removes one step of it. The
/// account is not usable when it is created; the link is the only way to make it usable; the link
/// is unguessable, expires, works once, and dies when a newer one is issued; and the password is
/// set by whoever redeems the link rather than by whoever filled in the form.
/// </para>
///
/// <para>
/// Redemption failures are all asserted to look identical from outside. A verification endpoint
/// that distinguished "expired" from "already used" from "never existed" would tell a stranger
/// which addresses have DAMS accounts and which links have been clicked.
/// </para>
/// </summary>
public sealed class ClientEmailVerificationTests
{
    private const string ChosenPassword = "mailbox-owner-pass-1";

    // ── Issuance ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Registering_stores_only_a_hash_and_emails_a_link_that_is_not_in_the_database()
    {
        await using var h = await Harness.CreateAsync();
        await h.RegisterAsync("buyer@example.com");

        var token = h.LastEmailedToken();
        Assert.NotNull(token);

        // 32 random bytes as URL-safe base64. Length is asserted rather than the value because the
        // value is the point: it is guessed, never derived from a user id or an address.
        Assert.Equal(43, token!.Length);
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);

        var stored = await h.Db.ClientEmailVerifications.AsNoTracking().SingleAsync();
        Assert.NotEqual(token, stored.TokenHash);
        Assert.DoesNotContain(token, stored.TokenHash);

        // The link travels in the fragment, which no server, proxy or CDN ever receives.
        var link = h.Email.Sent.Single();
        Assert.Contains($"/verify-email#token={token}", link.TextBody);
        Assert.DoesNotContain("?token=", link.TextBody);
    }

    [Fact]
    public async Task A_verification_link_is_built_only_from_the_configured_origin()
    {
        await using var h = await Harness.CreateAsync(publicBaseUrl: null);
        await h.RegisterAsync("buyer@example.com");

        // Nothing trustworthy to build a link from, so nothing is sent — rather than a link
        // assembled out of a request header the browser controls.
        Assert.Empty(h.Email.Sent);

        // The credential is still stored, so a resend can deliver it once the setting exists.
        Assert.Equal(1, await h.Db.ClientEmailVerifications.CountAsync());
    }

    // ── Redemption ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Redeeming_a_valid_link_sets_the_password_marks_it_verified_and_activates()
    {
        await using var h = await Harness.CreateAsync();
        await h.RegisterAsync("buyer@example.com");
        var token = h.LastEmailedToken()!;

        var result = await h.Verification.VerifyAsync(token, ChosenPassword);
        Assert.True(result.Verified);

        var user = await h.ReloadAsync("buyer@example.com");
        Assert.Equal(UserAccountStatus.Active, user.AccountStatus);
        Assert.NotNull(user.EmailVerifiedAt);
        Assert.True(BCrypt.Net.BCrypt.Verify(ChosenPassword, user.Password));

        // Verifying is not signing in. No session comes out of it.
        Assert.Null(user.RefreshToken);

        // ...and the ordinary login now works with the password the mailbox owner chose.
        Assert.NotNull(await h.Auth.LoginAsync(
            new LoginRequestDto { Email = "buyer@example.com", Password = ChosenPassword }));

        var stored = await h.Db.ClientEmailVerifications.AsNoTracking().SingleAsync();
        Assert.NotNull(stored.VerifiedAt);
    }

    [Fact]
    public async Task The_password_is_established_by_the_redeemer_not_the_registrant()
    {
        await using var h = await Harness.CreateAsync();

        // The exact attack. Somebody registers a buyer's address; only the buyer gets the mail,
        // and only the buyer's password ends up on the account.
        await h.RegisterAsync("victim@example.com");
        var token = h.LastEmailedToken()!;

        Assert.True((await h.Verification.VerifyAsync(token, "victims-own-password-9")).Verified);

        var user = await h.ReloadAsync("victim@example.com");
        Assert.True(BCrypt.Net.BCrypt.Verify("victims-own-password-9", user.Password));

        // There was never a moment where an attacker-chosen password existed to verify against.
        Assert.Null(await h.Auth.LoginAsync(
            new LoginRequestDto { Email = "victim@example.com", Password = "attacker-chosen-pass" }));
    }

    [Fact]
    public async Task An_unknown_token_is_refused()
    {
        await using var h = await Harness.CreateAsync();
        await h.RegisterAsync("buyer@example.com");

        AssertGenericRefusal(await h.Verification.VerifyAsync("a-token-nobody-ever-issued", ChosenPassword));
    }

    [Fact]
    public async Task An_expired_token_is_refused()
    {
        await using var h = await Harness.CreateAsync();
        await h.RegisterAsync("buyer@example.com");
        var token = h.LastEmailedToken()!;

        // One second past the twenty-four hour window.
        h.Clock.Advance(ClientEmailVerificationService.VerificationLifetime + TimeSpan.FromSeconds(1));

        AssertGenericRefusal(await h.Verification.VerifyAsync(token, ChosenPassword));

        var user = await h.ReloadAsync("buyer@example.com");
        Assert.Equal(UserAccountStatus.PendingEmailVerification, user.AccountStatus);
        Assert.Null(user.Password);
    }

    [Fact]
    public async Task A_consumed_token_cannot_be_replayed()
    {
        await using var h = await Harness.CreateAsync();
        await h.RegisterAsync("buyer@example.com");
        var token = h.LastEmailedToken()!;

        Assert.True((await h.Verification.VerifyAsync(token, ChosenPassword)).Verified);

        // The second attempt is what an intercepted link, a shared screenshot or a browser
        // back-button would produce. It must not be able to set a second password.
        AssertGenericRefusal(await h.Verification.VerifyAsync(token, "someone-elses-password"));

        var user = await h.ReloadAsync("buyer@example.com");
        Assert.True(BCrypt.Net.BCrypt.Verify(ChosenPassword, user.Password));
    }

    [Fact]
    public async Task Resending_revokes_the_previous_link()
    {
        await using var h = await Harness.CreateAsync();
        await h.RegisterAsync("buyer@example.com");
        var first = h.LastEmailedToken()!;

        await h.Verification.ResendAsync("buyer@example.com");
        var second = h.LastEmailedToken()!;
        Assert.NotEqual(first, second);

        // An older email still sitting in the same inbox is dead. Without this, every link ever
        // sent to an address would remain a way to change that account's password.
        AssertGenericRefusal(await h.Verification.VerifyAsync(first, ChosenPassword));
        Assert.True((await h.Verification.VerifyAsync(second, ChosenPassword)).Verified);

        var superseded = await h.Db.ClientEmailVerifications.AsNoTracking()
            .SingleAsync(v => v.Id == 1);
        Assert.NotNull(superseded.RevokedAt);
        Assert.Null(superseded.VerifiedAt);
    }

    [Fact]
    public async Task A_revoked_token_is_refused()
    {
        await using var h = await Harness.CreateAsync();
        await h.RegisterAsync("buyer@example.com");
        var token = h.LastEmailedToken()!;

        var stored = await h.Db.ClientEmailVerifications.SingleAsync();
        stored.RevokedAt = h.Clock.GetUtcNow().UtcDateTime;
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        AssertGenericRefusal(await h.Verification.VerifyAsync(token, ChosenPassword));
    }

    [Fact]
    public async Task Two_simultaneous_redemptions_of_one_token_produce_exactly_one_verification()
    {
        await using var h = await Harness.CreateAsync();
        await h.RegisterAsync("buyer@example.com");
        var token = h.LastEmailedToken()!;

        // Two clicks in flight at once — the double-click, or a mail client prefetching the link.
        // The in-memory provider serialises them, which is enough to prove the service's own
        // re-check inside the transaction refuses the loser; the serialisable transaction that
        // makes this hold under real concurrency is exercised against SQL Server separately.
        var first = await h.Verification.VerifyAsync(token, ChosenPassword);
        var second = await h.Verification.VerifyAsync(token, "a-different-password-2");

        Assert.True(first.Verified);
        AssertGenericRefusal(second);

        Assert.Equal(1, await h.Db.ClientEmailVerifications.CountAsync(v => v.VerifiedAt != null));
        var user = await h.ReloadAsync("buyer@example.com");
        Assert.True(BCrypt.Net.BCrypt.Verify(ChosenPassword, user.Password));
    }

    [Fact]
    public async Task Every_refusal_says_exactly_the_same_thing()
    {
        await using var h = await Harness.CreateAsync();
        await h.RegisterAsync("buyer@example.com");
        var live = h.LastEmailedToken()!;

        await h.Verification.ResendAsync("buyer@example.com");
        var current = h.LastEmailedToken()!;
        Assert.True((await h.Verification.VerifyAsync(current, ChosenPassword)).Verified);

        // Unknown, superseded, and already-consumed. Three different internal states; one answer,
        // because telling them apart tells a stranger which addresses are registered and which
        // links have already been clicked.
        var answers = new[]
        {
            await h.Verification.VerifyAsync("never-issued-at-all", ChosenPassword),
            await h.Verification.VerifyAsync(live, ChosenPassword),
            await h.Verification.VerifyAsync(current, ChosenPassword)
        };

        Assert.All(answers, a => AssertGenericRefusal(a));
        Assert.Single(answers.Select(a => a.Error).Distinct());
    }

    // ── Password rules ──────────────────────────────────────────────────────────

    [Fact]
    public async Task A_password_the_customer_can_fix_is_named_and_the_token_survives()
    {
        await using var h = await Harness.CreateAsync();
        await h.RegisterAsync("buyer@example.com");
        var token = h.LastEmailedToken()!;

        var tooShort = await h.Verification.VerifyAsync(token, "short");
        Assert.Equal(ClientVerificationFailure.PasswordTooShort, tooShort.Failure);

        // Over bcrypt's 72-byte boundary, which would otherwise be silently truncated — leaving
        // two different passwords opening the same account.
        var tooLong = await h.Verification.VerifyAsync(token, new string('x', 73));
        Assert.Equal(ClientVerificationFailure.PasswordTooLong, tooLong.Failure);

        // Neither mistake spent the link. A single-use credential must not be burned by a typo.
        Assert.True((await h.Verification.VerifyAsync(token, ChosenPassword)).Verified);
    }

    // ── Resend ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Resend_does_nothing_and_says_nothing_for_an_address_that_cannot_use_one()
    {
        await using var h = await Harness.CreateAsync();

        // Never registered.
        await h.Verification.ResendAsync("stranger@example.com");
        Assert.Empty(h.Email.Sent);

        // Registered, verified, and now a working account — a resend here would be a way to
        // interfere with somebody's live login.
        await h.RegisterAsync("buyer@example.com");
        Assert.True((await h.Verification.VerifyAsync(h.LastEmailedToken()!, ChosenPassword)).Verified);
        h.Email.Sent.Clear();

        await h.Verification.ResendAsync("buyer@example.com");
        Assert.Empty(h.Email.Sent);

        var user = await h.ReloadAsync("buyer@example.com");
        Assert.Equal(UserAccountStatus.Active, user.AccountStatus);
        Assert.True(BCrypt.Net.BCrypt.Verify(ChosenPassword, user.Password));
    }

    [Fact]
    public async Task Verification_only_opens_a_client_account()
    {
        await using var h = await Harness.CreateAsync();
        await h.RegisterAsync("buyer@example.com");
        var token = h.LastEmailedToken()!;

        // The login is given a staff role between issuance and redemption. Staff activation
        // applies employment checks this workflow knows nothing about, so this credential must
        // stop working rather than open an employee account by the client route.
        var user = await h.Db.Users.FirstAsync(u => u.NormalizedEmail == "BUYER@EXAMPLE.COM");
        user.RoleId = 3;
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        AssertGenericRefusal(await h.Verification.VerifyAsync(token, ChosenPassword));
    }

    private static void AssertGenericRefusal(ClientVerificationResult result)
    {
        Assert.False(result.Verified);
        Assert.Equal(ClientVerificationFailure.InvalidVerification, result.Failure);
        Assert.Equal(
            "This verification link is invalid or has expired. Request a new one and try again.",
            result.Error);
    }

    /// <summary>
    /// The real <see cref="ClientEmailVerificationService"/> and <see cref="AuthService"/> over an
    /// in-memory store, with a controllable clock and an email sender that keeps what it was
    /// given. The token is recovered from the delivered message rather than from the database,
    /// which is the only place it ever exists — and asserting that is half the point.
    /// </summary>
    private sealed class Harness : IAsyncDisposable
    {
        public AppDbContext Db { get; }
        public ClientEmailVerificationService Verification { get; }
        public AuthService Auth { get; }
        public CapturingEmailSender Email { get; }
        public TestClock Clock { get; }

        private Harness(AppDbContext db, TestClock clock, CapturingEmailSender email)
        {
            Db = db;
            Clock = clock;
            Email = email;
            Verification = new ClientEmailVerificationService(
                db, new NotificationSettingsStore(db), email, clock);
            Auth = new AuthService(db, new FixedTokenService(), Verification);
        }

        public static async Task<Harness> CreateAsync(string? publicBaseUrl = "https://dams.example")
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

            db.Roles.AddRange(
                new Role { RoleId = 1, Role_name = "Admin" },
                new Role { RoleId = 2, Role_name = "Client" },
                new Role { RoleId = 3, Role_name = "Manager" });

            if (publicBaseUrl != null)
                db.NotificationSettings.Add(new NotificationSetting
                {
                    Key = NotificationSettingKeys.PublicBaseUrl,
                    Value = publicBaseUrl
                });

            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            return new Harness(db, new TestClock(new DateTime(2026, 9, 9, 8, 0, 0, DateTimeKind.Utc)),
                new CapturingEmailSender());
        }

        public async Task RegisterAsync(string email)
        {
            await Auth.RegisterAsync(new RegisterRequestDto { FullName = "Bilal Buyer", Email = email });
            Db.ChangeTracker.Clear();
        }

        /// <summary>The raw token as the customer would read it out of their inbox.</summary>
        public string? LastEmailedToken()
        {
            var body = Email.Sent.LastOrDefault()?.TextBody;
            if (body == null)
                return null;

            const string marker = "#token=";
            var start = body.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
                return null;

            start += marker.Length;
            var end = body.IndexOfAny(['\r', '\n', ' '], start);
            return Uri.UnescapeDataString(end < 0 ? body[start..] : body[start..end]);
        }

        public async Task<User> ReloadAsync(string email)
        {
            Db.ChangeTracker.Clear();
            var normalized = EmailIdentity.Normalize(email);
            return await Db.Users.AsNoTracking().FirstAsync(u => u.NormalizedEmail == normalized);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public string ProviderName => "capture";

        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.FromResult(new EmailSendResult { Success = true });
        }
    }

    private sealed class TestClock(DateTime start) : TimeProvider
    {
        private DateTimeOffset _now = new(start, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    private sealed class FixedTokenService : ITokenService
    {
        public string GenerateAccessToken(User user, string roleName) => "access-token";
        public string GenerateRefreshToken() => "refresh-token";
    }
}
