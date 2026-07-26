namespace DAMS.Domain.Entities
{
    /// <summary>
    /// One browser on one device for one user. The endpoint is the browser's own push
    /// service URL and the keys are that browser's; both are secrets shared between DAMS and
    /// that browser only, and neither is ever returned to any client.
    /// </summary>
    public class PushSubscription
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        /// <summary>The push service URL. Unique across DAMS: a shared computer that logs in
        /// as somebody else re-registers the same endpoint, which moves it to the new user
        /// rather than leaving the previous user's messages arriving on that machine.</summary>
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>Client public key (base64url, uncompressed P-256 point).</summary>
        public string P256dh { get; set; } = string.Empty;

        /// <summary>Client auth secret (base64url, 16 bytes).</summary>
        public string Auth { get; set; } = string.Empty;

        /// <summary>Short label for the admin/device list — browser and platform only.</summary>
        public string? DeviceLabel { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

        public DateTime? LastSuccessAt { get; set; }

        public DateTime? DeactivatedAt { get; set; }

        public string? DeactivationReason { get; set; }

        public int ConsecutiveFailures { get; set; }

        public User User { get; set; } = null!;
    }
}
