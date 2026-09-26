using System.Reflection;
using System.Text.Json;
using DAMS.Application.Common;
using DAMS.Application.DTOs.IntegrationDtos;
using DAMS.Application.Services.Integrations;
using DAMS.Domain.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace DAMS.Application.Tests.Integrations;

/// <summary>
/// The security properties this integration depends on: a one-time OAuth state, credentials
/// that never leave the server, and a disconnect that removes access without erasing history.
/// </summary>
public class MetaIntegrationSecurityTests
{
    [Fact]
    public async Task StartingAConnection_StoresOnlyAHashOfTheState()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();

        var start = await h.Integration.StartConnectAsync(h.Leads.Admin, "/crm/settings");

        var state = ExtractQueryValue(start.AuthorizationUrl, "state");
        Assert.False(string.IsNullOrWhiteSpace(state));

        var stored = await h.Db.ExternalIntegrationOAuthStates.SingleAsync();
        // A leaked database row must not be replayable against the callback.
        Assert.NotEqual(state, stored.StateHash);
        Assert.DoesNotContain(state!, stored.StateHash);
        Assert.Equal(h.Leads.AdminUserId, stored.CreatedByUserId);
    }

    [Fact]
    public async Task TheAuthorizationUrl_RequestsOnlyTheDocumentedScopes()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();

        var start = await h.Integration.StartConnectAsync(h.Leads.Admin, null);
        var scope = Uri.UnescapeDataString(ExtractQueryValue(start.AuthorizationUrl, "scope")!);

        Assert.Contains("leads_retrieval", scope);
        Assert.Contains("pages_show_list", scope);
        Assert.Contains("ads_read", scope);
        // Write access to advertising is out of scope for this integration and must not be
        // requested by accident.
        Assert.DoesNotContain("ads_management", scope);
        Assert.DoesNotContain("business_management", scope);
    }

    [Fact]
    public async Task TheAuthorizationUrl_AsksAgainForPreviouslyDeclinedPermissions()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();

        var start = await h.Integration.StartConnectAsync(h.Leads.Admin, null);

        // Reconnect goes through this same URL. Without rerequest Meta never re-shows a
        // permission the person declined once, so a missing lead-critical scope could not be
        // recovered and the connection would stay in NeedsReauthorization.
        Assert.Equal("rerequest", ExtractQueryValue(start.AuthorizationUrl, "auth_type"));
    }

    [Fact]
    public async Task WithoutALoginConfiguration_TheAuthorizationUrlIsTheClassicScopeDialog()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();

        var start = await h.Integration.StartConnectAsync(h.Leads.Admin, null);
        var state = ExtractQueryValue(start.AuthorizationUrl, "state")!;

        // Pinned exactly, so adding Facebook Login for Business support cannot drift the URL
        // every existing connection has been made through.
        Assert.Equal(
            "https://www.facebook.com/v21.0/dialog/oauth" +
            "?client_id=test-app-id" +
            $"&redirect_uri={Uri.EscapeDataString("https://dams.test/api/integrations/meta/callback")}" +
            $"&state={Uri.EscapeDataString(state)}" +
            "&response_type=code" +
            "&auth_type=rerequest" +
            $"&scope={Uri.EscapeDataString(MetaScopes.Joined)}",
            start.AuthorizationUrl);
        Assert.Null(ExtractQueryValue(start.AuthorizationUrl, "config_id"));
    }

    [Fact]
    public async Task WithALoginConfiguration_TheAuthorizationUrlSendsConfigIdInsteadOfScope()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        h.Options.LoginConfigId = " 1234567890123456 ";

        var start = await h.Integration.StartConnectAsync(h.Leads.Admin, "/crm/settings");
        var url = start.AuthorizationUrl;

        Assert.Equal("1234567890123456", ExtractQueryValue(url, "config_id"));
        Assert.Equal("test-app-id", ExtractQueryValue(url, "client_id"));
        Assert.Equal("https://dams.test/api/integrations/meta/callback", ExtractQueryValue(url, "redirect_uri"));
        Assert.Equal("code", ExtractQueryValue(url, "response_type"));
        Assert.False(string.IsNullOrWhiteSpace(ExtractQueryValue(url, "state")));
        // The login configuration owns the permission list; Meta recommends not sending scope
        // with it, and rerequest is a parameter of the scope-driven dialog only.
        Assert.Null(ExtractQueryValue(url, "scope"));
        Assert.Null(ExtractQueryValue(url, "auth_type"));
        // Only a User access token configuration is supported: the callback still exchanges
        // for a long-lived user token, which a system-user response type would bypass.
        Assert.Null(ExtractQueryValue(url, "override_default_response_type"));
    }

    [Theory]
    [InlineData(true, ExternalIntegrationConnectionStatus.Connected)]
    [InlineData(false, ExternalIntegrationConnectionStatus.NeedsReauthorization)]
    public async Task WithALoginConfiguration_TheCallbackStillChecksEveryLeadCriticalPermission(
        bool configurationGrantsEverything, ExternalIntegrationConnectionStatus expected)
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        h.Options.LoginConfigId = "1234567890123456";
        if (!configurationGrantsEverything)
            h.Graph.Authorization.GrantedScopes = [MetaScopes.PagesShowList];

        var start = await h.Integration.StartConnectAsync(h.Leads.Admin, null);
        var state = ExtractQueryValue(start.AuthorizationUrl, "state")!;
        var redirect = await h.Integration.CompleteCallbackAsync("code-1", state, null);

        // A login configuration missing a lead-critical permission is caught exactly as a
        // declined scope is, rather than trusted because it came from the dashboard.
        Assert.Contains("meta=connected", redirect);
        var connection = await h.Db.ExternalIntegrationConnections.SingleAsync();
        Assert.Equal(expected, connection.Status);
    }

    [Theory]
    [InlineData("1234567890123456", true)]
    [InlineData("", true)]
    [InlineData("not-a-config-id", false)]
    [InlineData("123&scope=ads_management", false)]
    public void TheLoginConfigId_IsValidatedAtStartup(string loginConfigId, bool starts)
    {
        using var factory = new MetaWebhookEndpointTests.MetaApiFactory()
            .WithWebHostBuilder(b => b.UseSetting("MetaIntegration:LoginConfigId", loginConfigId));

        var boot = Record.Exception(() => factory.Services);

        if (starts)
        {
            Assert.Null(boot);
            return;
        }

        var failure = Assert.IsType<OptionsValidationException>(boot);
        Assert.Contains(failure.Failures, f => f.Contains("MetaIntegration:LoginConfigId"));
    }

    [Fact]
    public async Task AnUnknownState_IsRejected()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();

        var redirect = await h.Integration.CompleteCallbackAsync("code-1", "never-issued", null);

        Assert.Contains("meta=error", redirect);
        Assert.Contains("reason=invalid_state", redirect);
        Assert.Equal(0, await h.Db.ExternalIntegrationConnections.CountAsync());
    }

    [Fact]
    public async Task AnExpiredState_IsRejected()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();

        var start = await h.Integration.StartConnectAsync(h.Leads.Admin, null);
        var state = ExtractQueryValue(start.AuthorizationUrl, "state")!;

        var stored = await h.Db.ExternalIntegrationOAuthStates.SingleAsync();
        stored.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await h.Db.SaveChangesAsync();

        var redirect = await h.Integration.CompleteCallbackAsync("code-1", state, null);

        Assert.Contains("reason=invalid_state", redirect);
        Assert.Equal(0, await h.Db.ExternalIntegrationConnections.CountAsync());
    }

    [Fact]
    public async Task AReusedState_IsRejectedAndCreatesNoSecondConnection()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();

        var start = await h.Integration.StartConnectAsync(h.Leads.Admin, null);
        var state = ExtractQueryValue(start.AuthorizationUrl, "state")!;

        var first = await h.Integration.CompleteCallbackAsync("code-1", state, null);
        Assert.Contains("meta=connected", first);

        var second = await h.Integration.CompleteCallbackAsync("code-1", state, null);

        Assert.Contains("reason=invalid_state", second);
        Assert.Equal(1, await h.Db.ExternalIntegrationConnections.CountAsync());
    }

    [Fact]
    public async Task DecliningConsentAtMeta_IsReportedWithoutAnError()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();

        // Meta echoes the same state back whether the admin approved or declined — a denial is
        // not a stateless callback.
        var start = await h.Integration.StartConnectAsync(h.Leads.Admin, "/crm/settings");
        var state = ExtractQueryValue(start.AuthorizationUrl, "state")!;

        var redirect = await h.Integration.CompleteCallbackAsync(null, state, "access_denied");

        Assert.Contains("reason=denied", redirect);
        Assert.Equal(0, await h.Db.ExternalIntegrationConnections.CountAsync());
    }

    [Fact]
    public async Task ADeniedState_IsConsumedAndCannotBeReplayed()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();

        var start = await h.Integration.StartConnectAsync(h.Leads.Admin, null);
        var state = ExtractQueryValue(start.AuthorizationUrl, "state")!;

        await h.Integration.CompleteCallbackAsync(null, state, "access_denied");

        // Retrying the same state — with or without the error this time — must fail the same
        // way any other reused state does, not stay usable because the first attempt was a
        // denial rather than a success.
        var replay = await h.Integration.CompleteCallbackAsync("code-1", state, null);

        Assert.Contains("reason=invalid_state", replay);
        Assert.Equal(0, await h.Db.ExternalIntegrationConnections.CountAsync());
    }

    [Fact]
    public async Task ADenialWithNoState_IsRejectedAsInvalidRatherThanReportedAsDenied()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();

        // A denial callback missing the state DAMS itself issued cannot be distinguished from
        // a forged hit on the callback URL, regardless of what "error" it claims.
        var redirect = await h.Integration.CompleteCallbackAsync(null, null, "access_denied");

        Assert.Contains("reason=invalid_state", redirect);
    }

    [Fact]
    public async Task TheCallbackRedirect_NeverCarriesACredential()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();

        var start = await h.Integration.StartConnectAsync(h.Leads.Admin, null);
        var state = ExtractQueryValue(start.AuthorizationUrl, "state")!;

        var redirect = await h.Integration.CompleteCallbackAsync("authorization-code-1", state, null);

        Assert.DoesNotContain("authorization-code-1", redirect);
        Assert.DoesNotContain("long-lived-token", redirect);
        Assert.DoesNotContain("test-app-secret", redirect);
    }

    [Fact]
    public async Task ConnectingWithMissingPermissions_ConnectsButAsksForReauthorization()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        h.Graph.Authorization.GrantedScopes = ["pages_show_list"];

        var start = await h.Integration.StartConnectAsync(h.Leads.Admin, null);
        var state = ExtractQueryValue(start.AuthorizationUrl, "state")!;

        await h.Integration.CompleteCallbackAsync("code-1", state, null);

        // Saying so now is far better than letting every lead retrieval fail silently later.
        var connection = await h.Db.ExternalIntegrationConnections.SingleAsync();
        Assert.Equal(ExternalIntegrationConnectionStatus.NeedsReauthorization, connection.Status);
        Assert.Contains("leads_retrieval", connection.LastError);
    }

    [Fact]
    public async Task TheCallbackRedirect_UsesTheConfiguredFrontendOrigin_NotTheApiHostItRanOn()
    {
        // The callback itself always executes on the API host. MetaIntegrationHarness configures
        // FrontendReturnUrl as "https://dams.test/crm/settings" precisely to model a deployment
        // where the SPA and the API do not share a host — the redirect must still land on that
        // configured origin, combined with wherever this admin actually started from, rather than
        // being replaced outright by the relative path the SPA sent as returnPath.
        await using var h = await MetaIntegrationHarness.CreateAsync();

        var start = await h.Integration.StartConnectAsync(h.Leads.Admin, "/crm/settings?tab=integrations");
        var state = ExtractQueryValue(start.AuthorizationUrl, "state")!;

        var redirect = await h.Integration.CompleteCallbackAsync("code-1", state, null);

        Assert.StartsWith("https://dams.test/crm/settings?tab=integrations", redirect);
        Assert.Contains("meta=connected", redirect);
    }

    [Fact]
    public async Task ADeniedCallback_StillReturnsToTheConfiguredFrontendOrigin()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();

        var start = await h.Integration.StartConnectAsync(h.Leads.Admin, "/crm/settings?tab=integrations");
        var state = ExtractQueryValue(start.AuthorizationUrl, "state")!;

        var redirect = await h.Integration.CompleteCallbackAsync(null, state, "access_denied");

        Assert.StartsWith("https://dams.test/crm/settings?tab=integrations", redirect);
        Assert.Contains("reason=denied", redirect);
    }

    [Fact]
    public async Task ReconnectingTheSameAccount_UpdatesTheExistingConnection()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var start = await h.Integration.StartConnectAsync(h.Leads.Admin, null);
            var state = ExtractQueryValue(start.AuthorizationUrl, "state")!;
            await h.Integration.CompleteCallbackAsync("code-1", state, null);
        }

        // A second connection row would make webhook page resolution ambiguous.
        Assert.Equal(1, await h.Db.ExternalIntegrationConnections.CountAsync());
    }

    [Fact]
    public void NoIntegrationDto_CanCarryACredential()
    {
        var dtoTypes = typeof(MetaConnectionDto).Assembly
            .GetTypes()
            .Where(t => t.Namespace == typeof(MetaConnectionDto).Namespace && t.IsClass)
            .ToList();

        Assert.NotEmpty(dtoTypes);

        string[] forbidden = ["token", "secret", "credential", "password", "appsecret"];

        foreach (var type in dtoTypes)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                // TokenExpiresAt is a timestamp, not a credential, and is genuinely useful to
                // show an admin — so the check is on the value-carrying name, not the word.
                if (property.Name == nameof(MetaConnectionDto.TokenExpiresAt))
                    continue;

                var name = property.Name.ToLowerInvariant();
                Assert.DoesNotContain(forbidden, word => name.Contains(word));
            }
        }
    }

    [Fact]
    public async Task ASerializedConnection_ContainsNeitherTheTokenNorItsProtectedForm()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        await h.ConnectPageAsync();

        var connections = await h.Integration.GetConnectionsAsync();
        var json = JsonSerializer.Serialize(connections);

        Assert.DoesNotContain("user-token", json);
        Assert.DoesNotContain("page-token", json);
        // The protected form is base64 of the plaintext in tests; neither may appear.
        Assert.DoesNotContain(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("user-token-meta-user-1")), json);
    }

    [Fact]
    public async Task Disconnecting_RemovesCredentialsButKeepsEveryLeadAndItsAttribution()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await h.ConnectPageAsync();

        h.Graph.Leads["lead-1"] = FakeMetaGraphClient.Lead(
            "lead-1", [("full_name", "Ali Khan"), ("email", "ali@example.com")], pageId: page.ExternalId);

        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "lead-1"));
        await h.Processor.ProcessPendingEventsAsync(10);
        Assert.Equal(1, await h.Db.Leads.CountAsync());

        await h.Integration.DisconnectAsync(connection.Id, h.Leads.Admin);

        var updated = await h.Db.ExternalIntegrationConnections.SingleAsync(c => c.Id == connection.Id);
        Assert.Equal(ExternalIntegrationConnectionStatus.Disconnected, updated.Status);
        Assert.Null(updated.AccessTokenProtected);
        Assert.NotNull(updated.DisconnectedAt);

        var updatedPage = await h.Db.ExternalIntegrationResources.SingleAsync(r => r.Id == page.Id);
        Assert.Null(updatedPage.ResourceTokenProtected);
        Assert.False(updatedPage.IsEnabled);

        // Losing access to Meta must never lose the CRM's own records.
        Assert.Equal(1, await h.Db.Leads.CountAsync());
        var submission = await h.Db.LeadExternalSubmissions.SingleAsync();
        Assert.Equal(connection.Id, submission.ExternalIntegrationConnectionId);
        Assert.Contains(page.ExternalId, h.Graph.UnsubscribedPages);
    }

    [Fact]
    public async Task Disconnecting_NeverUnsubscribesAPageThatAnotherConnectionStillOwns()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();

        // Two DAMS connections hold a row for the same physical Facebook Page. "owner" is live;
        // "leaving" is disabled here but still carries a stale IsSubscribed=true.
        var (_, ownerPage) = await h.ConnectPageAsync(
            pageId: "shared-page", externalAccountId: "meta-user-owner", enabled: true);
        var (leaving, leavingPage) = await h.ConnectPageAsync(
            pageId: "shared-page", externalAccountId: "meta-user-leaving", enabled: false);
        leavingPage.IsSubscribed = true;
        await h.Db.SaveChangesAsync();

        // The owner's subscription is a real, established piece of remote state, not just a
        // local flag — establishing it here is what makes "is Meta still subscribed afterwards"
        // a question the fake can actually answer.
        await h.Graph.SubscribePageAsync("shared-page", "page-token-shared-page");

        await h.Integration.DisconnectAsync(leaving.Id, h.Leads.Admin);

        // A Page's subscription is app-to-Page. Disconnecting an unrelated account must never
        // take down the connection that actually owns it.
        Assert.DoesNotContain("shared-page", h.Graph.UnsubscribedPages);
        Assert.True(h.Graph.IsCurrentlySubscribed("shared-page"));

        var stillOwned = await h.Db.ExternalIntegrationResources.SingleAsync(r => r.Id == ownerPage.Id);
        Assert.True(stillOwned.IsEnabled);
        Assert.True(stillOwned.IsSubscribed);

        var released = await h.Db.ExternalIntegrationResources.SingleAsync(r => r.Id == leavingPage.Id);
        Assert.False(released.IsEnabled);
        Assert.False(released.IsSubscribed);
    }

    [Fact]
    public async Task Disconnecting_WhenMetaRefusesTheUnsubscribe_KeepsTheEvidenceAndSaysSo()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await h.ConnectPageAsync(pageId: "page-1", enabled: true);

        h.Graph.UnsubscribeFailure = new MetaTransientException("Meta could not be reached.");

        // The disconnect itself must still succeed — often the whole reason to disconnect is
        // that Meta no longer answers for this account.
        await h.Integration.DisconnectAsync(connection.Id, h.Leads.Admin);

        var updated = await h.Db.ExternalIntegrationConnections.SingleAsync(c => c.Id == connection.Id);
        Assert.Equal(ExternalIntegrationConnectionStatus.Disconnected, updated.Status);
        Assert.Null(updated.AccessTokenProtected);

        // The credentials needed to retry are gone by design, so an admin has to be told that
        // Meta may keep delivering — and the flag must not claim a cleanup that never happened.
        var reloadedPage = await h.Db.ExternalIntegrationResources.SingleAsync(r => r.Id == page.Id);
        Assert.False(reloadedPage.IsEnabled);
        Assert.True(reloadedPage.IsSubscribed);
        Assert.NotNull(updated.LastError);
        Assert.Contains("could not be told to stop", updated.LastError);
    }

    /// <summary>
    /// A disconnect whose unsubscribe Meta refused leaves a disabled Page still flagged as
    /// subscribed. Unchecking it again (from a stale tab, or the API directly) used to try Meta
    /// with credentials the disconnect had already deleted, and that failure marked the
    /// connection NeedsReauthorization, undoing the disconnect.
    /// </summary>
    [Fact]
    public async Task DisablingAPageOnADisconnectedConnection_LeavesItDisconnectedAndNeverCallsMeta()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await h.ConnectPageAsync(pageId: "page-1", enabled: true);
        h.Graph.UnsubscribeFailure = new MetaTransientException("Meta could not be reached.");
        await h.Integration.DisconnectAsync(connection.Id, h.Leads.Admin);
        h.Graph.UnsubscribeFailure = null;
        var callsBefore = h.Graph.SubscriptionCallLog.Count;

        var result = await h.Integration.SetResourceEnabledAsync(connection.Id, page.Id, isEnabled: false);

        Assert.False(result.IsEnabled);
        Assert.Equal(callsBefore, h.Graph.SubscriptionCallLog.Count);
        h.Db.ChangeTracker.Clear();
        var reloaded = await h.Db.ExternalIntegrationConnections.SingleAsync(c => c.Id == connection.Id);
        Assert.Equal(ExternalIntegrationConnectionStatus.Disconnected, reloaded.Status);
        // Still the record of what Meta may be holding, for the sync after a reconnect.
        Assert.True((await h.Db.ExternalIntegrationResources.SingleAsync(r => r.Id == page.Id)).IsSubscribed);
    }

    [Fact]
    public async Task EnablingAPage_SubscribesItForLeadDelivery()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await h.ConnectPageAsync(enabled: false);

        var result = await h.Integration.SetResourceEnabledAsync(connection.Id, page.Id, isEnabled: true);

        Assert.True(result.IsEnabled);
        Assert.Contains(page.ExternalId, h.Graph.SubscribedPages);

        await h.Integration.SetResourceEnabledAsync(connection.Id, page.Id, isEnabled: false);
        Assert.Contains(page.ExternalId, h.Graph.UnsubscribedPages);
    }

    [Fact]
    public async Task ThePhysicalPage_CanOnlyBeEnabledThroughOneConnectionAtATime()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();

        // Two different Meta users both administer the same real Facebook Page and each
        // connects DAMS separately. Meta's webhook subscription is app-to-Page, not
        // connection-to-Page, so if both were allowed to enable it, disconnecting one would
        // silently unsubscribe leads for the other.
        var (first, firstPage) = await h.ConnectPageAsync(
            pageId: "shared-page", externalAccountId: "meta-user-a", enabled: false);
        var (second, secondPage) = await h.ConnectPageAsync(
            pageId: "shared-page", externalAccountId: "meta-user-b", enabled: false);

        await h.Integration.SetResourceEnabledAsync(first.Id, firstPage.Id, isEnabled: true);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Integration.SetResourceEnabledAsync(second.Id, secondPage.Id, isEnabled: true));
        Assert.Contains("already enabled through another", error.Message);

        var reloadedSecond = await h.Db.ExternalIntegrationResources.SingleAsync(r => r.Id == secondPage.Id);
        Assert.False(reloadedSecond.IsEnabled);
    }

    [Fact]
    public async Task AFailedEvent_IsVisibleAndCanBeRetriedByAnAdminIdempotently()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        h.Options.MaxAttempts = 1;
        var (connection, page) = await h.ConnectPageAsync();

        h.Graph.LeadFailures.Enqueue(new MetaTransientException("Meta is temporarily unavailable."));
        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "recoverable-lead"));
        await h.Processor.ProcessPendingEventsAsync(10);

        var failed = await h.Db.ExternalIntegrationEvents.SingleAsync();
        Assert.Equal(ExternalIntegrationEventStatus.Failed, failed.Status);

        var visible = await h.Integration.GetEventsAsync(connection.Id, 25);
        Assert.Contains(visible, e => e.Id == failed.Id && e.Status == ExternalIntegrationEventStatus.Failed);

        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            h.Integration.RetryEventAsync(connection.Id, failed.Id, h.Leads.Manager));

        var queued = await h.Integration.RetryEventAsync(connection.Id, failed.Id, h.Leads.Admin);
        Assert.Equal(ExternalIntegrationEventStatus.Pending, queued.Status);
        Assert.Equal(1, queued.RetryCount);
        Assert.Equal("Ayesha Admin", queued.LastRetriedByName);
        Assert.Null(queued.LastError);

        h.Graph.Leads["recoverable-lead"] = FakeMetaGraphClient.Lead(
            "recoverable-lead", [("full_name", "Ali Khan"), ("email", "ali@example.com")], pageId: page.ExternalId);
        Assert.Equal(1, await h.Processor.ProcessPendingEventsAsync(10));

        var processed = await h.Db.ExternalIntegrationEvents.SingleAsync();
        Assert.Equal(ExternalIntegrationEventStatus.Processed, processed.Status);
        Assert.Null(processed.LastError);
        Assert.Equal(1, await h.Db.Leads.CountAsync());

        var repeated = await h.Integration.RetryEventAsync(connection.Id, failed.Id, h.Leads.Admin);
        Assert.Equal(ExternalIntegrationEventStatus.Processed, repeated.Status);
        Assert.Equal(1, repeated.RetryCount);
        Assert.Equal(1, await h.Db.Leads.CountAsync());
    }

    [Fact]
    public async Task DisablingANonOwningOrAlreadyDisabledPage_NeverUnsubscribesTheActiveOwner()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (owner, ownerPage) = await h.ConnectPageAsync(
            pageId: "shared-page", externalAccountId: "meta-user-owner", enabled: false);
        var (nonOwner, nonOwnerPage) = await h.ConnectPageAsync(
            pageId: "shared-page", externalAccountId: "meta-user-non-owner", enabled: false);

        await h.Integration.SetResourceEnabledAsync(owner.Id, ownerPage.Id, isEnabled: true);
        await h.Integration.SetResourceEnabledAsync(nonOwner.Id, nonOwnerPage.Id, isEnabled: false);
        await h.Integration.SetResourceEnabledAsync(nonOwner.Id, nonOwnerPage.Id, isEnabled: false);

        Assert.DoesNotContain("shared-page", h.Graph.UnsubscribedPages);
        Assert.True(h.Graph.IsCurrentlySubscribed("shared-page"));

        var reloadedOwner = await h.Db.ExternalIntegrationResources.SingleAsync(r => r.Id == ownerPage.Id);
        Assert.True(reloadedOwner.IsEnabled);
        Assert.True(reloadedOwner.IsSubscribed);

        var reloadedNonOwner = await h.Db.ExternalIntegrationResources.SingleAsync(r => r.Id == nonOwnerPage.Id);
        Assert.False(reloadedNonOwner.IsEnabled);
        Assert.False(reloadedNonOwner.IsSubscribed);
    }

    [Fact]
    public async Task DisablingAnAlreadyDisabledPageWithStaleSubscription_CleansUpMeta()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (connection, page) = await h.ConnectPageAsync(enabled: false);
        page.IsSubscribed = true;
        await h.Db.SaveChangesAsync();

        await h.Integration.SetResourceEnabledAsync(connection.Id, page.Id, isEnabled: false);

        Assert.Contains(page.ExternalId, h.Graph.UnsubscribedPages);
        var reloaded = await h.Db.ExternalIntegrationResources.SingleAsync(r => r.Id == page.Id);
        Assert.False(reloaded.IsEnabled);
        Assert.False(reloaded.IsSubscribed);
    }

    [Fact]
    public async Task RetryingAFailedEventRequiresAConnectedConnectionAndEnabledPage()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        h.Options.MaxAttempts = 1;
        var (connection, page) = await h.ConnectPageAsync();
        h.Graph.LeadFailures.Enqueue(new MetaTransientException("Meta is temporarily unavailable."));
        await h.Intake.RecordAsync(MetaIntegrationHarness.WebhookBody(page.ExternalId, "blocked-retry"));
        await h.Processor.ProcessPendingEventsAsync(10);
        var failed = await h.Db.ExternalIntegrationEvents.SingleAsync();
        Assert.Equal(ExternalIntegrationEventStatus.Failed, failed.Status);

        h.Db.ChangeTracker.Clear();
        connection = await h.Db.ExternalIntegrationConnections.SingleAsync(c => c.Id == connection.Id);
        connection.Status = ExternalIntegrationConnectionStatus.Disconnected;
        await h.Db.SaveChangesAsync();
        var disconnected = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Integration.RetryEventAsync(connection.Id, failed.Id, h.Leads.Admin));
        Assert.Contains("Reconnect", disconnected.Message);

        h.Db.ChangeTracker.Clear();
        connection = await h.Db.ExternalIntegrationConnections.SingleAsync(c => c.Id == connection.Id);
        page = await h.Db.ExternalIntegrationResources.SingleAsync(r => r.Id == page.Id);
        connection.Status = ExternalIntegrationConnectionStatus.Connected;
        page.IsEnabled = false;
        await h.Db.SaveChangesAsync();
        var disabled = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Integration.RetryEventAsync(connection.Id, failed.Id, h.Leads.Admin));
        Assert.Contains("Enable", disabled.Message);
    }

    [Fact]
    public async Task AResourceBelongingToAnotherConnection_CannotBeToggled()
    {
        await using var h = await MetaIntegrationHarness.CreateAsync();
        var (first, _) = await h.ConnectPageAsync(pageId: "page-a", externalAccountId: "meta-user-a");
        var (_, otherPage) = await h.ConnectPageAsync(pageId: "page-b", externalAccountId: "meta-user-b");

        await Assert.ThrowsAsync<LeadNotFoundException>(
            () => h.Integration.SetResourceEnabledAsync(first.Id, otherPage.Id, isEnabled: true));
    }

    private static string? ExtractQueryValue(string url, string key) =>
        System.Web.HttpUtility.ParseQueryString(new Uri(url).Query)[key];
}
