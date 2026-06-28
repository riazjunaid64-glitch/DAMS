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
        Task<FinancialSummaryDto> GetSummaryAsync(int? projectId, DateTime? from, DateTime? to);

        // ── Paged table rows (infinite scroll). Each returns one page + HasMore. ──
        Task<PagedResult<RevenueLineDto>> GetRevenuePageAsync(int? projectId, DateTime? from, DateTime? to, int skip, int take);
        Task<PagedResult<ExpenseLineDto>> GetExpensePageAsync(int? projectId, DateTime? from, DateTime? to, int skip, int take);
        Task<PagedResult<OutstandingLineDto>> GetOutstandingPageAsync(int? projectId, int skip, int take);
        Task<PagedResult<OverdueLineDto>> GetOverduePageAsync(int? projectId, int skip, int take);
        Task<PagedResult<NetProfitLineDto>> GetNetProfitPageAsync(int? projectId, DateTime? from, DateTime? to, int skip, int take);

        // Manual revenue CRUD
        Task<ManualRevenueResponseDto> CreateManualRevenueAsync(CreateManualRevenueDto dto, int? adminUserId);
        Task<ManualRevenueResponseDto> UpdateManualRevenueAsync(int id, UpdateManualRevenueDto dto);
        Task DeleteManualRevenueAsync(int id);

        // Expense CRUD
        Task<ExpenseResponseDto> CreateExpenseAsync(CreateExpenseDto dto, int? adminUserId);
        Task<ExpenseResponseDto> UpdateExpenseAsync(int id, UpdateExpenseDto dto);
        Task DeleteExpenseAsync(int id);
    }
}
