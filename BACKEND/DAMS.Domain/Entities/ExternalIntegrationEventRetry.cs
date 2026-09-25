namespace DAMS.Domain.Entities
{
    /// <summary>Append-only audit record for an operator-requested Meta event recovery.</summary>
    public class ExternalIntegrationEventRetry
    {
        public long Id { get; set; }

        public int ExternalIntegrationEventId { get; set; }

        public int RequestedByUserId { get; set; }

        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

        public ExternalIntegrationEvent? Event { get; set; }
    }
}
