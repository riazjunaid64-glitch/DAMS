using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.MediaDtos
{
    public class ProjectMediaResponseDto
    {
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public string MediaUrl { get; set; } = string.Empty;
        public string MediaType { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; }
        
        // Enterprise metadata
        public MediaCategory Category { get; set; }
        public bool IsCover { get; set; }
        public int DisplayOrder { get; set; }
        public string? AltText { get; set; }
        public string? Description { get; set; }
        public long FileSize { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public string? OriginalFileName { get; set; }
        public string? MimeType { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
