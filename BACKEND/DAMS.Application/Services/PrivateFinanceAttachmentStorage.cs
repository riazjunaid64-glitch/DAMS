using DAMS.Application.Interfaces;

namespace DAMS.Application.Services
{
    public sealed class PrivateFinanceAttachmentStorage : IFinanceAttachmentStorage
    {
        private readonly string _storageRoot;

        public PrivateFinanceAttachmentStorage(string storageRoot)
        {
            if (string.IsNullOrWhiteSpace(storageRoot))
                throw new ArgumentException("A private attachment storage path is required.", nameof(storageRoot));
            _storageRoot = Path.GetFullPath(storageRoot);
        }

        public async Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(_storageRoot);
            var storedFileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
            var fullPath = Resolve(storedFileName);

            try
            {
                content.Position = 0;
                await using var destination = new FileStream(
                    fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
                    FileOptions.Asynchronous | FileOptions.WriteThrough);
                await content.CopyToAsync(destination, cancellationToken);
                await destination.FlushAsync(cancellationToken);
                return storedFileName;
            }
            catch
            {
                if (File.Exists(fullPath))
                    File.Delete(fullPath);
                throw;
            }
        }

        public Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Resolve(storedFileName);
            Stream? stream = File.Exists(fullPath)
                ? new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan)
                : null;
            return Task.FromResult(stream);
        }

        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Resolve(storedFileName);
            if (File.Exists(fullPath))
                File.Delete(fullPath);
            return Task.CompletedTask;
        }

        private string Resolve(string storedFileName)
        {
            if (string.IsNullOrWhiteSpace(storedFileName) || Path.GetFileName(storedFileName) != storedFileName)
                throw new UnauthorizedAccessException("Invalid attachment storage key.");

            var fullPath = Path.GetFullPath(Path.Combine(_storageRoot, storedFileName));
            if (!fullPath.StartsWith(_storageRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Invalid attachment storage key.");
            return fullPath;
        }
    }
}
