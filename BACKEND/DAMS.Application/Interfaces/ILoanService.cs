using DAMS.Application.DTOs.FinanceDtos;

namespace DAMS.Application.Interfaces
{
    public interface ILoanService
    {
        Task<List<LoanDto>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken = default);
        Task<List<LoanAccountOptionDto>> GetAccountOptionsAsync(bool includeInactive, CancellationToken cancellationToken = default);
        Task<LoanDto> CreateAsync(SaveLoanDto dto, CancellationToken cancellationToken = default);
        Task<LoanDto> UpdateAsync(int id, SaveLoanDto dto, CancellationToken cancellationToken = default);
        Task<LoanStatementDto> GetStatementAsync(int id, int skip, int take, CancellationToken cancellationToken = default);
        Task<LoanTransactionDto> RecordTransactionAsync(int loanId, SaveLoanTransactionDto dto, int? userId, FinanceAttachmentUpload? attachment = null, CancellationToken cancellationToken = default);
        Task<LoanTransactionDto> UpdateTransactionAsync(int loanId, int transactionId, SaveLoanTransactionDto dto, int? userId, FinanceAttachmentUpload? attachment = null, bool removeAttachment = false, CancellationToken cancellationToken = default);
        Task DeleteTransactionAsync(int loanId, int transactionId, string concurrencyToken, CancellationToken cancellationToken = default);
        Task<FinanceAttachmentDownload> GetTransactionAttachmentAsync(int loanId, int transactionId, CancellationToken cancellationToken = default);
        Task RemoveTransactionAttachmentAsync(int loanId, int transactionId, CancellationToken cancellationToken = default);
    }
}
