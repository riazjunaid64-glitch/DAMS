namespace DAMS.Domain.Entities
{
    /// <summary>Immutable private-file metadata for one uploaded file.</summary>
    public class CustomerDocumentVersion
    {
        public int Id { get; set; }
        public int RequirementId { get; set; }
        public int VersionNumber { get; set; }
        public bool IsCurrent { get; set; }
        public string StoredFileName { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public int? UploadedByUserId { get; set; }
        public string? UploadedByName { get; set; }
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
        public byte[] RowVersion { get; set; } = [];

        public CustomerDocumentRequirement Requirement { get; set; } = null!;
    }
}
