namespace DAMS.Domain.Entities
{
    /// <summary>
    /// Who changed notification configuration, or who sent a broadcast, and what changed.
    /// Secret values are never written here — only the fact that a secret was replaced.
    /// </summary>
    public class NotificationAuditEntry
    {
        public int Id { get; set; }

        /// <summary>"settings", "template", "rule", "job", "suppression".</summary>
        public string Area { get; set; } = string.Empty;

        public string Action { get; set; } = string.Empty;

        public string? Details { get; set; }

        public int? EntityId { get; set; }

        public int PerformedByUserId { get; set; }

        public string? PerformedByName { get; set; }

        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    }
}
