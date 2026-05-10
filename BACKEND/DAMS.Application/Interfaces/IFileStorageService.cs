namespace DAMS.Application.Interfaces
{
    public interface IFileStorageService
    {
        Task<string> SaveFileAsync(Stream fileStream, string fileName, string folder, CancellationToken cancellationToken = default);
        Task DeleteFileAsync(string filePath, CancellationToken cancellationToken = default);
        Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default);
        Task<Stream> GetFileAsync(string filePath, CancellationToken cancellationToken = default);
        Task<string> GetPresignedUrlAsync(string filePath, TimeSpan expiration, CancellationToken cancellationToken = default);
    }
}
