using DAMS.Application.DTOs.ExpenseDtos;
using DAMS.Application.DTOs.FinanceDtos;

namespace DAMS.Application.Interfaces
{
    public interface IFinanceService
    {
        /// <summary>
        /// Builds the global financial dashboard (summary cards + revenue table + expense table)
        /// for the selected project (null = all projects) and optional date range.
        /// </summary>
        Task<FinanceDashboardDto> GetDashboardAsync(int? projectId, DateTime? from, DateTime? to);

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
