namespace DAMS.Application.DTOs.LeadDtos
{
    public sealed class LeadDocumentUpload
    {
        public required Stream Content { get; init; }

        public required string FileName { get; init; }

        public string? ContentType { get; init; }

        public long Length { get; init; }
    }

    public sealed class LeadDocumentDownload
    {
        public required Stream Content { get; init; }

        public required string FileName { get; init; }

        public required string ContentType { get; init; }
    }
}
