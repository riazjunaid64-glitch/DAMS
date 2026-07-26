namespace DAMS.Application.Common
{
    /// <summary>
    /// Timings for the follow-up, inactivity and escalation alerts. Bound from the
    /// "LeadAlerts" configuration section so the business can tune its own service levels
    /// without a code change.
    /// </summary>
    public sealed class LeadAlertOptions
    {
        public const string SectionName = "LeadAlerts";

        /// <summary>Hours an assigned lead may go without first contact before escalating.</summary>
        public int FirstResponseHours { get; set; } = 4;

        /// <summary>How far ahead a due follow-up is announced to its owner.</summary>
        public int FollowUpDueWindowHours { get; set; } = 4;

        /// <summary>Grace after the due time before a follow-up counts as overdue.</summary>
        public int FollowUpOverdueGraceHours { get; set; } = 1;

        /// <summary>How long an untouched follow-up waits before it is recorded as missed.</summary>
        public int FollowUpMissedAfterHours { get; set; } = 24;

        /// <summary>Days of silence on an open lead before the owner and manager are told.</summary>
        public int InactivityDays { get; set; } = 7;

        /// <summary>How long after its slot a site visit is recorded as missed.</summary>
        public int SiteVisitMissedAfterHours { get; set; } = 6;

        /// <summary>Minutes between background scans. Zero or less disables the scanner.</summary>
        public int ScanIntervalMinutes { get; set; } = 15;

        /// <summary>Upper bound on rows handled per category per scan.</summary>
        public int MaxRowsPerScan { get; set; } = 500;
    }
}
