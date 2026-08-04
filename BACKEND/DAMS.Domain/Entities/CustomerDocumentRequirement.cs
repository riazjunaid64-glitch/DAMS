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
        public string AllowedFileTypes { get; set; } = ".pdf,.jpg,.jpeg,.png";
        public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;
        public CustomerDocumentStatus Status { get; set; } = CustomerDocumentStatus.Missing;
        public DateTime? DueDate { get; set; }
        public DateTime? PostponedUntil { get; set; }
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
