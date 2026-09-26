namespace DAMS.Application.Common
{
    /// <summary>Technical provider keys. Stored as strings so a new provider needs no migration.</summary>
    public static class IntegrationProviders
    {
        public const string Meta = "meta";
    }

    /// <summary>
    /// Business lead-source codes an integration may resolve to. These are seeded
    /// <c>LeadSource.Code</c> values, distinct from the technical provider above: one Meta
    /// connection produces Facebook leads, Instagram leads, or — when the platform cannot be
    /// established — plain Meta ones.
    /// </summary>
    public static class IntegrationSourceCodes
    {
        public const string Facebook = "facebook";
        public const string Instagram = "instagram";

        /// <summary>
        /// The honest answer when Meta does not tell us which surface the lead came from.
        /// Defaulting to Facebook instead would quietly corrupt channel reporting, because a
        /// lead-ad webhook is always delivered through a Page whether or not the ad ran there.
        /// </summary>
        public const string Meta = "meta";

        /// <summary>
        /// Every source a Meta lead can resolve to. They stay usable by integration intake even
        /// when inactive (LeadService.ResolveSourceAsync), and cannot be switched off while Meta
        /// is connected (LeadConfigurationService.UpdateSourceAsync).
        /// </summary>
        public static readonly string[] All = [Facebook, Instagram, Meta];
    }

    /// <summary>Asset kinds discovered inside a connection.</summary>
    public static class ExternalResourceTypes
    {
        public const string FacebookPage = "facebook_page";
        public const string InstagramAccount = "instagram_account";
        public const string AdAccount = "ad_account";
        public const string Campaign = "campaign";
        public const string AdSet = "ad_set";
        public const string Ad = "ad";
        public const string LeadForm = "lead_form";
    }

    /// <summary>
    /// The complete set of Meta permissions DAMS asks for, in one place so nobody can widen
    /// access by editing a URL somewhere.
    ///
    /// This scope list is read-only with respect to advertising. Do NOT add ads_management or
    /// business_management without evidence: DAMS believes neither is needed for discovering
    /// pages, reading form metadata, subscribing a page to leadgen webhooks or retrieving a
    /// lead, but that is unverified until the KAN-12 live test (see below).
    ///
    /// What each permission is for:
    /// <list type="bullet">
    /// <item><c>pages_show_list</c> — <c>GET me/accounts</c> (Page discovery).</item>
    /// <item><c>pages_read_engagement</c> — Page fields read during discovery.</item>
    /// <item><c>pages_manage_metadata</c> — <c>POST/DELETE {page-id}/subscribed_apps</c>
    /// (leadgen webhook subscription).</item>
    /// <item><c>leads_retrieval</c> — <c>GET {leadgen-id}</c> and the Page's lead forms.</item>
    /// <item><c>ads_read</c> — optional; ad account, campaign, ad set and ad discovery.</item>
    /// <item><c>instagram_basic</c> — optional; reading the linked Instagram account.</item>
    /// </list>
    ///
    /// Meta's lead-ads guides also mention <c>pages_manage_ads</c> (lead retrieval) and
    /// <c>ads_management</c> (<c>subscribed_apps</c>). Neither is requested: whether Meta enforces
    /// them for this flow is decided by the live Floria Heights Instant Form test (KAN-12), not
    /// by the docs alone. Until that test has passed, this list is not verified against the
    /// production app. If it fails with a permission error, add the scope proven necessary to
    /// <see cref="All"/> and <see cref="LeadCritical"/>, record here the endpoint and Meta error
    /// that proved it together with the test date, and update MetaIntegrationSecurityTests.
    /// Existing connections then simply reconnect; the OAuth dialog is opened with
    /// <c>auth_type=rerequest</c>, so Meta asks again for anything previously declined.
    /// </summary>
    public static class MetaScopes
    {
        /// <summary>List the pages the person administers.</summary>
        public const string PagesShowList = "pages_show_list";

        /// <summary>Read page metadata and its connected Instagram account.</summary>
        public const string PagesReadEngagement = "pages_read_engagement";

        /// <summary>Subscribe and unsubscribe the app to a page's leadgen webhook.</summary>
        public const string PagesManageMetadata = "pages_manage_metadata";

        /// <summary>Retrieve the actual lead behind a leadgen_id.</summary>
        public const string LeadsRetrieval = "leads_retrieval";

        /// <summary>Read-only discovery of ad accounts, campaigns, ad sets and ads.</summary>
        public const string AdsRead = "ads_read";

        /// <summary>
        /// Required by Meta to read the instagram_business_account field expansion this
        /// integration requests while discovering Pages, so a Page's linked Instagram account
        /// can be attributed correctly instead of lumped in as a plain Facebook lead.
        /// </summary>
        public const string InstagramBasic = "instagram_basic";

        public static readonly string[] All =
        [
            PagesShowList,
            PagesReadEngagement,
            PagesManageMetadata,
            LeadsRetrieval,
            AdsRead,
            InstagramBasic
        ];

        /// <summary>
        /// Without every one of these, lead capture itself cannot work, so their absence is
        /// what actually earns <c>NeedsReauthorization</c>.
        /// </summary>
        public static readonly string[] LeadCritical =
        [
            PagesShowList,
            PagesReadEngagement,
            PagesManageMetadata,
            LeadsRetrieval
        ];

        /// <summary>
        /// <c>ads_read</c> requires Advanced Access (App Review) on Meta's side and is
        /// frequently absent on a freshly connected, not-yet-reviewed app. Missing it degrades
        /// only campaign/ad-set/ad discovery — it must never stop a Page from delivering leads.
        /// <c>instagram_basic</c> is the same kind of degradable permission: without it,
        /// Instagram accounts are simply not discovered and their leads fall back to the
        /// generic "meta" source rather than being mislabelled — Page discovery, and therefore
        /// lead delivery, still works.
        /// </summary>
        public static readonly string[] Optional = [AdsRead, InstagramBasic];

        public static string Joined => string.Join(',', All);
    }
}
