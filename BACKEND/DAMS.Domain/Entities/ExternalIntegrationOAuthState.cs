namespace DAMS.Domain.Entities
{
    /// <summary>
    /// A single-use, short-lived OAuth state issued when an admin starts connecting a provider
    /// and consumed when the provider redirects back.
    ///
    /// Only the hash of the state is stored. A stolen database row therefore cannot be replayed
    /// against the callback, and the callback — which must be anonymous, because the provider
    /// redirects a browser carrying no session — learns which admin acted from this row rather
    /// than from the untrusted request.
    /// </summary>
    public class ExternalIntegrationOAuthState
    {
        public int Id { get; set; }

        public string Provider { get; set; } = string.Empty;

        /// <summary>Base64 SHA-256 of the raw state value. The raw value is never persisted.</summary>
        public string StateHash { get; set; } = string.Empty;

        public int CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime ExpiresAt { get; set; }

        /// <summary>Set the moment the state is redeemed; a second attempt must fail.</summary>
        public DateTime? ConsumedAt { get; set; }

        /// <summary>Relative CRM path to return the admin to. Never an absolute URL — that would be an open redirect.</summary>
        public string? ReturnPath { get; set; }

        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    }
}
