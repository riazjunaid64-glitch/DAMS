using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.MediaDtos
{
    public class UpdateMediaDto
    {
        public MediaCategory? Category { get; set; }
        public bool? IsCover { get; set; }
        public int? DisplayOrder { get; set; }
        public string? AltText { get; set; }
        public string? Description { get; set; }
    }
}
