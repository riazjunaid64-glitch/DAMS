using DAMS.Application.DTOs.FinanceDtos;

namespace DAMS.Application.Interfaces
{
    public interface IStaffCashService
    {
        Task<StaffCashOverviewDto> GetOverviewAsync(bool includeSettled, CancellationToken cancellationToken = default);
        Task<StaffCashHolderDto> CreateHolderAsync(CreateStaffCashHolderDto dto, CancellationToken cancellationToken = default);
        Task<StaffCashStatementDto> GetStatementAsync(int staffFinanceAccountId, string? cursor, int take, CancellationToken cancellationToken = default);
        Task<StaffCashHistoryItemDto> RecordTransferAsync(int staffFinanceAccountId, SaveStaffCashTransferDto dto, int? userId, FinanceAttachmentUpload? attachment = null, CancellationToken cancellationToken = default);
        Task<StaffCashHistoryItemDto> UpdateTransferAsync(int staffFinanceAccountId, int transferId, SaveStaffCashTransferDto dto, int? userId, FinanceAttachmentUpload? attachment = null, bool removeAttachment = false, CancellationToken cancellationToken = default);
        Task DeleteTransferAsync(int staffFinanceAccountId, int transferId, string concurrencyToken, CancellationToken cancellationToken = default);
        Task<FinanceAttachmentDownload> GetTransferAttachmentAsync(int staffFinanceAccountId, int transferId, CancellationToken cancellationToken = default);
        Task RemoveTransferAttachmentAsync(int staffFinanceAccountId, int transferId, CancellationToken cancellationToken = default);
    }
}
