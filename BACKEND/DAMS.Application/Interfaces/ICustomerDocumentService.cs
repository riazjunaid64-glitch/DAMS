using DAMS.Application.Common;
using DAMS.Application.DTOs.CustomerDocumentDtos;
using DAMS.Application.DTOs.FinanceDtos;

namespace DAMS.Application.Interfaces
{
    public interface ICustomerDocumentService
    {
        Task<List<CustomerDocumentCategoryDto>> GetCategoriesAsync(bool includeInactive, CancellationToken cancellationToken = default);
        Task<CustomerDocumentCategoryDto> CreateCategoryAsync(CreateCustomerDocumentCategoryDto dto, CustomerDocumentActor actor, CancellationToken cancellationToken = default);
        Task<CustomerDocumentCategoryDto> UpdateCategoryAsync(int id, UpdateCustomerDocumentCategoryDto dto, CustomerDocumentActor actor, CancellationToken cancellationToken = default);
        Task DeleteCategoryAsync(int id, CustomerDocumentActor actor, CancellationToken cancellationToken = default);
        Task<CustomerDocumentAssignmentResultDto> AssignCategoryAsync(int id, AssignCustomerDocumentCategoryDto dto, CustomerDocumentActor actor, CancellationToken cancellationToken = default);
        Task<CustomerDocumentChecklistDto> GetChecklistAsync(int customerId, CancellationToken cancellationToken = default);
        Task<PagedResult<CustomerDocumentAuditDto>> GetHistoryAsync(int customerId, int? beforeId, int take, CancellationToken cancellationToken = default);
        Task<CustomerDocumentRequirementDto> AddRequirementAsync(int customerId, AddCustomerDocumentRequirementDto dto, CustomerDocumentActor actor, CancellationToken cancellationToken = default);
        Task<CustomerDocumentRequirementDto> UploadAsync(int customerId, int requirementId, string concurrencyToken, CustomerDocumentUpload upload, CustomerDocumentActor actor, CancellationToken cancellationToken = default);
        Task<CustomerDocumentRequirementDto> ChangeStatusAsync(int customerId, int requirementId, CustomerDocumentStatusChangeDto dto, CustomerDocumentActor actor, CancellationToken cancellationToken = default);
        Task<CustomerDocumentRequirementDto> ChangeDueDateAsync(int customerId, int requirementId, CustomerDocumentDueDateDto dto, CustomerDocumentActor actor, CancellationToken cancellationToken = default);
        Task<CustomerDocumentDownload> DownloadAsync(int customerId, int requirementId, int versionId, CustomerDocumentActor actor, CancellationToken cancellationToken = default);
    }
}
