using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.DTOs.WhtDtos;

namespace DAMS.Application.Interfaces
{
    /// <summary>Admin-managed expense heads and their withholding rates.</summary>
    public interface IExpenseCategoryService
    {
        Task<List<ExpenseCategoryDto>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken = default);
        Task<ExpenseCategoryDto> GetByIdAsync(int id, CancellationToken cancellationToken = default);
        Task<ExpenseCategoryDto> CreateAsync(SaveExpenseCategoryDto dto, int? adminUserId, CancellationToken cancellationToken = default);
        Task<ExpenseCategoryDto> UpdateAsync(int id, SaveExpenseCategoryDto dto, CancellationToken cancellationToken = default);

        /// <summary>Retires a category. Soft-deletes when expenses reference it, because the rate
        /// it carried is part of filed tax history.</summary>
        Task<ExpenseCategoryDto?> DeleteAsync(int id, CancellationToken cancellationToken = default);
    }

    public interface IVendorService
    {
        Task<PagedResult<VendorDto>> GetPageAsync(string? search, bool activeOnly, int skip, int take, CancellationToken cancellationToken = default);
        Task<List<VendorOptionDto>> GetOptionsAsync(bool includeInactive, CancellationToken cancellationToken = default);
        Task<VendorDto> GetByIdAsync(int id, CancellationToken cancellationToken = default);
        Task<VendorDto> CreateAsync(SaveVendorDto dto, int? adminUserId, CancellationToken cancellationToken = default);
        Task<VendorDto> UpdateAsync(int id, SaveVendorDto dto, CancellationToken cancellationToken = default);

        /// <summary>Year-to-date gross and withheld for a vendor, broken down by tax section —
        /// the detail behind the threshold notice on the expense form.</summary>
        Task<List<VendorYtdLineDto>> GetYearToDateAsync(int vendorId, DateTime? asOf, CancellationToken cancellationToken = default);
    }

    /// <summary>Withholding calculation, settings, reporting and FBR deposits.</summary>
    public interface IWhtService
    {
        Task<WhtCalculationResultDto> CalculateAsync(WhtCalculationRequestDto request, CancellationToken cancellationToken = default);

        /// <summary>Resolves and persists the withholding fields onto an expense before it is
        /// saved. The single point where an <see cref="Domain.Entities.Expense"/> gets its tax.</summary>
        Task ApplyToExpenseAsync(Domain.Entities.Expense expense, decimal? requestedRate, decimal? requestedAmount, string? overrideReason, CancellationToken cancellationToken = default);

        /// <summary>The same, for a fixed-asset purchase. Shares the expense rate table and the
        /// per-vendor annual allowance, because to FBR both are money paid to a supplier under a
        /// tax section — only the accounting treatment differs.</summary>
        Task ApplyToAssetPurchaseAsync(Domain.Entities.AssetPurchase purchase, decimal? requestedRate, decimal? requestedAmount, string? overrideReason, CancellationToken cancellationToken = default);

        /// <summary>
        /// Refuses a change to a source record that would drop Tax Payable below zero on any date —
        /// the source-record half of the "Tax Payable is never negative" invariant that deposit
        /// creation guards from the other side. Call it inside the same transaction as the change
        /// it is checking, and before the change is saved.
        /// <para>
        /// The change is expressed as a replacement — what the record withheld and when, against
        /// what it will withhold and when — because Tax Payable is reported AS AT a date. Moving a
        /// record's date without touching its amount changes nothing all-time yet can strand a
        /// deposit in a month that no longer has the withholding behind it, so a signed all-time
        /// total cannot express the question. Pass a null date with zero tax for the side that does
        /// not exist: creating a record has no previous side, deleting one has no new side.
        /// </para>
        /// </summary>
        Task EnsureDepositsStayCoveredAsync(
            DateTime? previousDate, decimal previousWithheld,
            DateTime? newDate, decimal newWithheld,
            CancellationToken cancellationToken = default);

        Task<FinanceSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);
        Task<FinanceSettingsDto> UpdateSettingsAsync(SaveFinanceSettingsDto dto, string? actorName, CancellationToken cancellationToken = default);

        Task<WhtPayableSummaryDto> GetPayableSummaryAsync(DateTime? from, DateTime? to, CancellationToken cancellationToken = default);
        Task<List<WhtVendorLineDto>> GetByVendorAsync(DateTime? from, DateTime? to, CancellationToken cancellationToken = default);

        /// <summary>The s.165 statement as CSV. Excel opens it directly; no spreadsheet library
        /// is pulled into the build for a report that is one flat table.</summary>
        Task<(string FileName, byte[] Content)> ExportAsync(DateTime? from, DateTime? to, CancellationToken cancellationToken = default);

        Task<List<WhtDepositDto>> GetDepositsAsync(DateTime? from, DateTime? to, CancellationToken cancellationToken = default);
        Task<WhtDepositDto> CreateDepositAsync(SaveWhtDepositDto dto, int? adminUserId, CancellationToken cancellationToken = default);
        Task<WhtDepositDto> UpdateDepositAsync(int id, SaveWhtDepositDto dto, CancellationToken cancellationToken = default);
        Task DeleteDepositAsync(int id, string? concurrencyToken = null, CancellationToken cancellationToken = default);
    }
}
