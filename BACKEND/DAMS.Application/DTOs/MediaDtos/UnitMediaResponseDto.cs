namespace DAMS.Application.DTOs.MediaDtos
{
    public class UnitMediaResponseDto
    {
        public int Id { get; set; }
        public int UnitId { get; set; }
        public string MediaUrl { get; set; } = string.Empty;
        public string MediaType { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; }
    }
}
