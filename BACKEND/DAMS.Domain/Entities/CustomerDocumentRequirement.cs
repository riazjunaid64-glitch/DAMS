using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>One checklist item for one customer. Category values are snapshotted at assignment time.</summary>
    public class CustomerDocumentRequirement
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public int? CategoryId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsRequired { get; set; }
        public int DisplayOrder { get; set; }
        public CustomerDocumentStatus Status { get; set; } = CustomerDocumentStatus.Needed;
        public string? NotNeededReason { get; set; }
        public int? NotNeededByUserId { get; set; }
        public string? NotNeededByName { get; set; }
        public DateTime? NotNeededAt { get; set; }
        public int? LastActionByUserId { get; set; }
        public string? LastActionByName { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public byte[] RowVersion { get; set; } = [];

        public Customer Customer { get; set; } = null!;
        public CustomerDocumentCategory? Category { get; set; }
        public ICollection<CustomerDocumentVersion> Versions { get; set; } = new List<CustomerDocumentVersion>();
        public ICollection<CustomerDocumentAuditEntry> AuditEntries { get; set; } = new List<CustomerDocumentAuditEntry>();
    }
}
