namespace DAMS.Domain.Entities
{
    /// <summary>A reusable definition. Requirements snapshot its business fields so history survives later edits.</summary>
    public class CustomerDocumentCategory
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsRequiredByDefault { get; set; }
        public int DisplayOrder { get; set; }
        public string AllowedFileTypes { get; set; } = ".pdf,.jpg,.jpeg,.png";
        public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;
        public bool IsActive { get; set; } = true;
        public bool AssignToNewCustomers { get; set; }
        public int? DefaultDueDays { get; set; }
        public int? CreatedByUserId { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public byte[] RowVersion { get; set; } = [];

        public ICollection<CustomerDocumentRequirement> Requirements { get; set; } = new List<CustomerDocumentRequirement>();
    }
}
