using System.Text.RegularExpressions;
using DAMS.Application.Common;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using DAMS.Application.Services.Notifications;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// The invitation engine: what is minted, what is stored, where the link points, and what
/// survives a failed send. The token is treated throughout as a credential — the tests only
/// ever learn it the way the employee does, by reading the email that was sent.
/// </summary>
public sealed class StaffInvitationServiceTests
{
    private const string BaseUrl = "https://dams.test";

    // ── Successful invitation ───────────────────────────────────────────────────

    [Fact]
    public async Task An_invited_login_gets_a_persisted_invitation_and_exactly_one_email()
    {
        await using var h = await Harness.CreateAsync();

        var result = await h.Service.IssueAsync(h.InvitedUserId, h.AdminUserId);

        Assert.True(result.Issued);
        Assert.True(result.EmailSent);
        Assert.Equal(StaffInvitationFailure.None, result.Failure);

        var invitation = await h.OnlyInvitationAsync();
        Assert.Equal(invitation.Id, result.InvitationId);
        Assert.Equal(h.AdminUserId, invitation.InvitedByUserId);
        Assert.Null(invitation.AcceptedAt);
        Assert.Null(invitation.RevokedAt);

        var sent = Assert.Single(h.Email.Sent);
        Assert.Equal("invited@dams.test", sent.To);
    }

    [Fact]
    public async Task Issuing_an_invitation_does_not_activate_the_login_or_give_it_a_password()
    {
        await using var h = await Harness.CreateAsync();

        await h.Service.IssueAsync(h.InvitedUserId, h.AdminUserId);

        var user = await h.ReloadInvitedAsync();
        Assert.Equal(UserAccountStatus.Invited, user.AccountStatus);
        Assert.Null(user.Password);
    }

    [Fact]
    public async Task Expiry_is_twenty_four_hours_from_the_controlled_clock()
    {
        await using var h = await Harness.CreateAsync();
        var now = new DateTime(2026, 8, 22, 9, 30, 0, DateTimeKind.Utc);
        h.Clock.Set(now);

        var result = await h.Service.IssueAsync(h.InvitedUserId, h.AdminUserId);

        var invitation = await h.OnlyInvitationAsync();
        Assert.Equal(now, invitation.CreatedAt);
        Assert.Equal(now.AddHours(24), invitation.ExpiresAt);
        Assert.Equal(invitation.ExpiresAt, result.ExpiresAt);
    }

    // ── Token security ──────────────────────────────────────────────────────────

    [Fact]
    public async Task The_stored_hash_is_the_hash_of_the_emailed_token_and_never_the_token()
    {
        await using var h = await Harness.CreateAsync();

        await h.Service.IssueAsync(h.InvitedUserId, h.AdminUserId);

        var token = h.LastEmailedToken();
        var invitation = await h.OnlyInvitationAsync();

        // What the database holds verifies the emailed token without being able to produce it.
        Assert.Equal(StaffInvitationService.HashToken(token), invitation.TokenHash);
        Assert.NotEqual(token, invitation.TokenHash);
        Assert.DoesNotContain(token, invitation.TokenHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_token_is_high_entropy_url_safe_and_unrelated_to_any_identifier()
    {
        await using var h = await Harness.CreateAsync();
        var tokens = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < 12; i++)
        {
            var result = await h.Service.IssueAsync(h.InvitedUserId, h.AdminUserId);
            var token = h.LastEmailedToken();

            // 32 random bytes as unpadded base64url: 43 characters, none of them needing
            // escaping in a query string.
            Assert.Equal(43, token.Length);
            Assert.Matches(new Regex("^[A-Za-z0-9_-]+$"), token);

            // Nothing an Admin can see or count is the secret.
            Assert.NotEqual(h.InvitedUserId.ToString(), token);
            Assert.NotEqual(result.InvitationId!.Value.ToString(), token);
            Assert.False(Guid.TryParse(token, out _), "A Guid is not enough entropy for a credential.");

            Assert.True(tokens.Add(token), "A token repeated, so the generator is not random.");
        }
    }

    // ── Trusted activation URL ──────────────────────────────────────────────────

    [Fact]
    public async Task The_activation_link_is_built_from_the_configured_public_base_url()
    {
        await using var h = await Harness.CreateAsync();

        await h.Service.IssueAsync(h.InvitedUserId, h.AdminUserId);

        var url = h.LastActivationUrl();
        var token = h.LastEmailedToken();
        Assert.Equal($"{BaseUrl}/activate-account?token={token}", url);
    }

    [Theory]
    [InlineData("https://dams.test")]
    [InlineData("https://dams.test/")]
    [InlineData("  https://dams.test///  ")]
    public async Task A_trailing_slash_never_produces_a_double_slash_link(string configured)
    {
        await using var h = await Harness.CreateAsync(publicBaseUrl: configured);

        await h.Service.IssueAsync(h.InvitedUserId, h.AdminUserId);

        Assert.StartsWith("https://dams.test/activate-account?token=", h.LastActivationUrl());
    }

    [Fact]
    public async Task A_host_the_browser_could_supply_never_reaches_the_link()
    {
        // The only origin the service can see is the setting; there is no request in scope
        // for a Host, Origin or Referer header to leak into the email.
        await using var h = await Harness.CreateAsync(publicBaseUrl: "https://dams.test");
        await h.SetSettingAsync(NotificationSettingKeys.PublicBaseUrl, "https://dams.test");

        await h.Service.IssueAsync(h.InvitedUserId, h.AdminUserId);

        Assert.DoesNotContain("localhost", h.LastActivationUrl(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("attacker", h.LastActivationUrl(), StringComparison.OrdinalIgnoreCase);
    }

    // ── Resend ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_resend_mints_a_new_token_and_revokes_the_previous_invitation()
    {
        await using var h = await Harness.CreateAsync();

        await h.Service.IssueAsync(h.InvitedUserId, h.AdminUserId);
        var firstToken = h.LastEmailedToken();
        var firstHash = StaffInvitationService.HashToken(firstToken);

        h.Clock.Set(h.Clock.UtcNow.AddHours(2));
        var resend = await h.Service.ResendAsync(h.InvitedUserId, h.AdminUserId);
        var secondToken = h.LastEmailedToken();

        Assert.True(resend.EmailSent);
        Assert.NotEqual(firstToken, secondToken);
        Assert.Equal(2, h.Email.Sent.Count);

        var invitations = await h.InvitationsAsync();
        Assert.Equal(2, invitations.Count);

        // The superseded row is kept as history, but is no longer acceptable.
        var old = invitations.Single(i => i.TokenHash == firstHash);
        Assert.NotNull(old.RevokedAt);
        Assert.Equal(h.Clock.UtcNow, old.RevokedAt);

        // Exactly one outstanding invitation, and it is the new one.
        var outstanding = Assert.Single(invitations, i => i.RevokedAt == null && i.AcceptedAt == null);
        Assert.Equal(StaffInvitationService.HashToken(secondToken), outstanding.TokenHash);
        Assert.Equal(h.Clock.UtcNow.AddHours(24), outstanding.ExpiresAt);
    }

    [Fact]
    public async Task A_resend_does_not_extend_the_old_invitations_expiry()
    {
        await using var h = await Harness.CreateAsync();
        var start = h.Clock.UtcNow;

        await h.Service.IssueAsync(h.InvitedUserId, h.AdminUserId);
        h.Clock.Set(start.AddHours(3));
        await h.Service.ResendAsync(h.InvitedUserId, h.AdminUserId);

        var invitations = await h.InvitationsAsync();
        var old = invitations.Single(i => i.RevokedAt != null);
        Assert.Equal(start.AddHours(24), old.ExpiresAt);
    }

    // ── Account-state restrictions ──────────────────────────────────────────────

    [Fact]
    public async Task An_active_login_cannot_be_issued_an_activation_link()
    {
        await using var h = await Harness.CreateAsync();

        var result = await h.Service.IssueAsync(h.ActiveUserId, h.AdminUserId);

        Assert.False(result.Issued);
        Assert.False(result.EmailSent);
        Assert.Equal(StaffInvitationFailure.AccountNotInvitable, result.Failure);
        Assert.Empty(await h.InvitationsAsync());
        Assert.Empty(h.Email.Sent);
    }

    [Fact]
    public async Task A_disabled_login_cannot_be_issued_an_activation_link()
    {
        await using var h = await Harness.CreateAsync();

        var result = await h.Service.IssueAsync(h.DisabledUserId, h.AdminUserId);

        Assert.False(result.Issued);
        Assert.Equal(StaffInvitationFailure.AccountNotInvitable, result.Failure);
        Assert.Empty(await h.InvitationsAsync());
        Assert.Empty(h.Email.Sent);
    }

    [Fact]
    public async Task A_missing_login_is_rejected_cleanly()
    {
        await using var h = await Harness.CreateAsync();

        var result = await h.Service.IssueAsync(987654, h.AdminUserId);

        Assert.False(result.Issued);
        Assert.Equal(StaffInvitationFailure.UserNotFound, result.Failure);
        Assert.Empty(await h.InvitationsAsync());
        Assert.Empty(h.Email.Sent);
    }

    // ── Configuration and delivery failures ─────────────────────────────────────

    [Fact]
    public async Task An_unconfigured_public_base_url_keeps_the_invitation_and_sends_nothing()
    {
        await using var h = await Harness.CreateAsync(publicBaseUrl: null);

        var result = await h.Service.IssueAsync(h.InvitedUserId, h.AdminUserId);

        Assert.True(result.Issued);
        Assert.False(result.EmailSent);
        Assert.Equal(StaffInvitationFailure.PublicBaseUrlNotConfigured, result.Failure);

        // No half-usable email with a relative or invented link.
        Assert.Empty(h.Email.Sent);
        var invitation = await h.OnlyInvitationAsync();
        Assert.Null(invitation.RevokedAt);
    }

    [Fact]
    public async Task An_smtp_failure_leaves_the_invitation_persisted_and_the_account_untouched()
    {
        await using var h = await Harness.CreateAsync();
        h.Email.FailPermanently = true;

        var result = await h.Service.IssueAsync(h.InvitedUserId, h.AdminUserId);

        Assert.True(result.Issued);
        Assert.False(result.EmailSent);
        Assert.Equal(StaffInvitationFailure.EmailDeliveryFailed, result.Failure);

        var invitation = await h.OnlyInvitationAsync();
        Assert.Null(invitation.RevokedAt);

        var user = await h.ReloadInvitedAsync();
        Assert.Equal(UserAccountStatus.Invited, user.AccountStatus);
        Assert.Null(user.Password);
    }

    [Fact]
    public async Task A_resend_recovers_an_invitation_whose_email_failed()
    {
        await using var h = await Harness.CreateAsync();
        h.Email.FailPermanently = true;
        var failed = await h.Service.IssueAsync(h.InvitedUserId, h.AdminUserId);

        h.Email.FailPermanently = false;
        h.Clock.Set(h.Clock.UtcNow.AddMinutes(10));
        var retry = await h.Service.ResendAsync(h.InvitedUserId, h.AdminUserId);

        Assert.True(retry.EmailSent);
        Assert.NotEqual(failed.InvitationId, retry.InvitationId);

        var invitations = await h.InvitationsAsync();
        Assert.Equal(2, invitations.Count);
        var outstanding = Assert.Single(invitations, i => i.RevokedAt == null);
        Assert.Equal(retry.InvitationId, outstanding.Id);
        Assert.Equal(StaffInvitationService.HashToken(h.LastEmailedToken()), outstanding.TokenHash);
    }

    // ── Email safety ────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_email_carries_the_link_and_nothing_that_resembles_a_credential()
    {
        await using var h = await Harness.CreateAsync();

        await h.Service.IssueAsync(h.InvitedUserId, h.AdminUserId);

        var sent = Assert.Single(h.Email.Sent);
        foreach (var body in new[] { sent.HtmlBody, sent.TextBody, sent.Subject })
        {
            Assert.DoesNotContain("temporary password", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("your password is", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TokenHash", body, StringComparison.OrdinalIgnoreCase);
        }

        // The token appears only as part of the activation URL, never as a stray value.
        var token = h.LastEmailedToken();
        var url = h.LastActivationUrl();
        Assert.Equal(
            CountOccurrences(sent.TextBody, url),
            CountOccurrences(sent.TextBody, token));
        Assert.Equal(
            CountOccurrences(sent.HtmlBody, url),
            CountOccurrences(sent.HtmlBody, token));

        Assert.Contains("expires in 24 hours", sent.TextBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not expecting this invitation", sent.TextBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Company_and_employee_names_cannot_inject_html_into_the_email()
    {
        await using var h = await Harness.CreateAsync();
        await h.SetSettingAsync(NotificationSettingKeys.CompanyName, "<script>alert(1)</script> Estates");
        await h.RenameInvitedAsync("Ali \"O'Brien\" <img src=x onerror=alert(2)>");

        await h.Service.IssueAsync(h.InvitedUserId, h.AdminUserId);

        // No tag from either value survives as markup — they are text in the rendered mail.
        var sent = Assert.Single(h.Email.Sent);
        Assert.DoesNotContain("<script", sent.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", sent.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", sent.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("&lt;img src=x onerror=alert(2)&gt;", sent.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("&quot;O&#39;Brien&quot;", sent.HtmlBody, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    /// <summary>
    /// One Admin and three logins — Active, Invited and Disabled — wired to the real service
    /// over an in-memory DAMS, a recording email fake and a clock the test drives.
    /// </summary>
    private sealed class Harness : IAsyncDisposable
    {
        public AppDbContext Db { get; }
        public StaffInvitationService Service { get; }
        public NotificationTestHarness.FakeEmailSender Email { get; }
        public LeadTestHarness.FakeClock Clock { get; }
        public NotificationSettingsStore Settings { get; }

        public int AdminUserId { get; private set; }
        public int InvitedUserId { get; private set; }
        public int ActiveUserId { get; private set; }
        public int DisabledUserId { get; private set; }

        private Harness(AppDbContext db)
        {
            Db = db;
            Clock = new LeadTestHarness.FakeClock(new DateTime(2026, 8, 22, 8, 0, 0, DateTimeKind.Utc));
            Settings = new NotificationSettingsStore(db);
            Email = new NotificationTestHarness.FakeEmailSender();
            Service = new StaffInvitationService(db, Settings, Email, Clock);
        }

        public static async Task<Harness> CreateAsync(string? publicBaseUrl = BaseUrl)
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var h = new Harness(db);

            var admin = NewUser("Ayesha Admin", "admin@dams.test", UserAccountStatus.Active, "hash");
            var invited = NewUser("Sana Sales", "invited@dams.test", UserAccountStatus.Invited, null);
            var active = NewUser("Omar Sales", "active@dams.test", UserAccountStatus.Active, "hash");
            var disabled = NewUser("Zara Left", "disabled@dams.test", UserAccountStatus.Disabled, "hash");
            db.Users.AddRange(admin, invited, active, disabled);
            await db.SaveChangesAsync();

            h.AdminUserId = admin.UserId;
            h.InvitedUserId = invited.UserId;
            h.ActiveUserId = active.UserId;
            h.DisabledUserId = disabled.UserId;

            await h.Settings.SetAsync(NotificationSettingKeys.CompanyName, "DAMS Estates", h.AdminUserId);
            await h.Settings.SetAsync(NotificationSettingKeys.AppName, "DAMS", h.AdminUserId);
            if (publicBaseUrl != null)
                await h.Settings.SetAsync(NotificationSettingKeys.PublicBaseUrl, publicBaseUrl, h.AdminUserId);
            await db.SaveChangesAsync();

            return h;
        }

        public async Task SetSettingAsync(string key, string? value)
        {
            await Settings.SetAsync(key, value, AdminUserId);
            await Db.SaveChangesAsync();
        }

        public async Task RenameInvitedAsync(string fullName)
        {
            var user = await Db.Users.FirstAsync(u => u.UserId == InvitedUserId);
            user.FullName = fullName;
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public Task<List<StaffInvitation>> InvitationsAsync()
        {
            Db.ChangeTracker.Clear();
            return Db.StaffInvitations.AsNoTracking().ToListAsync();
        }

        public async Task<StaffInvitation> OnlyInvitationAsync() => (await InvitationsAsync()).Single();

        public async Task<User> ReloadInvitedAsync()
        {
            Db.ChangeTracker.Clear();
            return await Db.Users.AsNoTracking().FirstAsync(u => u.UserId == InvitedUserId);
        }

        /// <summary>The activation URL exactly as the employee would receive it.</summary>
        public string LastActivationUrl()
        {
            var text = Email.Sent[^1].TextBody;
            var match = Regex.Match(text, @"https?://\S*/activate-account\?token=\S+");
            Assert.True(match.Success, "No activation link was present in the email.");
            return match.Value;
        }

        /// <summary>
        /// The only place a test may learn the token: the email, like the employee. Nothing
        /// reads it out of the database, because the database does not have it.
        /// </summary>
        public string LastEmailedToken() =>
            Uri.UnescapeDataString(LastActivationUrl().Split("?token=", StringSplitOptions.None)[1]);

        private static User NewUser(string name, string email, UserAccountStatus status, string? password) => new()
        {
            RoleId = 1,
            FullName = name,
            Email = email,
            Password = password,
            AccountStatus = status
        };

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
