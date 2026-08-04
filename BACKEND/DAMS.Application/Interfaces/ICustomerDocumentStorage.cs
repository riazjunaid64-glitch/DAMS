namespace DAMS.Application.Interfaces
{
    /// <summary>Opaque-key private storage; keys are never URLs or browser-supplied values.</summary>
    public interface ICustomerDocumentStorage
    {
        Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default);
        Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default);
        Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default);
    }
}
