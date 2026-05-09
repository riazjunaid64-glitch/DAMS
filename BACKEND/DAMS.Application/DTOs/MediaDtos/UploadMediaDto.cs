using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.MediaDtos
{
    public class UploadMediaDto
    {
        public MediaCategory Category { get; set; } = MediaCategory.Gallery;
        public string? AltText { get; set; }
        public string? Description { get; set; }
        public bool IsCover { get; set; } = false;
    }
}
