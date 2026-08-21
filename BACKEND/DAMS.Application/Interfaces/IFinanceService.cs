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
        Task<FinancialSummaryDto> GetSummaryAsync(int? projectId, DateTime? from, DateTime? to, int? accountId = null, bool unassigned = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// The whole dashboard screen — cards, trend and revenue-by-project — in one answer, built
        /// from one set of date bounds. Replaces the client-side fan-out that ran a full summary per
        /// chart bucket and another per project, so the database work no longer grows with either.
        /// </summary>
        Task<FinanceDashboardDataDto> GetDashboardAsync(int? projectId, DateTime? from, DateTime? to, int? accountId = null, bool unassigned = false, CancellationToken cancellationToken = default);

        // ── Paged table rows (infinite scroll). Each returns one page + HasMore. ──
        Task<PagedResult<RevenueLineDto>> GetRevenuePageAsync(int? projectId, DateTime? from, DateTime? to, int skip, int take, int? accountId = null, bool unassigned = false, CancellationToken cancellationToken = default);
        Task<PagedResult<ExpenseLineDto>> GetExpensePageAsync(int? projectId, DateTime? from, DateTime? to, int skip, int take, int? accountId = null, bool unassigned = false, CancellationToken cancellationToken = default);
        /// <summary>Customer money held but not yet earned, one row per booking, as at
        /// <paramref name="to"/>. A balance view: there is no period start.</summary>
        Task<PagedResult<CustomerDepositLineDto>> GetCustomerDepositPageAsync(int? projectId, DateTime? to, int skip, int take, CancellationToken cancellationToken = default);
        Task<PagedResult<OutstandingLineDto>> GetOutstandingPageAsync(int? projectId, int skip, int take, CancellationToken cancellationToken = default);
        Task<PagedResult<OverdueLineDto>> GetOverduePageAsync(int? projectId, int skip, int take, CancellationToken cancellationToken = default);
        /// <summary>Every line behind Net Profit — revenue, costs, and the fixed assets bought in the
        /// period. The signed amounts total to <c>FinancialSummaryDto.NetProfit</c>.</summary>
        Task<PagedResult<NetProfitLineDto>> GetNetProfitPageAsync(int? projectId, DateTime? from, DateTime? to, int skip, int take, int? accountId = null, bool unassigned = false, CancellationToken cancellationToken = default);
        /// <summary>Every component behind Total Expenses — ordinary expenses, commissions, rebates,
        /// customer credits, loan interest and fixed assets, each net of its reversals. The signed
        /// amounts total to <c>FinancialSummaryDto.TotalExpenses</c>, which the ordinary expense page
        /// cannot do because it holds only the first of those.</summary>
        Task<PagedResult<CostLineDto>> GetCostBreakdownPageAsync(int? projectId, DateTime? from, DateTime? to, int skip, int take, int? accountId = null, bool unassigned = false, CancellationToken cancellationToken = default);
        Task<PagedResult<AssetPurchaseLineDto>> GetAssetPurchasePageAsync(int? projectId, DateTime? from, DateTime? to, int skip, int take, int? assetAccountId = null, int? accountId = null, bool unassigned = false, CancellationToken cancellationToken = default);

        Task<ProfitAndLossDto> GetProfitAndLossAsync(int? projectId, DateTime? from, DateTime? to, CancellationToken cancellationToken = default);
        Task<TrialBalanceDto> GetTrialBalanceAsync(int? projectId, DateTime asAt, int monthsBack, CancellationToken cancellationToken = default);
        Task<BalanceSheetDto> GetBalanceSheetAsync(int? projectId, DateTime asAt, CancellationToken cancellationToken = default);
        Task<FinanceExportDto> ExportProfitAndLossAsync(int? projectId, DateTime? from, DateTime? to, CancellationToken cancellationToken = default);
        Task<FinanceExportDto> ExportTrialBalanceAsync(int? projectId, DateTime asAt, int monthsBack, CancellationToken cancellationToken = default);
        Task<FinanceExportDto> ExportBalanceSheetAsync(int? projectId, DateTime asAt, CancellationToken cancellationToken = default);

        // Manual revenue CRUD
        Task<ManualRevenueResponseDto> CreateManualRevenueAsync(CreateManualRevenueDto dto, int? adminUserId, FinanceAttachmentUpload? attachment = null, CancellationToken cancellationToken = default);
        Task<ManualRevenueResponseDto> UpdateManualRevenueAsync(int id, UpdateManualRevenueDto dto, FinanceAttachmentUpload? attachment = null, bool removeAttachment = false, CancellationToken cancellationToken = default);
        Task DeleteManualRevenueAsync(int id, string? concurrencyToken = null, CancellationToken cancellationToken = default);

        // Expense CRUD
        Task<ExpenseResponseDto> CreateExpenseAsync(CreateExpenseDto dto, int? adminUserId, FinanceAttachmentUpload? attachment = null, CancellationToken cancellationToken = default);
        Task<ExpenseResponseDto> UpdateExpenseAsync(int id, UpdateExpenseDto dto, FinanceAttachmentUpload? attachment = null, bool removeAttachment = false, CancellationToken cancellationToken = default);
        Task DeleteExpenseAsync(int id, string? concurrencyToken = null, CancellationToken cancellationToken = default);

        // Fixed-asset purchase CRUD. Same shape as expenses; the difference is where the value goes.
        Task<AssetPurchaseResponseDto> CreateAssetPurchaseAsync(CreateAssetPurchaseDto dto, int? adminUserId, FinanceAttachmentUpload? attachment = null, CancellationToken cancellationToken = default);
        Task<AssetPurchaseResponseDto> UpdateAssetPurchaseAsync(int id, UpdateAssetPurchaseDto dto, FinanceAttachmentUpload? attachment = null, bool removeAttachment = false, CancellationToken cancellationToken = default);
        Task DeleteAssetPurchaseAsync(int id, string concurrencyToken, CancellationToken cancellationToken = default);

        Task<FinanceAttachmentDownload> GetAttachmentAsync(FinanceRecordKind kind, int recordId, CancellationToken cancellationToken = default);
        Task RemoveAttachmentAsync(FinanceRecordKind kind, int recordId, CancellationToken cancellationToken = default);
    }
}
