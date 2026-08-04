using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>Append-only customer-document history. Application endpoints never update or delete these rows.</summary>
    public class CustomerDocumentAuditEntry
    {
        public long Id { get; set; }
        public int? CustomerId { get; set; }
        public int? RequirementId { get; set; }
        public int? CategoryId { get; set; }
        public int? VersionId { get; set; }
        public CustomerDocumentAction Action { get; set; }
        public CustomerDocumentStatus? PreviousStatus { get; set; }
        public CustomerDocumentStatus? NewStatus { get; set; }
        public string? Notes { get; set; }
        public int? PerformedByUserId { get; set; }
        public string? PerformedByName { get; set; }
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

        public Customer? Customer { get; set; }
        public CustomerDocumentRequirement? Requirement { get; set; }
        public CustomerDocumentCategory? Category { get; set; }
        public CustomerDocumentVersion? Version { get; set; }
    }
}
