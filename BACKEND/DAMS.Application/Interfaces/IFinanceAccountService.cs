using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Domain.Enums;

namespace DAMS.Application.Interfaces
{
    public interface IFinanceAccountService
    {
        Task<PagedResult<FinanceAccountResponseDto>> GetPageAsync(string? search, FinanceAccountType? type, string? holder, bool? isActive, int skip, int take, CancellationToken cancellationToken = default);
        Task<List<FinanceAccountOptionDto>> GetOptionsAsync(bool includeInactive, bool cashLikeOnly = true, CancellationToken cancellationToken = default);
        Task<FinanceAccountResponseDto> GetByIdAsync(int id, CancellationToken cancellationToken = default);
        Task<PagedResult<FinanceAccountTransactionDto>> GetTransactionsAsync(int id, int skip, int take, CancellationToken cancellationToken = default);
        Task<FinanceAccountsOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default);
        Task<FinanceAccountResponseDto> CreateAsync(CreateFinanceAccountDto dto, CancellationToken cancellationToken = default);
        Task<FinanceAccountResponseDto> UpdateAsync(int id, UpdateFinanceAccountDto dto, CancellationToken cancellationToken = default);
        Task<FinanceAccountResponseDto> SetActiveAsync(int id, bool isActive, string concurrencyToken, CancellationToken cancellationToken = default);
        Task DeleteUnusedAsync(int id, CancellationToken cancellationToken = default);
        Task EnsureSelectableAsync(int accountId, int? currentAccountId = null, CancellationToken cancellationToken = default);
        Task<List<FinanceAccountResponseDto>> SetupClientChartAsync(CancellationToken cancellationToken = default);
    }
}
