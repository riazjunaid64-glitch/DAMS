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

        public string GraphApiVersion { get; set; } = "v21.0";

        public int OAuthStateLifetimeMinutes { get; set; } = 10;

        /// <summary>Seconds between event sweeps. Zero or less disables the worker entirely.</summary>
        public int EventIntervalSeconds { get; set; } = 15;

        public int EventBatchSize { get; set; } = 25;

        /// <summary>Six hours: often enough that a new form appears the same day, rare enough to be invisible in rate limits.</summary>
        public int ResourceSyncIntervalSeconds { get; set; } = 21600;

        public int LeaseMinutes { get; set; } = 5;

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

        /// <summary>True only when every credential needed to talk to Meta is present.</summary>
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(AppId)
            && !string.IsNullOrWhiteSpace(AppSecret)
            && !string.IsNullOrWhiteSpace(WebhookVerifyToken)
            && !string.IsNullOrWhiteSpace(OAuthCallbackUrl);
    }
}
