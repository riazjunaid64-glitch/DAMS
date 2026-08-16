using DAMS.Application.Services.Integrations;

namespace DAMS.Application.Interfaces
{
    /// <summary>
    /// The only component in DAMS that knows a Meta Graph URL. Everything else asks in domain
    /// terms, which keeps endpoint and version churn to one file and makes the whole
    /// integration testable without a Meta account.
    ///
    /// Every method throws exactly one of MetaTransientException, MetaAuthorizationException
    /// or MetaPermanentException; nothing else escapes.
    /// </summary>
    public interface IMetaGraphClient
    {
        /// <summary>Exchanges an authorization code for a long-lived token and identifies who granted it.</summary>
        Task<MetaAuthorizationResult> CompleteAuthorizationAsync(string code, CancellationToken cancellationToken = default);

        /// <summary>Pages the user administers, each with its own page token and connected Instagram account.</summary>
        Task<MetaDiscoveryPage> GetPagesAsync(string userAccessToken, CancellationToken cancellationToken = default);

        Task<MetaDiscoveryPage> GetAdAccountsAsync(string userAccessToken, CancellationToken cancellationToken = default);

        /// <summary>Campaigns, ad sets and ads beneath one ad account, in a single flattened list.</summary>
        Task<MetaDiscoveryPage> GetAdAccountChildrenAsync(
            string adAccountExternalId, string userAccessToken, CancellationToken cancellationToken = default);

        Task<MetaDiscoveryPage> GetLeadFormsAsync(
            string pageExternalId, string pageAccessToken, CancellationToken cancellationToken = default);

        /// <summary>Retrieves the actual lead behind a leadgen id, preserving the full response.</summary>
        Task<MetaLead> GetLeadAsync(string leadgenId, string accessToken, CancellationToken cancellationToken = default);

        /// <summary>Starts or stops leadgen webhook delivery for a page.</summary>
        Task SubscribePageAsync(string pageExternalId, string pageAccessToken, CancellationToken cancellationToken = default);

        Task UnsubscribePageAsync(string pageExternalId, string pageAccessToken, CancellationToken cancellationToken = default);
    }
}
