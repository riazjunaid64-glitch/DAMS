namespace DAMS.Application.Common
{
    /// <summary>
    /// Mechanics for the Meta Lead Ads integration, bound from the "MetaIntegration"
    /// configuration section.
    ///
    /// The four credentials are deliberately absent from any committed appsettings file and
    /// must come from user-secrets or environment variables (MetaIntegration__AppSecret and
    /// friends), the same posture as Jwt:Key. When they are missing the integration is simply
    /// off: the app still boots, the endpoints refuse politely, and the worker skips. There
    /// are no built-in or default credentials.
    /// </summary>
    public sealed class MetaIntegrationOptions
    {
        public const string SectionName = "MetaIntegration";

        public string? AppId { get; set; }

        public string? AppSecret { get; set; }

        /// <summary>Shared with Meta when the webhook subscription is created; echoed back on verification.</summary>
        public string? WebhookVerifyToken { get; set; }

        /// <summary>Absolute HTTPS URL Meta redirects the browser back to. Must match the app's allow-list exactly.</summary>
        public string? OAuthCallbackUrl { get; set; }

        /// <summary>CRM screen the admin is returned to once the callback completes.</summary>
        public string? FrontendReturnUrl { get; set; }

        /// <summary>
        /// Optional Facebook Login for Business configuration ID from the App Dashboard. Set it
        /// when the Meta app is a Business app: the connect dialog then sends config_id instead
        /// of scope. Unset, the classic scope-based dialog is used unchanged. The configuration
        /// must grant every MetaScopes.LeadCritical permission and issue a User access token.
        /// </summary>
        public string? LoginConfigId { get; set; }

        public string GraphApiVersion { get; set; } = "v21.0";

        public int OAuthStateLifetimeMinutes { get; set; } = 10;

        /// <summary>Seconds between event sweeps. Zero or less disables the worker entirely.</summary>
        public int EventIntervalSeconds { get; set; } = 15;

        public int EventBatchSize { get; set; } = 25;

        /// <summary>
        /// Seconds the worker waits after boot before its first database check, and the first
        /// wait (at least one second) before re-checking when the database could not be reached.
        /// Each further failed check doubles the wait, up to <see cref="StartupRetryMaxDelaySeconds"/>.
        /// </summary>
        public int StartupDelaySeconds { get; set; } = 15;

        public int StartupRetryMaxDelaySeconds { get; set; } = 300;

        /// <summary>Six hours: often enough that a new form appears the same day, rare enough to be invisible in rate limits.</summary>
        public int ResourceSyncIntervalSeconds { get; set; } = 21600;

        /// <summary>
        /// How long a worker holds the events it claims. It also caps the batch: every event
        /// claimed must be able to spend two Graph calls at RequestTimeoutSeconds each, twice
        /// over, inside it (see MetaLeadEventProcessor.ClaimEventsAsync). Ten minutes keeps five
        /// events per sweep at the default 30-second timeout.
        /// </summary>
        public int LeaseMinutes { get; set; } = 10;

        public int MaxAttempts { get; set; } = 6;

        public int BaseRetryDelaySeconds { get; set; } = 60;

        public int MaxRetryDelayMinutes { get; set; } = 60;

        /// <summary>
        /// How long to park events when the authorization has failed. Long, because nothing
        /// will succeed until a human reconnects — but not infinite, so a fixed connection
        /// drains its backlog on its own.
        /// </summary>
        public int AuthRetryDelayHours { get; set; } = 6;

        public int RequestTimeoutSeconds { get; set; } = 30;

        /// <summary>Webhook bodies larger than this are refused unread.</summary>
        public int MaxWebhookBodyBytes { get; set; } = 524288;

        /// <summary>Hard stop on cursor following, so a pagination bug cannot spin forever.</summary>
        public int MaxGraphPages { get; set; } = 20;

        /// <summary>
        /// How long a background worker holds a connection's sync lease. A full sync makes many
        /// sequential Graph calls (pages, per-page forms, ad accounts, per-account campaigns/ad
        /// sets/ads), each individually bounded by RequestTimeoutSeconds; this needs enough
        /// headroom for all of them together, not just one. Released as soon as the sync
        /// finishes — success or failure — so this is a ceiling for a wedged worker, not the
        /// normal wait time for a retry.
        /// </summary>
        public int SyncLeaseMinutes { get; set; } = 15;

        /// <summary>
        /// Days to keep a finished (Processed/Ignored/Failed) event before it is pruned. Zero
        /// (the default) keeps every event forever. This is a data-retention policy question —
        /// raw lead field answers pass through these rows — so it is left off until the business
        /// decides a period, rather than this integration picking one silently.
        /// </summary>
        public int EventRetentionDays { get; set; } = 0;

        /// <summary>
        /// How many days before a connection's own access token expires every Admin is told to
        /// reconnect it. Meta's long-lived user token lasts about 60 days. Zero switches the
        /// warning off.
        /// </summary>
        public int TokenExpiryWarningDays { get; set; } = 7;

        /// <summary>
        /// Tell every Admin when an enabled Page that has delivered leads before has sent none for
        /// this many days — the sign of a webhook subscription Meta dropped. Zero (the default)
        /// switches it off: a Page with no campaign running is quiet for good reason, so how long
        /// is too long is the business's call.
        /// </summary>
        public int QuietPageAlertDays { get; set; } = 0;

        /// <summary>True only when every credential needed to talk to Meta is present.</summary>
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(AppId)
            && !string.IsNullOrWhiteSpace(AppSecret)
            && !string.IsNullOrWhiteSpace(WebhookVerifyToken)
            && !string.IsNullOrWhiteSpace(OAuthCallbackUrl);
    }
}
