namespace DAMS.Application.Interfaces
{
    public interface IFinancialEvidenceStorage
    {
        Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default);
        Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default);
        Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default);
    }
}
