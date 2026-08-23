using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Domain.Enums;

namespace DAMS.Application.Interfaces
{
    public interface IFinanceAccountService
    {
        Task<PagedResult<FinanceAccountResponseDto>> GetPageAsync(string? search, FinanceAccountType? type, string? holder, bool? isActive, int skip, int take, CancellationToken cancellationToken = default);
        Task<List<FinanceAccountOptionDto>> GetOptionsAsync(bool includeInactive, bool cashLikeOnly = true, FinanceAccountType? type = null, CancellationToken cancellationToken = default);
        Task<FinanceAccountResponseDto> GetByIdAsync(int id, CancellationToken cancellationToken = default);
        Task<PagedResult<FinanceAccountTransactionDto>> GetTransactionsAsync(int id, int skip, int take, CancellationToken cancellationToken = default);
        Task<PagedResult<FinanceAccountTransactionDto>> GetTransactionsAsync(int id, int? projectId,
            DateTime? from, DateTime? to, int skip, int take, CancellationToken cancellationToken = default);
        Task<FinanceAccountLedgerSliceDto> GetTransactionLedgerSliceAsync(int id, int? projectId,
            DateTime from, DateTime to, int skip, int take, CancellationToken cancellationToken = default);
        Task<FinanceAccountsOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default);
        Task<FinanceAccountResponseDto> CreateAsync(CreateFinanceAccountDto dto, CancellationToken cancellationToken = default);
        Task<FinanceAccountResponseDto> UpdateAsync(int id, UpdateFinanceAccountDto dto, CancellationToken cancellationToken = default);
        Task<FinanceAccountResponseDto> SetActiveAsync(int id, bool isActive, string concurrencyToken, CancellationToken cancellationToken = default);
        Task DeleteUnusedAsync(int id, CancellationToken cancellationToken = default);
        Task EnsureSelectableAsync(int accountId, int? currentAccountId = null, CancellationToken cancellationToken = default);
        Task EnsureExpenseSourceAsync(int accountId, int? currentAccountId = null, CancellationToken cancellationToken = default);

        /// <summary>Validates that an account can receive a capitalised purchase — active, and typed
        /// FixedAsset or WorkInProgress.</summary>
        Task EnsureAssetAccountAsync(int accountId, int? currentAccountId = null, CancellationToken cancellationToken = default);
        Task<List<FinanceAccountResponseDto>> SetupClientChartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Resolves the id of the given system account, creating it from the canonical
        /// definition (or adopting a matching-named account) if it does not exist yet. Safe to
        /// call from inside an ambient transaction — it does not commit anything itself.
        /// </summary>
        Task<int> EnsureSystemAccountAsync(FinanceSystemAccountRole role, CancellationToken cancellationToken = default);
    }
}
