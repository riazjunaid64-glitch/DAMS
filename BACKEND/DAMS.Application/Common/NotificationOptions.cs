namespace DAMS.Application.Common
{
    /// <summary>
    /// Operational knobs for the delivery workers, bound from the "Notifications"
    /// configuration section. Business wording and channel choices are admin settings in the
    /// database; only the mechanics live here.
    /// </summary>
    public sealed class NotificationOptions
    {
        public const string SectionName = "Notifications";

        /// <summary>Seconds between delivery sweeps. Zero or less disables the worker.</summary>
        public int DeliveryIntervalSeconds { get; set; } = 20;

        /// <summary>Seconds between scheduled-job sweeps. Zero or less disables the worker.</summary>
        public int ScheduleIntervalSeconds { get; set; } = 30;

        /// <summary>Deliveries claimed per sweep.</summary>
        public int DeliveryBatchSize { get; set; } = 50;

        /// <summary>Jobs claimed per sweep.</summary>
        public int JobBatchSize { get; set; } = 5;

        /// <summary>How long a claimed row stays leased to one worker. A worker that dies
        /// mid-send releases its rows automatically once this passes.</summary>
        public int LeaseMinutes { get; set; } = 5;

        /// <summary>Attempts before a delivery is written off as permanently failed.</summary>
        public int MaxAttempts { get; set; } = 5;

        /// <summary>First retry delay; each further attempt doubles it, capped by
        /// <see cref="MaxRetryDelayMinutes"/>.</summary>
        public int BaseRetryDelaySeconds { get; set; } = 60;

        public int MaxRetryDelayMinutes { get; set; } = 60;

        /// <summary>Recipients one admin broadcast may reach. A larger audience is refused
        /// rather than silently truncated.</summary>
        public int MaxBroadcastRecipients { get; set; } = 5000;

        /// <summary>An audience above this needs an explicit confirmation flag from the
        /// composer before the job is created.</summary>
        public int LargeAudienceThreshold { get; set; } = 100;

        /// <summary>Days after which read notifications are pruned from the inbox. Zero keeps
        /// them for ever.</summary>
        public int InboxRetentionDays { get; set; } = 180;

        /// <summary>Consecutive push failures before a subscription is deactivated.</summary>
        public int PushFailureThreshold { get; set; } = 5;

        /// <summary>Maximum active browser subscriptions one account may own.</summary>
        public int MaxPushSubscriptionsPerUser { get; set; } = 10;

        /// <summary>
        /// Exact hosts or suffixes (prefixed with a dot) accepted as browser push services.
        /// Keeping this allow-list server-side prevents a subscription from becoming SSRF.
        /// </summary>
        public string[] AllowedPushEndpointHosts { get; set; } =
        {
            "fcm.googleapis.com",
            ".googleapis.com",
            "updates.push.services.mozilla.com",
            ".push.services.mozilla.com",
            "web.push.apple.com",
            ".push.apple.com",
            ".notify.windows.com"
        };

        /// <summary>Seconds a live inbox stream waits between heartbeats.</summary>
        public int StreamHeartbeatSeconds { get; set; } = 25;
    }
}
