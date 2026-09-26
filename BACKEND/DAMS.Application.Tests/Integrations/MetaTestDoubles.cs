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

    /// <summary>Set by a test to simulate a page walk stopped by MaxGraphPages before it finished.</summary>
    public bool PagesTruncated { get; set; }

    /// <summary>Set to false by a test to simulate a Page listing that fell back to no Instagram field expansion.</summary>
    public bool PagesIncludeInstagramAccounts { get; set; } = true;
    public bool AdAccountsTruncated { get; set; }
    public bool AdAccountChildrenTruncated { get; set; }

    /// <summary>Thrown by the next ad-account discovery call, then discarded.</summary>
    public Exception? AdAccountDiscoveryFailure { get; set; }

    /// <summary>Leads this fake knows about, keyed by leadgen id.</summary>
    public Dictionary<string, MetaLead> Leads { get; } = [];

    /// <summary>Thrown by the next GetLeadAsync call, then discarded. Lets one test drive one failure.</summary>
    public Queue<Exception> LeadFailures { get; } = new();

    public Exception? DiscoveryFailure { get; set; }

    /// <summary>Run synchronously at the very start of GetPagesAsync, before DiscoveryFailure is
    /// thrown — lets a test deterministically do something (e.g. cancel a token) at the exact
    /// moment a sync is "in flight", rather than racing real concurrency.</summary>
    public Action? OnGetPages { get; set; }

    /// <summary>The same hook one stage later: SubscribePageAsync is only reached once discovery
    /// has already been applied, which is where a sync holds staged, unsaved work.</summary>
    public Action? OnSubscribePage { get; set; }

    /// <summary>Thrown by every UnsubscribePageAsync call while set — Meta refusing the remote
    /// cleanup that a disconnect cannot retry once it has cleared its own credentials.</summary>
    public Exception? UnsubscribeFailure { get; set; }

    public List<string> SubscribedPages { get; } = [];
    public List<string> UnsubscribedPages { get; } = [];
    public List<(string LeadgenId, string Token)> LeadRequests { get; } = [];

    /// <summary>
    /// Every subscribe/unsubscribe call in the order it actually happened, across every
    /// DAMS connection that shares this fake — SubscribedPages/UnsubscribedPages alone can't
    /// answer "what does Meta think right now", only "did this get called at some point".
    /// A physical Page's subscription is one shared piece of remote state; the last call
    /// against a given page id is what Meta would actually be left holding.
    /// </summary>
    public List<(string PageExternalId, bool Subscribed)> SubscriptionCallLog { get; } = [];

    /// <summary>What Meta would currently report for this page, per SubscriptionCallLog.</summary>
    public bool IsCurrentlySubscribed(string pageExternalId) =>
        SubscriptionCallLog.LastOrDefault(c => c.PageExternalId == pageExternalId) is { PageExternalId: not null } call
        && call.Subscribed;

    public Task<MetaAuthorizationResult> CompleteAuthorizationAsync(string code, CancellationToken cancellationToken = default) =>
        Task.FromResult(Authorization);

    public Task<MetaDiscoveryPage> GetPagesAsync(string userAccessToken, CancellationToken cancellationToken = default)
    {
        OnGetPages?.Invoke();

        if (DiscoveryFailure is not null)
            throw DiscoveryFailure;

        return Task.FromResult(new MetaDiscoveryPage
        {
            Items = Pages.ToList(),
            Truncated = PagesTruncated,
            IncludesInstagramAccounts = PagesIncludeInstagramAccounts
        });
    }

    public Task<MetaDiscoveryPage> GetAdAccountsAsync(string userAccessToken, CancellationToken cancellationToken = default)
    {
        if (AdAccountDiscoveryFailure is not null)
            throw AdAccountDiscoveryFailure;

        return Task.FromResult(new MetaDiscoveryPage { Items = AdAccounts.ToList(), Truncated = AdAccountsTruncated });
    }

    public Task<MetaDiscoveryPage> GetAdAccountChildrenAsync(
        string adAccountExternalId, string userAccessToken, CancellationToken cancellationToken = default) =>
        Task.FromResult(new MetaDiscoveryPage { Items = AdAccountChildren.ToList(), Truncated = AdAccountChildrenTruncated });

    /// <summary>Thrown by every lead-form read while set.</summary>
    public Exception? LeadFormsFailure { get; set; }

    public Task<MetaDiscoveryPage> GetLeadFormsAsync(
        string pageExternalId, string pageAccessToken, CancellationToken cancellationToken = default)
    {
        if (LeadFormsFailure is not null)
            throw LeadFormsFailure;

        return Task.FromResult(new MetaDiscoveryPage
        {
            Items = LeadForms.Where(f => f.ParentExternalId == pageExternalId).ToList()
        });
    }

    public Task<MetaLead> GetLeadAsync(string leadgenId, string accessToken, CancellationToken cancellationToken = default)
    {
        LeadRequests.Add((leadgenId, accessToken));

        if (LeadFailures.Count > 0)
            throw LeadFailures.Dequeue();

        return Leads.TryGetValue(leadgenId, out var lead)
            ? Task.FromResult(lead)
            : throw new MetaPermanentException($"Lead {leadgenId} does not exist.");
    }

    /// <summary>Ad names Meta would share for a lead, keyed by leadgen id. Absent means none.</summary>
    public Dictionary<string, MetaLeadAdNames> AdNames { get; } = [];

    /// <summary>Thrown by the next GetLeadAdNamesAsync call, then discarded.</summary>
    public Queue<Exception> AdNameFailures { get; } = new();

    public List<string> AdNameRequests { get; } = [];

    public Task<MetaLeadAdNames> GetLeadAdNamesAsync(string leadgenId, string accessToken, CancellationToken cancellationToken = default)
    {
        AdNameRequests.Add(leadgenId);

        if (AdNameFailures.Count > 0)
            throw AdNameFailures.Dequeue();

        return Task.FromResult(AdNames.GetValueOrDefault(leadgenId) ?? new MetaLeadAdNames(null, null, null));
    }

    public Task SubscribePageAsync(string pageExternalId, string pageAccessToken, CancellationToken cancellationToken = default)
    {
        OnSubscribePage?.Invoke();

        SubscribedPages.Add(pageExternalId);
        SubscriptionCallLog.Add((pageExternalId, true));
        return Task.CompletedTask;
    }

    public Task UnsubscribePageAsync(string pageExternalId, string pageAccessToken, CancellationToken cancellationToken = default)
    {
        if (UnsubscribeFailure is not null)
            throw UnsubscribeFailure;

        UnsubscribedPages.Add(pageExternalId);
        SubscriptionCallLog.Add((pageExternalId, false));
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
