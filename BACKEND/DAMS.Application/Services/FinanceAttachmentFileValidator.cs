using System.IO.Compression;
using System.Text;
using DAMS.Application.DTOs.FinanceDtos;
using SkiaSharp;

namespace DAMS.Application.Services
{
    public sealed record ValidatedFinanceAttachment(string OriginalFileName, string Extension, string ContentType, long FileSize);

    public static class FinanceAttachmentFileValidator
    {
        public const long MaxFileSize = 15 * 1024 * 1024;
        public const long MaxRequestSize = MaxFileSize + (512 * 1024);

        private static readonly IReadOnlyDictionary<string, string> AllowedTypes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [".pdf"] = "application/pdf",
                [".jpg"] = "image/jpeg",
                [".jpeg"] = "image/jpeg",
                [".png"] = "image/png",
                [".webp"] = "image/webp",
                [".doc"] = "application/msword",
                [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                [".xls"] = "application/vnd.ms-excel",
                [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            };

        public static ValidatedFinanceAttachment Validate(FinanceAttachmentUpload upload)
        {
            if (upload.Length <= 0)
                throw new InvalidOperationException("The selected attachment is empty.");
            if (upload.Length > MaxFileSize)
                throw new InvalidOperationException("The attachment is too large. The maximum allowed size is 15 MB.");
            if (!upload.Content.CanRead || !upload.Content.CanSeek)
                throw new InvalidOperationException("The selected attachment could not be read.");

            var originalName = SanitizeFileName(upload.FileName);
            var extension = Path.GetExtension(originalName).ToLowerInvariant();
            if (!AllowedTypes.TryGetValue(extension, out var contentType))
                throw new InvalidOperationException("This file type is not supported. Choose a PDF, JPG, PNG, WebP, Word, or Excel file.");

            var originalPosition = upload.Content.Position;
            try
            {
                upload.Content.Position = 0;
                if (upload.Content.Length == 0)
                    throw new InvalidOperationException("The selected attachment is empty.");
                if (upload.Content.Length != upload.Length)
                    throw new InvalidOperationException("The attachment could not be read completely. Please select it again.");

                if (contentType.StartsWith("image/", StringComparison.Ordinal))
                    ValidateImage(upload.Content, extension);
                else if (extension == ".pdf")
                    ValidatePdf(upload.Content);
                else if (extension is ".doc" or ".xls")
                    ValidateOleDocument(upload.Content);
                else
                    ValidateOpenXmlDocument(upload.Content, extension);
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new InvalidOperationException("The attachment appears to be corrupted or does not match its file extension.");
            }
            finally
            {
                upload.Content.Position = originalPosition;
            }

            return new ValidatedFinanceAttachment(originalName, extension, contentType, upload.Length);
        }

        private static void ValidateImage(Stream content, string extension)
        {
            content.Position = 0;
            using var managed = new SKManagedStream(content, false);
            using var codec = SKCodec.Create(managed);
            if (codec == null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
                throw new InvalidOperationException("The selected image is empty or corrupted.");
            if (codec.Info.Width > 10000 || codec.Info.Height > 10000)
                throw new InvalidOperationException("The image dimensions cannot exceed 10000 x 10000 pixels.");

            var expected = extension switch
            {
                ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
                ".png" => SKEncodedImageFormat.Png,
                ".webp" => SKEncodedImageFormat.Webp,
                _ => throw new InvalidOperationException("This image type is not supported.")
            };
            if (codec.EncodedFormat != expected)
                throw new InvalidOperationException("The image content does not match its file extension.");
        }

        private static void ValidatePdf(Stream content)
        {
            var header = new byte[5];
            content.Position = 0;
            if (content.Read(header, 0, header.Length) != header.Length || Encoding.ASCII.GetString(header) != "%PDF-")
                throw new InvalidOperationException("The selected PDF is empty, corrupted, or is not a PDF file.");

            var tailLength = (int)Math.Min(2048, content.Length);
            var tail = new byte[tailLength];
            content.Position = content.Length - tailLength;
            _ = content.Read(tail, 0, tail.Length);
            if (!Encoding.ASCII.GetString(tail).Contains("%%EOF", StringComparison.Ordinal))
                throw new InvalidOperationException("The selected PDF appears to be incomplete or corrupted.");
        }

        private static void ValidateOleDocument(Stream content)
        {
            byte[] signature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
            var actual = new byte[signature.Length];
            content.Position = 0;
            if (content.Read(actual, 0, actual.Length) != actual.Length || !actual.SequenceEqual(signature))
                throw new InvalidOperationException("The document is corrupted or does not match its file extension.");
        }

        private static void ValidateOpenXmlDocument(Stream content, string extension)
        {
            content.Position = 0;
            using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
            var requiredEntry = extension == ".docx" ? "word/document.xml" : "xl/workbook.xml";
            if (archive.GetEntry("[Content_Types].xml") == null || archive.GetEntry(requiredEntry) == null)
                throw new InvalidOperationException("The document is corrupted or does not match its file extension.");
        }

        private static string SanitizeFileName(string fileName)
        {
            var name = Path.GetFileName(fileName ?? string.Empty).Trim();
            name = new string(name.Where(c => !char.IsControl(c) && !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("The attachment file name is invalid.");

            var extension = Path.GetExtension(name);
            var stem = Path.GetFileNameWithoutExtension(name);
            var maxStemLength = Math.Max(1, 180 - extension.Length);
            if (stem.Length > maxStemLength)
                stem = stem[..maxStemLength];
            return stem + extension;
        }
    }
}
