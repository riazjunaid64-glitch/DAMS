using DAMS.Application.Common;
using DAMS.Application.DTOs.CustomerDocumentDtos;
using DAMS.Application.DTOs.FinanceDtos;

namespace DAMS.Application.Interfaces
{
    public interface ICustomerDocumentService
    {
        Task<DocumentSetupListDto> GetSetupAsync(CancellationToken cancellationToken = default);
        Task<DocumentSetupItemDto> CreateDocumentAsync(SaveDocumentNameDto dto, CustomerDocumentActor actor, CancellationToken cancellationToken = default);
        Task<DocumentSetupItemDto> RenameDocumentAsync(int id, SaveDocumentNameDto dto, CustomerDocumentActor actor, CancellationToken cancellationToken = default);
        Task RemoveDocumentAsync(int id, CustomerDocumentActor actor, CancellationToken cancellationToken = default);
        Task<DocumentSetupItemDto> SetAsksEveryCustomerAsync(int id, bool asksEveryCustomer, CustomerDocumentActor actor, CancellationToken cancellationToken = default);
        Task<CustomerDocumentChecklistDto> GetChecklistAsync(int customerId, CancellationToken cancellationToken = default);
        Task<PagedResult<CustomerDocumentAuditDto>> GetHistoryAsync(int customerId, int? beforeId, int take, CancellationToken cancellationToken = default);
        Task<PagedResult<CustomerDocumentVersionDto>> GetVersionsAsync(int customerId, int requirementId,
            int? beforeVersionNumber, int take, CancellationToken cancellationToken = default);
        Task<CustomerDocumentRequirementDto> AddDocumentAsync(int customerId, int? categoryId, string? name, CustomerDocumentUpload upload, CustomerDocumentActor actor, CancellationToken cancellationToken = default);
        Task<CustomerDocumentRequirementDto> UploadAsync(int customerId, int requirementId, string concurrencyToken, CustomerDocumentUpload upload, CustomerDocumentActor actor, CancellationToken cancellationToken = default);
        Task<CustomerDocumentRequirementDto> MarkNotNeededAsync(int customerId, int requirementId, NotNeededDocumentDto dto, CustomerDocumentActor actor, CancellationToken cancellationToken = default);
        Task<CustomerDocumentDownload> DownloadAsync(int customerId, int requirementId, int versionId, CustomerDocumentActor actor, CancellationToken cancellationToken = default);
        Task<CustomerDocumentDownload> ViewAsync(int customerId, int requirementId, int versionId, CustomerDocumentActor actor, CancellationToken cancellationToken = default);
    }
}
