using System.Reflection;
using System.Text.Json;
using DAMS.Application.Common;
using DAMS.Application.DTOs.IntegrationDtos;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
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

        var redirect = await h.Integration.CompleteCallbackAsync(null, null, "access_denied");

        Assert.Contains("reason=denied", redirect);
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
