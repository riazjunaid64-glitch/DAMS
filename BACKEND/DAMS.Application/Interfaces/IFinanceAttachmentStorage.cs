namespace DAMS.Application.Interfaces
{
    /// <summary>Storage for private finance files. Values returned by SaveAsync are opaque keys, never URLs.</summary>
    public interface IFinanceAttachmentStorage
    {
        Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default);

        Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default);

        Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default);
    }
}
