using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    public class ProjectMedia
    {
        public int Id { get; set; }

        public int ProjectId { get; set; }

        public string MediaUrl { get; set; } = string.Empty;

        public string MediaType { get; set; } = string.Empty;

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        
        public MediaCategory Category { get; set; } = MediaCategory.Gallery;

        public bool IsCover { get; set; } = false;

        public int DisplayOrder { get; set; } = 0;

        public string? AltText { get; set; }

        public string? Description { get; set; }

        public long FileSize { get; set; } // in bytes

        public int? Width { get; set; }

        public int? Height { get; set; }

        public string? OriginalFileName { get; set; }

        public string? MimeType { get; set; }

        public DateTime? UpdatedAt { get; set; }

        // Navigation
        public Project Project { get; set; } = null!;
    }
}