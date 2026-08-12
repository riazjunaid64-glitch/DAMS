using DAMS.Application.DTOs.ExpenseDtos;
using DAMS.Application.DTOs.FinanceDtos;

namespace DAMS.Application.Interfaces
{
    public interface IFinanceService
    {
        /// <summary>
        /// Builds the 5 summary cards (totals only) for the selected project
        /// (null = all projects) and optional date range. Table rows are paged separately.
        /// </summary>
        Task<FinancialSummaryDto> GetSummaryAsync(int? projectId, DateTime? from, DateTime? to, int? accountId = null, bool unassigned = false);

        // ── Paged table rows (infinite scroll). Each returns one page + HasMore. ──
        Task<PagedResult<RevenueLineDto>> GetRevenuePageAsync(int? projectId, DateTime? from, DateTime? to, int skip, int take, int? accountId = null, bool unassigned = false);
        Task<PagedResult<ExpenseLineDto>> GetExpensePageAsync(int? projectId, DateTime? from, DateTime? to, int skip, int take, int? accountId = null, bool unassigned = false);
        Task<PagedResult<OutstandingLineDto>> GetOutstandingPageAsync(int? projectId, int skip, int take);
        Task<PagedResult<OverdueLineDto>> GetOverduePageAsync(int? projectId, int skip, int take);
        Task<PagedResult<NetProfitLineDto>> GetNetProfitPageAsync(int? projectId, DateTime? from, DateTime? to, int skip, int take, int? accountId = null, bool unassigned = false);

        Task<ProfitAndLossDto> GetProfitAndLossAsync(int? projectId, DateTime? from, DateTime? to, CancellationToken cancellationToken = default);
        Task<TrialBalanceDto> GetTrialBalanceAsync(int? projectId, DateTime asAt, int monthsBack, CancellationToken cancellationToken = default);
        Task<BalanceSheetDto> GetBalanceSheetAsync(int? projectId, DateTime asAt, CancellationToken cancellationToken = default);
        Task<FinanceExportDto> ExportProfitAndLossAsync(int? projectId, DateTime? from, DateTime? to, CancellationToken cancellationToken = default);
        Task<FinanceExportDto> ExportTrialBalanceAsync(int? projectId, DateTime asAt, int monthsBack, CancellationToken cancellationToken = default);
        Task<FinanceExportDto> ExportBalanceSheetAsync(int? projectId, DateTime asAt, CancellationToken cancellationToken = default);

        // Manual revenue CRUD
        Task<ManualRevenueResponseDto> CreateManualRevenueAsync(CreateManualRevenueDto dto, int? adminUserId, FinanceAttachmentUpload? attachment = null, CancellationToken cancellationToken = default);
        Task<ManualRevenueResponseDto> UpdateManualRevenueAsync(int id, UpdateManualRevenueDto dto, FinanceAttachmentUpload? attachment = null, bool removeAttachment = false, CancellationToken cancellationToken = default);
        Task DeleteManualRevenueAsync(int id, CancellationToken cancellationToken = default);

        // Expense CRUD
        Task<ExpenseResponseDto> CreateExpenseAsync(CreateExpenseDto dto, int? adminUserId, FinanceAttachmentUpload? attachment = null, CancellationToken cancellationToken = default);
        Task<ExpenseResponseDto> UpdateExpenseAsync(int id, UpdateExpenseDto dto, FinanceAttachmentUpload? attachment = null, bool removeAttachment = false, CancellationToken cancellationToken = default);
        Task DeleteExpenseAsync(int id, CancellationToken cancellationToken = default);

        Task<FinanceAttachmentDownload> GetAttachmentAsync(FinanceRecordKind kind, int recordId, CancellationToken cancellationToken = default);
        Task RemoveAttachmentAsync(FinanceRecordKind kind, int recordId, CancellationToken cancellationToken = default);
    }
}
