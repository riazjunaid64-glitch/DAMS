namespace DAMS.Domain.Entities
{
    public class ProjectMedia
    {
        public int Id { get; set; }

        public int ProjectId { get; set; }

        public string MediaUrl { get; set; } = string.Empty;

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public Project Project { get; set; } = null!;
    }
}