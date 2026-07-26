using DAMS.Application.DTOs.FinanceDtos;

namespace DAMS.Application.Services
{
    public sealed record ValidatedFinanceAttachment(string OriginalFileName, string Extension, string ContentType, long FileSize);

    /// <summary>
    /// Finance-specific limits over the shared <see cref="UploadedFileValidator"/>.
    /// </summary>
    public static class FinanceAttachmentFileValidator
    {
        public const long MaxFileSize = 15 * 1024 * 1024;
        public const long MaxRequestSize = MaxFileSize + (512 * 1024);

        public static ValidatedFinanceAttachment Validate(FinanceAttachmentUpload upload)
        {
            var validated = UploadedFileValidator.Validate(upload.Content, upload.FileName, upload.Length, MaxFileSize);
            return new ValidatedFinanceAttachment(
                validated.OriginalFileName, validated.Extension, validated.ContentType, validated.FileSize);
        }
    }
}
