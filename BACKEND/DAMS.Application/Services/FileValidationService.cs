using SkiaSharp;

namespace DAMS.Application.Services
{
    public class FileValidationService
    {
        private const long MaxFileSize = 50 * 1024 * 1024; // 50 MB
        private const long MaxImageSize = 20 * 1024 * 1024; // 20 MB for images
        private const long MaxVideoSize = 100 * 1024 * 1024; // 100 MB for videos
        private const long MaxDocumentSize = 10 * 1024 * 1024; // 10 MB for documents

        private static readonly string[] AllowedImageTypes =
        {
            "image/jpeg", "image/png", "image/gif", "image/webp"
            // image/svg+xml excluded: SVG can embed <script> tags and bypass content scanning
        };
        
        private static readonly string[] AllowedVideoTypes = 
        { 
            "video/mp4", "video/webm", "video/quicktime", "video/x-msvideo" 
        };
        
        private static readonly string[] AllowedDocumentTypes = 
        { 
            "application/pdf", 
            "application/msword", 
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "application/vnd.ms-excel",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        };

        private static readonly string[] DangerousExtensions = 
        { 
            ".exe", ".bat", ".cmd", ".sh", ".ps1", ".dll", ".sys", ".scr", ".vbs", ".js", ".jar" 
        };

        public static void ValidateFile(Stream fileStream, string contentType, string fileName)
        {
            // Check file size
            var fileSize = fileStream.Length;
            
            if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                if (fileSize > MaxImageSize)
                    throw new Exception($"Image size exceeds maximum allowed size of {MaxImageSize / 1024 / 1024} MB.");
            }
            else if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            {
                if (fileSize > MaxVideoSize)
                    throw new Exception($"Video size exceeds maximum allowed size of {MaxVideoSize / 1024 / 1024} MB.");
            }
            else if (AllowedDocumentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            {
                if (fileSize > MaxDocumentSize)
                    throw new Exception($"Document size exceeds maximum allowed size of {MaxDocumentSize / 1024 / 1024} MB.");
            }
            else if (fileSize > MaxFileSize)
            {
                throw new Exception($"File size exceeds maximum allowed size of {MaxFileSize / 1024 / 1024} MB.");
            }

            // Check MIME type
            var isAllowedType = AllowedImageTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase) ||
                                AllowedVideoTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase) ||
                                AllowedDocumentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase);

            if (!isAllowedType)
            {
                throw new Exception($"File type '{contentType}' is not allowed. Allowed types: images, videos, and documents (PDF, Word, Excel).");
            }

            // Check file extension safety
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            if (DangerousExtensions.Contains(ext))
            {
                throw new Exception($"File extension '{ext}' is not allowed for security reasons.");
            }

            // Additional validation for images to prevent malicious files
            if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                ValidateImageContent(fileStream);
            }
        }

        private static void ValidateImageContent(Stream fileStream)
        {
            var originalPosition = fileStream.Position;
            try
            {
                fileStream.Position = 0;
                using var managedStream = new SKManagedStream(fileStream, false);
                using var codec = SKCodec.Create(managedStream)
                    ?? throw new InvalidOperationException("The file does not contain a recognized image.");
                
                // Validate image dimensions (prevent extremely large images)
                if (codec.Info.Width > 10000 || codec.Info.Height > 10000)
                {
                    throw new Exception("Image dimensions exceed maximum allowed size (10000x10000 pixels).");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Invalid image file: {ex.Message}");
            }
            finally
            {
                fileStream.Position = originalPosition;
            }
        }

        public static (int? Width, int? Height) GetImageDimensions(Stream fileStream, string contentType)
        {
            if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) || contentType == "image/svg+xml")
            {
                return (null, null);
            }

            var originalPosition = fileStream.Position;
            try
            {
                fileStream.Position = 0;
                using var managedStream = new SKManagedStream(fileStream, false);
                using var codec = SKCodec.Create(managedStream);
                return codec == null
                    ? (null, null)
                    : (codec.Info.Width, codec.Info.Height);
            }
            catch
            {
                return (null, null);
            }
            finally
            {
                fileStream.Position = originalPosition;
            }
        }

        public static string SanitizeFileName(string fileName)
        {
            // Remove path separators and special characters
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = string.Join("_", fileName.Split(invalidChars));
            
            // Limit length
            if (sanitized.Length > 255)
            {
                var ext = Path.GetExtension(sanitized);
                var nameWithoutExt = Path.GetFileNameWithoutExtension(sanitized);
                sanitized = nameWithoutExt.Substring(0, 255 - ext.Length) + ext;
            }

            return sanitized;
        }
    }
}
