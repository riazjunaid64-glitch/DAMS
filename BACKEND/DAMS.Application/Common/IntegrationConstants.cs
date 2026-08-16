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
    /// business_management: neither is required for discovering pages, reading form metadata,
    /// subscribing a page to leadgen webhooks, or retrieving a lead. If a future Graph version
    /// genuinely requires more for one of those exact operations, document which endpoint and
    /// why before adding it.
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

        public static readonly string[] All =
        [
            PagesShowList,
            PagesReadEngagement,
            PagesManageMetadata,
            LeadsRetrieval,
            AdsRead
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
        /// </summary>
        public static readonly string[] Optional = [AdsRead];

        public static string Joined => string.Join(',', All);
    }
}
