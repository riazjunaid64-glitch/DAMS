namespace DAMS.Domain.Entities
{
    /// <summary>
    /// Immutable receipt for a provider submission. Multiple external submissions may enrich
    /// the same lead, but each provider/id pair may be processed only once.
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

        public Lead Lead { get; set; } = null!;
    }
}
