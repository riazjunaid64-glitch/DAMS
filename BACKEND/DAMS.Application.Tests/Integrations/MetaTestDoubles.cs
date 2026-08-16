using System.Text.Json;
using DAMS.Application.Interfaces;
using DAMS.Application.Services.Integrations;

namespace DAMS.Application.Tests.Integrations;

/// <summary>
/// Stands in for the Meta Graph API so the whole integration can be tested without a Meta
/// account, a network, or credentials.
///
/// Scripted rather than recorded: each test says what Meta should return or throw, which
/// makes failure paths — a 429, a revoked token — as easy to exercise as the happy one.
/// </summary>
internal sealed class FakeMetaGraphClient : IMetaGraphClient
{
    public MetaAuthorizationResult Authorization { get; set; } = new()
    {
        AccessToken = "long-lived-token",
        UserId = "meta-user-1",
        DisplayName = "Acme Marketing",
        GrantedScopes = [.. DAMS.Application.Common.MetaScopes.All]
    };

    public List<MetaDiscoveredResource> Pages { get; set; } = [];
    public List<MetaDiscoveredResource> AdAccounts { get; set; } = [];
    public List<MetaDiscoveredResource> AdAccountChildren { get; set; } = [];
    public List<MetaDiscoveredResource> LeadForms { get; set; } = [];

    /// <summary>Leads this fake knows about, keyed by leadgen id.</summary>
    public Dictionary<string, MetaLead> Leads { get; } = [];

    /// <summary>Thrown by the next GetLeadAsync call, then discarded. Lets one test drive one failure.</summary>
    public Queue<Exception> LeadFailures { get; } = new();

    public Exception? DiscoveryFailure { get; set; }

    public List<string> SubscribedPages { get; } = [];
    public List<string> UnsubscribedPages { get; } = [];
    public List<(string LeadgenId, string Token)> LeadRequests { get; } = [];

    public Task<MetaAuthorizationResult> CompleteAuthorizationAsync(string code, CancellationToken cancellationToken = default) =>
        Task.FromResult(Authorization);

    public Task<List<MetaDiscoveredResource>> GetPagesAsync(string userAccessToken, CancellationToken cancellationToken = default)
    {
        if (DiscoveryFailure is not null)
            throw DiscoveryFailure;

        return Task.FromResult(Pages.ToList());
    }

    public Task<List<MetaDiscoveredResource>> GetAdAccountsAsync(string userAccessToken, CancellationToken cancellationToken = default) =>
        Task.FromResult(AdAccounts.ToList());

    public Task<List<MetaDiscoveredResource>> GetAdAccountChildrenAsync(
        string adAccountExternalId, string userAccessToken, CancellationToken cancellationToken = default) =>
        Task.FromResult(AdAccountChildren.ToList());

    public Task<List<MetaDiscoveredResource>> GetLeadFormsAsync(
        string pageExternalId, string pageAccessToken, CancellationToken cancellationToken = default) =>
        Task.FromResult(LeadForms.Where(f => f.ParentExternalId == pageExternalId).ToList());

    public Task<MetaLead> GetLeadAsync(string leadgenId, string accessToken, CancellationToken cancellationToken = default)
    {
        LeadRequests.Add((leadgenId, accessToken));

        if (LeadFailures.Count > 0)
            throw LeadFailures.Dequeue();

        return Leads.TryGetValue(leadgenId, out var lead)
            ? Task.FromResult(lead)
            : throw new MetaPermanentException($"Lead {leadgenId} does not exist.");
    }

    public Task SubscribePageAsync(string pageExternalId, string pageAccessToken, CancellationToken cancellationToken = default)
    {
        SubscribedPages.Add(pageExternalId);
        return Task.CompletedTask;
    }

    public Task UnsubscribePageAsync(string pageExternalId, string pageAccessToken, CancellationToken cancellationToken = default)
    {
        UnsubscribedPages.Add(pageExternalId);
        return Task.CompletedTask;
    }

    /// <summary>Builds a lead whose raw JSON matches its fields, as a real response would.</summary>
    public static MetaLead Lead(
        string leadgenId,
        (string Name, string? Value)[] fields,
        string? platform = null,
        string? pageId = null,
        string? campaignName = null,
        string? adName = null,
        string? formId = null)
    {
        var lead = new MetaLead
        {
            LeadgenId = leadgenId,
            PageId = pageId,
            Platform = platform,
            CampaignName = campaignName,
            CampaignId = campaignName is null ? null : "camp-1",
            AdName = adName,
            AdId = adName is null ? null : "ad-1",
            AdSetName = adName is null ? null : "Set A",
            AdSetId = adName is null ? null : "adset-1",
            FormId = formId,
            CreatedTime = new DateTime(2026, 8, 16, 9, 0, 0, DateTimeKind.Utc),
            FieldData = fields.Select(f => new MetaFieldAnswer(f.Name, f.Value)).ToList()
        };

        lead.RawJson = JsonSerializer.Serialize(new
        {
            id = leadgenId,
            platform,
            field_data = fields.Select(f => new { name = f.Name, values = new[] { f.Value } })
        });

        return lead;
    }
}

/// <summary>
/// Round-trips secrets through base64 instead of Data Protection, so Application-layer
/// tests need no ASP.NET host. It is deliberately not encryption — these tests care that
/// values survive the round trip, not how they are protected.
/// </summary>
internal sealed class PlaintextSecretProtector : IIntegrationSecretProtector
{
    public string Protect(string plaintext) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plaintext));

    public string? TryUnprotect(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
            return null;

        try
        {
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>Simulates a lost or rotated key ring: everything protected becomes unreadable.</summary>
internal sealed class UnreadableSecretProtector : IIntegrationSecretProtector
{
    public string Protect(string plaintext) => "unreadable";

    public string? TryUnprotect(string? protectedValue) => null;
}
