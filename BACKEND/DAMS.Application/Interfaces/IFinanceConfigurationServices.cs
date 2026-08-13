using DAMS.Application.DTOs.FinanceDtos;

namespace DAMS.Application.Interfaces
{
    public interface IRevenueCategoryService
    {
        Task<List<RevenueCategoryDto>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken = default);
        Task<RevenueCategoryDto> GetByIdAsync(int id, CancellationToken cancellationToken = default);
        Task<RevenueCategoryDto> CreateAsync(SaveRevenueCategoryDto dto, int? userId, CancellationToken cancellationToken = default);
        Task<RevenueCategoryDto> UpdateAsync(int id, SaveRevenueCategoryDto dto, CancellationToken cancellationToken = default);
        Task<RevenueCategoryDto?> DeleteAsync(int id, CancellationToken cancellationToken = default);
    }

    public interface IOpeningBalanceService
    {
        Task<OpeningBalanceSetDto?> GetCurrentAsync(CancellationToken cancellationToken = default);
        Task<OpeningBalanceSetDto> CreateAsync(DateTime asAtDate, int? userId, CancellationToken cancellationToken = default);
        Task<OpeningBalanceSetDto> SaveAsync(int id, SaveOpeningBalanceSetDto dto, int? userId, CancellationToken cancellationToken = default);
        Task<OpeningBalanceSetDto> CommitAsync(int id, string concurrencyToken, int? userId, CancellationToken cancellationToken = default);
        Task<OpeningBalanceSetDto> ReopenAsync(int id, ReopenOpeningBalanceSetDto dto, int? userId, CancellationToken cancellationToken = default);
    }

    public interface ICapitalPartnerService
    {
        Task<List<CapitalPartnerDto>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken = default);
        Task<CapitalPartnerDto> CreateAsync(SaveCapitalPartnerDto dto, CancellationToken cancellationToken = default);
        Task<CapitalPartnerDto> UpdateAsync(int id, SaveCapitalPartnerDto dto, CancellationToken cancellationToken = default);
        Task<List<CapitalPartnerDto>> UpdateSharesAsync(SaveCapitalPartnerSharesDto dto, CancellationToken cancellationToken = default);
        Task<CapitalPartnerStatementDto> GetStatementAsync(int id, DateTime? from, DateTime? to, CancellationToken cancellationToken = default);
        Task<CapitalTransactionDto> RecordTransactionAsync(int id, SaveCapitalTransactionDto dto, int? userId, CancellationToken cancellationToken = default);
    }
}
