using DAMS.Application.Interfaces;

namespace DAMS.Application.Services
{
public class LocalFileStorageService : IFileStorageService
{
    private readonly string _webRootPath;

    public LocalFileStorageService(string webRootPath)
    {
        _webRootPath = string.IsNullOrWhiteSpace(webRootPath)
            ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
            : webRootPath;
    }

    public async Task<string> SaveFileAsync(Stream fileStream, string fileName, string folder)
    {
        // 1. Generate unique file name
        var fileNameWithExt = Guid.NewGuid().ToString() + Path.GetExtension(fileName);

        // 2. Build folder path
        var uploadPath = Path.Combine(_webRootPath, "uploads", folder);

        // 3. Ensure directory exists
        if (!Directory.Exists(uploadPath))
        {
            Directory.CreateDirectory(uploadPath);
        }

        // 4. Full file path
        var filePath = Path.Combine(uploadPath, fileNameWithExt);

        // 5. Save file
        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await fileStream.CopyToAsync(stream);
        }

        // 6. Return relative URL
        return $"/uploads/{folder}/{fileNameWithExt}";
    }

    public Task DeleteFileAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Task.CompletedTask;
        }

        var fullPath = Path.Combine(_webRootPath, filePath.TrimStart('/'));

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }
}
}
