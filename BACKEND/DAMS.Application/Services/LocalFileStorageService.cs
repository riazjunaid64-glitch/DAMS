using DAMS.Application.Interfaces;

namespace DAMS.Application.Services
{
public class LocalFileStorageService : IFileStorageService
{
    private readonly string _webRootPath;
    private static readonly object _fileLock = new object();

    public LocalFileStorageService(string webRootPath)
    {
        _webRootPath = string.IsNullOrWhiteSpace(webRootPath)
            ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
            : webRootPath;
    }

    public async Task<string> SaveFileAsync(Stream fileStream, string fileName, string folder, CancellationToken cancellationToken = default)
    {
        // 1. Generate unique file name
        var fileNameWithExt = Guid.NewGuid().ToString() + Path.GetExtension(fileName);

        // 2. Build folder path
        var uploadPath = Path.Combine(_webRootPath, "uploads", folder);

        // 3. Ensure directory exists
        if (!Directory.Exists(uploadPath))
        {
            lock (_fileLock)
            {
                if (!Directory.Exists(uploadPath))
                {
                    Directory.CreateDirectory(uploadPath);
                }
            }
        }

        // 4. Full file path
        var filePath = Path.Combine(uploadPath, fileNameWithExt);

        // 5. Save file with cancellation support
        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await fileStream.CopyToAsync(stream, cancellationToken);
        }

        // 6. Return relative URL
        return $"/uploads/{folder}/{fileNameWithExt}";
    }

    public Task DeleteFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Task.CompletedTask;
        }

        var fullPath = Path.Combine(_webRootPath, filePath.TrimStart('/'));

        if (File.Exists(fullPath))
        {
            lock (_fileLock)
            {
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Task.FromResult(false);
        }

        var fullPath = Path.Combine(_webRootPath, filePath.TrimStart('/'));
        return Task.FromResult(File.Exists(fullPath));
    }

    public async Task<Stream> GetFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
        }

        var fullPath = Path.Combine(_webRootPath, filePath.TrimStart('/'));

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        return await Task.FromResult<FileStream>(new FileStream(fullPath, FileMode.Open, FileAccess.Read));
    }

    public Task<string> GetPresignedUrlAsync(string filePath, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        // For local storage, presigned URLs are not applicable
        // Return the regular URL since files are served directly
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
        }

        return Task.FromResult(filePath);
    }
}
}
