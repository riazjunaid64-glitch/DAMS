using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>
    /// A private file attached to a lead. Bytes live outside the web root and are served
    /// only through an authorised endpoint; this row holds safe metadata only.
    /// </summary>
    public class LeadDocument
    {
        public int Id { get; set; }

        public int LeadId { get; set; }

        // Set when the document arrived with a specific recorded communication.
        public int? CommunicationId { get; set; }

        public LeadDocumentCategory Category { get; set; } = LeadDocumentCategory.Other;

        public string StoredFileName { get; set; } = string.Empty;

        public string OriginalFileName { get; set; } = string.Empty;

        public string ContentType { get; set; } = string.Empty;

        public long FileSize { get; set; }

        public string? Description { get; set; }

        public int? UploadedByUserId { get; set; }

        public string? UploadedByName { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        public Lead Lead { get; set; } = null!;

        public LeadCommunication? Communication { get; set; }
    }
}
