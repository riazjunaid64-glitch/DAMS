namespace DAMS.Domain.Entities
{
    /// <summary>
    /// What an administrator has said a provider lead form means: which project its leads are
    /// about, and which of its answers fill which lead field.
    ///
    /// Keyed by the provider and the form's own id rather than by a discovered resource row, so
    /// the setting survives a reconnect or a resync that recreates those rows. Changing it only
    /// affects submissions that arrive afterwards; stored submissions are never rewritten.
    /// </summary>
    public class ExternalLeadFormMapping
    {
        public int Id { get; set; }

        public string Provider { get; set; } = string.Empty;

        public string FormExternalId { get; set; } = string.Empty;

        public int? InterestedProjectId { get; set; }

        /// <summary>Question → lead field and option → value, serialized. See LeadFormAnswerMappings.</summary>
        public string? AnswerMappingsJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public int? UpdatedByUserId { get; set; }

        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public Project? InterestedProject { get; set; }
    }
}
