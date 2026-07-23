namespace DAMS.Application.DTOs.FinanceDtos
{
    public sealed class FinanceAttachmentDto
    {
        public string FileName { get; set; } = string.Empty;

        public string ContentType { get; set; } = string.Empty;

        public long FileSize { get; set; }

        public DateTime UploadedAt { get; set; }
    }

    public sealed class FinanceAttachmentUpload
    {
        public required Stream Content { get; init; }

        public required string FileName { get; init; }

        public string? ContentType { get; init; }

        public long Length { get; init; }
    }

    public sealed class FinanceAttachmentDownload
    {
        public required Stream Content { get; init; }

        public required string FileName { get; init; }

        public required string ContentType { get; init; }
    }

    public enum FinanceRecordKind
    {
        Revenue,
        Expense
    }
}
