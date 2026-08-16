namespace DAMS.Domain.Entities
{
    /// <summary>
    /// Immutable receipt for a provider submission. Multiple external submissions may enrich
    /// the same lead, but each provider/id pair may be processed only once.
    ///
    /// One person enquiring twice through Meta produces one Lead and two of these rows: the
    /// lead carries the current picture, these carry what each enquiry actually said, verbatim
    /// and forever. Nothing here is ever rewritten once stored.
    /// </summary>
    public class LeadExternalSubmission
    {
        public int Id { get; set; }

        public int LeadId { get; set; }

        public string Provider { get; set; } = string.Empty;

        public string ExternalLeadId { get; set; } = string.Empty;

        public string? ExternalFormReference { get; set; }

        public DateTime? ExternalSubmittedAt { get; set; }

        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

        // ── Provider attribution (nullable: only integration-sourced submissions carry it) ──

        /// <summary>The connection this arrived through. Kept even after that connection is disconnected.</summary>
        public int? ExternalIntegrationConnectionId { get; set; }

        /// <summary>"facebook" or "instagram" when the provider states it reliably; otherwise null.</summary>
        public string? Platform { get; set; }

        public string? PageExternalId { get; set; }

        public string? PageName { get; set; }

        public string? AdAccountExternalId { get; set; }

        public string? CampaignExternalId { get; set; }

        public string? CampaignName { get; set; }

        public string? AdSetExternalId { get; set; }

        public string? AdSetName { get; set; }

        public string? AdExternalId { get; set; }

        public string? AdName { get; set; }

        public string? ExternalFormName { get; set; }

        /// <summary>The provider's complete response, kept for troubleshooting. Never returned over the API.</summary>
        public string? RawPayloadJson { get; set; }

        /// <summary>
        /// Every answer the person gave, recognised or not. This is what makes a custom form
        /// question survive without DAMS growing a column for it.
        /// </summary>
        public string? FieldDataJson { get; set; }

        public Lead Lead { get; set; } = null!;

        public ExternalIntegrationConnection? Connection { get; set; }
    }
}
