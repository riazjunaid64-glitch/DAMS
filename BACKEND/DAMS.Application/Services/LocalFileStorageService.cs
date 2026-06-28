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

    private string ResolveAndValidatePath(string filePath)
    {
        var combined = Path.Combine(_webRootPath, filePath.TrimStart('/').TrimStart('\\'));
        var canonical = Path.GetFullPath(combined);
        var root = Path.GetFullPath(_webRootPath);
        if (!canonical.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !canonical.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Access to the requested path is denied.");
        }
        return canonical;
    }

    public Task DeleteFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Task.CompletedTask;
        }

        var fullPath = ResolveAndValidatePath(filePath);

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
}
}
