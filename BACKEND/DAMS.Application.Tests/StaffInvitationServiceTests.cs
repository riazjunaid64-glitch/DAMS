using System.Security.Cryptography;
using System.Text;
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
        Assert.Equal(Sha256(token), invitation.TokenHash);
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
        var firstHash = Sha256(firstToken);

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
        Assert.Equal(Sha256(secondToken), outstanding.TokenHash);
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
        Assert.Equal(Sha256(h.LastEmailedToken()), outstanding.TokenHash);
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

    // ── Activation: spending the token ──────────────────────────────────────────

    private const string ChosenPassword = "sana-chose-this-1";

    [Fact]
    public async Task A_valid_link_sets_the_employees_own_password_and_activates_the_login()
    {
        await using var h = await Harness.CreateAsync();
        await h.Service.IssueAsync(h.InvitedUserId, h.AdminUserId);
        var token = h.LastEmailedToken();

        // A session that should not exist on an invited login, to prove activation clears it.
        await h.GiveInvitedAStaleSessionAsync();

        h.Clock.Set(h.Clock.UtcNow.AddHours(3));
        var result = await h.Service.ActivateAsync(token, ChosenPassword);

        Assert.True(result.Activated);
        Assert.Equal(StaffActivationFailure.None, result.Failure);
        Assert.Null(result.Error);

        var user = await h.ReloadAsync(h.InvitedUserId);
        Assert.Equal(UserAccountStatus.Active, user.AccountStatus);

        // The employee's own password, verifiable but not readable, and not the raw string.
        Assert.NotNull(user.Password);
        Assert.True(BCrypt.Net.BCrypt.Verify(ChosenPassword, user.Password));
        Assert.NotEqual(ChosenPassword, user.Password);
        Assert.DoesNotContain(ChosenPassword, user.Password, StringComparison.Ordinal);

        // Activating is not signing in: no session is created, and any stale one is gone.
        Assert.Null(user.RefreshToken);
        Assert.Null(user.RefreshTokenExpiresAt);

        var invitation = await h.OnlyInvitationAsync();
        Assert.Equal(h.Clock.UtcNow, invitation.AcceptedAt);
        Assert.Null(invitation.RevokedAt);

        // Nothing about the consumed row can reconstruct either secret.
        Assert.DoesNotContain(token, invitation.TokenHash, StringComparison.Ordinal);
        Assert.DoesNotContain(ChosenPassword, invitation.TokenHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Activation_sends_nothing_and_needs_no_public_site_address()
    {
        // No PublicBaseUrl configured at all: consuming a token is a local state change and
        // must not reach for a setting or an SMTP server.
        await using var h = await Harness.CreateAsync(publicBaseUrl: null);
        await h.SeedInvitationAsync(h.InvitedUserId, "seeded-token-value");

        var result = await h.Service.ActivateAsync("seeded-token-value", ChosenPassword);

        Assert.True(result.Activated);
        Assert.Empty(h.Email.Sent);
    }

    [Fact]
    public async Task Accepting_one_link_kills_every_other_one_still_outstanding()
    {
        await using var h = await Harness.CreateAsync();
        await h.Service.IssueAsync(h.InvitedUserId, h.AdminUserId);
        var token = h.LastEmailedToken();

        // A second live invitation, as an imported or historic row could leave behind.
        await h.SeedInvitationAsync(h.InvitedUserId, "sibling-token-value");

        Assert.True((await h.Service.ActivateAsync(token, ChosenPassword)).Activated);

        var invitations = await h.InvitationsAsync();
        Assert.Equal(2, invitations.Count);
        Assert.All(invitations, i => Assert.True(i.AcceptedAt != null || i.RevokedAt != null));

        // And the sibling really is spent, not merely marked.
        AssertSafelyRejected(await h.Service.ActivateAsync("sibling-token-value", "another-password-9"));
    }

    // ── Activation: every link that must not work ───────────────────────────────

    [Fact]
    public async Task A_token_nobody_ever_issued_is_rejected()
    {
        await using var h = await Harness.CreateAsync();
        await h.Service.IssueAsync(h.InvitedUserId, h.AdminUserId);

        AssertSafelyRejected(await h.Service.ActivateAsync("kIuGf2sVnQ0-guessed-token", ChosenPassword));
        await AssertStillWaitingAsync(h);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_token_is_rejected_without_touching_anything(string token)
    {
        await using var h = await Harness.CreateAsync();
        await h.Service.IssueAsync(h.InvitedUserId, h.AdminUserId);

        AssertSafelyRejected(await h.Service.ActivateAsync(token, ChosenPassword));
        await AssertStillWaitingAsync(h);
    }

    [Fact]
    public async Task An_expired_link_is_rejected()
    {
        await using var h = await Harness.CreateAsync();
        await h.Service.IssueAsync(h.InvitedUserId, h.AdminUserId);
        var token = h.LastEmailedToken();

        h.Clock.Set(h.Clock.UtcNow.AddHours(24).AddSeconds(1));

        AssertSafelyRejected(await h.Service.ActivateAsync(token, ChosenPassword));
        await AssertStillWaitingAsync(h);
    }

    [Fact]
    public async Task A_link_a_resend_superseded_is_rejected()
    {
        await using var h = await Harness.CreateAsync();
        await h.Service.IssueAsync(h.InvitedUserId, h.AdminUserId);
        var firstToken = h.LastEmailedToken();

        h.Clock.Set(h.Clock.UtcNow.AddMinutes(5));
        await h.Service.ResendAsync(h.InvitedUserId, h.AdminUserId);

        AssertSafelyRejected(await h.Service.ActivateAsync(firstToken, ChosenPassword));
        await AssertStillWaitingAsync(h);
    }

    [Fact]
    public async Task An_already_accepted_link_is_rejected()
    {
        await using var h = await Harness.CreateAsync();
        await h.SeedInvitationAsync(h.InvitedUserId, "spent-token-value",
            acceptedAt: h.Clock.UtcNow.AddMinutes(-1));

        AssertSafelyRejected(await h.Service.ActivateAsync("spent-token-value", ChosenPassword));
        await AssertStillWaitingAsync(h);
    }

    [Fact]
    public async Task The_same_link_cannot_be_spent_twice()
    {
        await using var h = await Harness.CreateAsync();
        await h.Service.IssueAsync(h.InvitedUserId, h.AdminUserId);
        var token = h.LastEmailedToken();

        Assert.True((await h.Service.ActivateAsync(token, ChosenPassword)).Activated);
        var afterFirst = (await h.ReloadAsync(h.InvitedUserId)).Password;

        // A replay must not be able to set a second password on an account it no longer owns.
        AssertSafelyRejected(await h.Service.ActivateAsync(token, "attacker-chosen-2"));

        var user = await h.ReloadAsync(h.InvitedUserId);
        Assert.Equal(afterFirst, user.Password);
        Assert.True(BCrypt.Net.BCrypt.Verify(ChosenPassword, user.Password));
        Assert.False(BCrypt.Net.BCrypt.Verify("attacker-chosen-2", user.Password));
    }

    [Fact]
    public async Task A_link_to_a_login_that_is_already_active_is_rejected()
    {
        await using var h = await Harness.CreateAsync();
        await h.SeedInvitationAsync(h.ActiveUserId, "active-token-value");
        var before = (await h.ReloadAsync(h.ActiveUserId)).Password;

        AssertSafelyRejected(await h.Service.ActivateAsync("active-token-value", ChosenPassword));

        // An activation link is not a password reset for a working account.
        var user = await h.ReloadAsync(h.ActiveUserId);
        Assert.Equal(before, user.Password);
        Assert.Equal(UserAccountStatus.Active, user.AccountStatus);
    }

    [Fact]
    public async Task A_link_to_a_disabled_login_is_rejected_and_does_not_reactivate_it()
    {
        await using var h = await Harness.CreateAsync();
        await h.SeedInvitationAsync(h.DisabledUserId, "disabled-token-value");

        AssertSafelyRejected(await h.Service.ActivateAsync("disabled-token-value", ChosenPassword));

        var user = await h.ReloadAsync(h.DisabledUserId);
        Assert.Equal(UserAccountStatus.Disabled, user.AccountStatus);
        Assert.Equal("hash", user.Password);
    }

    [Fact]
    public async Task A_link_to_a_login_that_no_longer_exists_is_rejected()
    {
        await using var h = await Harness.CreateAsync();
        await h.Service.IssueAsync(h.InvitedUserId, h.AdminUserId);
        var token = h.LastEmailedToken();

        await h.DeleteInvitedLoginAsync();

        AssertSafelyRejected(await h.Service.ActivateAsync(token, ChosenPassword));
    }

    [Fact]
    public async Task A_link_belonging_to_no_employee_record_is_rejected()
    {
        await using var h = await Harness.CreateAsync();
        await h.Service.IssueAsync(h.InvitedUserId, h.AdminUserId);
        var token = h.LastEmailedToken();

        // The employment record went away between the invitation and the click.
        await h.DeleteInvitedEmployeeAsync();

        AssertSafelyRejected(await h.Service.ActivateAsync(token, ChosenPassword));
        await AssertStillWaitingAsync(h);
    }

    [Theory]
    [InlineData(EmployeeStatus.Inactive)]
    [InlineData(EmployeeStatus.Terminated)]
    public async Task A_link_for_somebody_who_no_longer_works_here_is_rejected(EmployeeStatus status)
    {
        await using var h = await Harness.CreateAsync();
        await h.Service.IssueAsync(h.InvitedUserId, h.AdminUserId);
        var token = h.LastEmailedToken();

        // Yesterday's invitation must not outlive the employment it was granted for.
        await h.SetInvitedEmployeeStatusAsync(status);

        AssertSafelyRejected(await h.Service.ActivateAsync(token, ChosenPassword));
        await AssertStillWaitingAsync(h);
    }

    // ── Activation: the password the employee chooses ───────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("short7!")]
    public async Task A_password_under_the_floor_is_refused_and_leaves_the_link_usable(string password)
    {
        await using var h = await Harness.CreateAsync();
        await h.Service.IssueAsync(h.InvitedUserId, h.AdminUserId);
        var token = h.LastEmailedToken();

        var result = await h.Service.ActivateAsync(token, password);

        Assert.False(result.Activated);
        Assert.Equal(StaffActivationFailure.PasswordTooShort, result.Failure);
        await AssertStillWaitingAsync(h);

        // A typo must not burn the invitation.
        Assert.True((await h.Service.ActivateAsync(token, ChosenPassword)).Activated);
    }

    [Theory]
    // 73 ASCII characters, and 40 accented ones — 80 bytes — which is the same problem in
    // a form a character count would miss.
    [InlineData(73, 'a')]
    [InlineData(40, 'é')]
    public async Task A_password_bcrypt_could_not_hash_whole_is_refused_rather_than_truncated(
        int length, char filler)
    {
        await using var h = await Harness.CreateAsync();
        await h.Service.IssueAsync(h.InvitedUserId, h.AdminUserId);
        var token = h.LastEmailedToken();

        var result = await h.Service.ActivateAsync(token, new string(filler, length));

        // Accepting it would mean a different, longer password opened the same account.
        Assert.False(result.Activated);
        Assert.Equal(StaffActivationFailure.PasswordTooLong, result.Failure);
        await AssertStillWaitingAsync(h);
    }

    /// <summary>
    /// Every bad link answers identically. An anonymous caller who tries a guessed token, an
    /// expired one and one belonging to a disabled account cannot tell from the reply which
    /// accounts exist or what state they are in.
    /// </summary>
    private static void AssertSafelyRejected(StaffActivationResult result)
    {
        Assert.False(result.Activated);
        Assert.Equal(StaffActivationFailure.InvalidInvitation, result.Failure);
        Assert.Equal(StaffActivationResult.InvalidInvitationMessage, result.Error);
    }

    /// <summary>The invited login is exactly as it was: no password, still waiting.</summary>
    private static async Task AssertStillWaitingAsync(Harness h)
    {
        var user = await h.ReloadInvitedAsync();
        Assert.Equal(UserAccountStatus.Invited, user.AccountStatus);
        Assert.Null(user.Password);
    }

    /// <summary>The same hash the service stores, computed independently of it.</summary>
    private static string Sha256(string value) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    /// <summary>
    /// One Admin and three logins — Active, Invited and Disabled — each with an active
    /// employment record behind it, wired to the real service over an in-memory DAMS, a
    /// recording email fake and a clock the test drives.
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

            // Activation requires a live employment record, so each login has one and the
            // tests that care take it away rather than arranging its absence.
            db.Employees.AddRange(
                NewEmployee(invited), NewEmployee(active), NewEmployee(disabled));
            await db.SaveChangesAsync();

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

        public Task<User> ReloadInvitedAsync() => ReloadAsync(InvitedUserId);

        public async Task<User> ReloadAsync(int userId)
        {
            Db.ChangeTracker.Clear();
            return await Db.Users.AsNoTracking().FirstAsync(u => u.UserId == userId);
        }

        /// <summary>
        /// Writes an invitation directly, for the states the service will not produce itself:
        /// a link for an account that is no longer invitable, one that is already spent, or a
        /// second live one alongside the issued token.
        /// </summary>
        public async Task SeedInvitationAsync(
            int userId, string rawToken, DateTime? acceptedAt = null, DateTime? revokedAt = null)
        {
            Db.StaffInvitations.Add(new StaffInvitation
            {
                UserId = userId,
                InvitedByUserId = AdminUserId,
                TokenHash = Sha256(rawToken),
                CreatedAt = Clock.UtcNow,
                ExpiresAt = Clock.UtcNow.AddHours(24),
                AcceptedAt = acceptedAt,
                RevokedAt = revokedAt
            });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        /// <summary>A session an invited login should never have, to prove activation clears it.</summary>
        public async Task GiveInvitedAStaleSessionAsync()
        {
            var user = await Db.Users.FirstAsync(u => u.UserId == InvitedUserId);
            user.RefreshToken = "stale-session-hash";
            user.RefreshTokenExpiresAt = Clock.UtcNow.AddDays(15);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task SetInvitedEmployeeStatusAsync(EmployeeStatus status)
        {
            var employee = await Db.Employees.FirstAsync(e => e.UserId == InvitedUserId);
            employee.Status = status;
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task DeleteInvitedEmployeeAsync()
        {
            Db.Employees.Remove(await Db.Employees.FirstAsync(e => e.UserId == InvitedUserId));
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task DeleteInvitedLoginAsync()
        {
            await DeleteInvitedEmployeeAsync();
            Db.Users.Remove(await Db.Users.FirstAsync(u => u.UserId == InvitedUserId));
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
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

        private static Employee NewEmployee(User user) => new()
        {
            FullName = user.FullName,
            UserId = user.UserId,
            Email = user.Email,
            JobTitle = "Sales Executive",
            JoinDate = new DateTime(2026, 1, 1),
            Status = EmployeeStatus.Active
        };

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
