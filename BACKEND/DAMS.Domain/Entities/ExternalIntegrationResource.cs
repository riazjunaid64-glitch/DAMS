namespace DAMS.Domain.Entities
{
    /// <summary>
    /// One asset discovered inside a connection — a Facebook Page, an Instagram account, an
    /// ad account, campaign, ad set, ad or lead form.
    ///
    /// Resources are only ever upserted. When a provider stops returning one it is marked
    /// inactive rather than deleted, so historical leads keep their attribution and a page
    /// that reappears keeps the enabled state an admin already chose for it.
    /// </summary>
    public class ExternalIntegrationResource
    {
        public int Id { get; set; }

        public int ExternalIntegrationConnectionId { get; set; }

        /// <summary>Denormalised from the connection so a webhook can resolve a page without a join.</summary>
        public string Provider { get; set; } = string.Empty;

        /// <summary>facebook_page, instagram_account, ad_account, campaign, ad_set, ad or lead_form — a string, so a new asset type needs no migration.</summary>
        public string ResourceType { get; set; } = string.Empty;

        public string ExternalId { get; set; } = string.Empty;

        /// <summary>Provider id of the owning asset — the page behind an Instagram account, the campaign behind an ad set.</summary>
        public string? ParentExternalId { get; set; }

        public string? Name { get; set; }

        /// <summary>The provider's own status string, kept verbatim for display.</summary>
        public string? ExternalStatus { get; set; }

        /// <summary>
        /// Off until an admin turns it on. Enabling a page is also what subscribes it for
        /// webhooks, so nothing is ingested from an asset nobody chose.
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>False once the provider stops returning this asset.</summary>
        public bool IsActive { get; set; } = true;

        /// <summary>True while the provider is delivering webhooks for this page.</summary>
        public bool IsSubscribed { get; set; }

        public string? MetadataJson { get; set; }

        /// <summary>Page-scoped access token, protected at rest. Only set where the provider requires one.</summary>
        public string? ResourceTokenProtected { get; set; }

        public DateTime? ResourceTokenExpiresAt { get; set; }

        public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;

        public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

        public DateTime? LastSyncedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public ExternalIntegrationConnection Connection { get; set; } = null!;
    }
}
