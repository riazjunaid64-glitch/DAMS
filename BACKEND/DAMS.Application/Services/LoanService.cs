using System.Data;
using DAMS.Application.Common;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DAMS.Application.Services
{
    public sealed class LoanService : ILoanService
    {
        private const decimal MaximumAmount = 999_999_999_999_999.99m;
        private readonly AppDbContext _context;
        private readonly IFinanceAccountService _accounts;
        private readonly IFinanceAttachmentWriter _attachments;

        public LoanService(AppDbContext context, IFinanceAccountService accounts, IFinanceAttachmentWriter attachments)
        {
            _context = context;
            _accounts = accounts;
            _attachments = attachments;
        }

        public async Task<List<LoanDto>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken = default)
        {
            var loans = await LoanAggregates(_context.Loans.AsNoTracking().Where(l => includeInactive || l.IsActive))
                .OrderByDescending(l => l.IsActive).ThenBy(l => l.Name).ToListAsync(cancellationToken);
            return loans.Select(MapLoan).ToList();
        }

        public async Task<List<LoanAccountOptionDto>> GetAccountOptionsAsync(bool includeInactive, CancellationToken cancellationToken = default) =>
            await _context.FinanceAccounts.AsNoTracking()
                .Where(a => a.Type == FinanceAccountType.Liability
                    && a.SystemRole == FinanceSystemAccountRole.None
                    && (includeInactive || a.IsActive))
                .OrderByDescending(a => a.IsActive).ThenBy(a => a.DisplayOrder).ThenBy(a => a.Name)
                .Select(a => new LoanAccountOptionDto
                {
                    Id = a.Id, Name = a.Name, AccountHolderName = a.AccountHolderName, IsActive = a.IsActive,
                    LinkedLoanId = a.Loans.Select(l => (int?)l.Id).SingleOrDefault(),
                    LinkedLoanName = a.Loans.Select(l => l.Name).SingleOrDefault()
                }).ToListAsync(cancellationToken);

        public async Task<LoanDto> CreateAsync(SaveLoanDto dto, CancellationToken cancellationToken = default)
        {
            ValidateLoan(dto);
            await ValidateLoanAccountAsync(dto.FinanceAccountId, null, requireActive: true, cancellationToken);
            await EnsureNameUniqueAsync(dto.Name, null, cancellationToken);
            if (!dto.IsActive)
            {
                var opening = await _context.FinanceAccounts.AsNoTracking().Where(a => a.Id == dto.FinanceAccountId)
                    .Select(a => a.OpeningBalance).SingleAsync(cancellationToken);
                if (opening != 0m)
                    throw new InvalidOperationException("A loan with principal outstanding cannot be created as inactive.");
            }
            var loan = new Loan
            {
                Name = dto.Name.Trim(), LenderName = Clean(dto.LenderName), FinanceAccountId = dto.FinanceAccountId,
                IsActive = dto.IsActive, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            };
            _context.Loans.Add(loan);
            await _context.SaveChangesAsync(cancellationToken);
            return await GetLoanAsync(loan.Id, cancellationToken);
        }

        public Task<LoanDto> UpdateAsync(int id, SaveLoanDto dto, CancellationToken cancellationToken = default) =>
            ExecuteResilientlyAsync(() => UpdateCoreAsync(id, dto, cancellationToken));

        private async Task<LoanDto> UpdateCoreAsync(int id, SaveLoanDto dto, CancellationToken cancellationToken)
        {
            ValidateLoan(dto);
            await using var guard = await BeginGuardAsync(cancellationToken);
            var committed = false;
            try
            {
                var loan = await _context.Loans.SingleOrDefaultAsync(l => l.Id == id, cancellationToken)
                    ?? throw new InvalidOperationException("Loan not found.");
                ApplyToken(loan, dto.ConcurrencyToken);
                await ValidateLoanAccountAsync(dto.FinanceAccountId, id, requireActive: dto.IsActive, cancellationToken);
                await EnsureNameUniqueAsync(dto.Name, id, cancellationToken);
                var balance = await EnsurePrincipalNeverNegativeAsync(id, null, dto.FinanceAccountId, cancellationToken);
                if (!dto.IsActive)
                {
                    if (balance != 0m)
                        throw new InvalidOperationException("A loan can only be made inactive after its principal balance reaches zero.");
                }
                loan.Name = dto.Name.Trim();
                loan.LenderName = Clean(dto.LenderName);
                loan.FinanceAccountId = dto.FinanceAccountId;
                loan.IsActive = dto.IsActive;
                loan.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
                if (guard != null) await guard.CommitAsync(cancellationToken);
                committed = true;
                return await GetLoanAsync(id, cancellationToken);
            }
            catch
            {
                if (guard != null && !committed) await guard.RollbackAsync(cancellationToken);
                throw;
            }
        }

        public async Task<LoanStatementDto> GetStatementAsync(int id, int skip, int take, CancellationToken cancellationToken = default)
        {
            skip = Math.Max(0, skip);
            take = Math.Clamp(take, 1, 200);
            var loan = await GetLoanAsync(id, cancellationToken);
            var query = _context.LoanTransactions.AsNoTracking().Where(t => t.LoanId == id);
            // No time-of-day is entered. On the same date, show drawdowns before repayments in
            // chronological order (therefore repayments first in this newest-first response),
            // matching the day-level non-negative balance invariant.
            var ordered = query.OrderByDescending(t => t.Date).ThenByDescending(t => t.Type)
                .ThenByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id);
            var skippedMovement = skip == 0 ? 0m : await ordered.Take(skip)
                .SumAsync(t => t.Type == LoanTransactionType.Drawdown ? t.PrincipalAmount : -t.PrincipalAmount, cancellationToken);
            var rows = await ordered.Skip(skip).Take(take + 1)
                .Include(t => t.FinanceAccount).Include(t => t.Attachment).ToListAsync(cancellationToken);
            var running = loan.CurrentBalance - skippedMovement;
            var items = new List<LoanTransactionDto>();
            foreach (var transaction in rows.Take(take))
            {
                items.Add(MapTransaction(transaction, running));
                running -= SignedPrincipal(transaction);
            }
            return new LoanStatementDto { Loan = loan, Items = items, HasMore = rows.Count > take };
        }

        public async Task<LoanTransactionDto> RecordTransactionAsync(int loanId, SaveLoanTransactionDto dto, int? userId,
            FinanceAttachmentUpload? attachment = null, CancellationToken cancellationToken = default)
        {
            // Generated outside the retry delegate: if SQL reports a transient failure after the
            // commit actually reached the server, the retry finds and returns the first row instead
            // of recording the same drawdown or repayment twice.
            var operationId = Guid.NewGuid();
            // Written outside the retry delegate too, and for a related reason: the upload is a
            // forward-only stream, so a second attempt would find it already consumed and store an
            // empty file. Stored once, then only its metadata crosses the retry boundary.
            var stored = attachment == null ? null : await _attachments.StoreAsync(attachment, cancellationToken);
            try
            {
                return await ExecuteResilientlyAsync(() => RecordTransactionCoreAsync(loanId, dto, userId, operationId, stored, cancellationToken));
            }
            catch
            {
                await _attachments.DiscardAsync(stored?.StoredFileName);
                throw;
            }
        }

        private async Task<LoanTransactionDto> RecordTransactionCoreAsync(int loanId, SaveLoanTransactionDto dto, int? userId,
            Guid operationId, StoredFinanceAttachment? stored, CancellationToken cancellationToken)
        {
            ValidateTransaction(dto);
            await using var guard = await BeginGuardAsync(cancellationToken);
            var committed = false;
            try
            {
                var loan = await _context.Loans.AsNoTracking().Include(l => l.FinanceAccount)
                    .SingleOrDefaultAsync(l => l.Id == loanId, cancellationToken)
                    ?? throw new InvalidOperationException("Loan not found.");
                if (!loan.IsActive) throw new InvalidOperationException("Transactions cannot be recorded for an inactive loan.");
                if (!loan.FinanceAccount.IsActive || loan.FinanceAccount.Type != FinanceAccountType.Liability
                    || loan.FinanceAccount.SystemRole != FinanceSystemAccountRole.None)
                    throw new InvalidOperationException("The linked loan liability account must be active.");
                var existingId = await _context.LoanTransactions.AsNoTracking()
                    .Where(t => t.OperationId == operationId).Select(t => (int?)t.Id)
                    .SingleOrDefaultAsync(cancellationToken);
                if (existingId.HasValue)
                {
                    if (guard != null) await guard.CommitAsync(cancellationToken);
                    committed = true;
                    return await GetTransactionAsync(existingId.Value, cancellationToken);
                }
                // A retry after a rolled-back SaveChanges can leave the first attempt tracked as
                // Added or Unchanged. It is not in SQL, so detach it before rebuilding this attempt.
                foreach (var pending in _context.ChangeTracker.Entries<LoanTransaction>()
                    .Where(e => e.Entity.OperationId == operationId).ToList())
                    pending.State = EntityState.Detached;
                await ValidateTransactionDateAsync(dto.Date, cancellationToken);
                await _accounts.EnsureSelectableAsync(dto.FinanceAccountId, null, cancellationToken);
                var transaction = new LoanTransaction
                {
                    OperationId = operationId, LoanId = loanId, Type = dto.Type, PrincipalAmount = Money(dto.PrincipalAmount),
                    InterestAmount = Money(dto.InterestAmount), Date = dto.Date.Date,
                    FinanceAccountId = dto.FinanceAccountId, Reference = Clean(dto.Reference), Note = Clean(dto.Note),
                    CreatedByUserId = userId, UpdatedByUserId = userId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
                };
                if (stored != null)
                {
                    transaction.Attachment = new FinanceAttachment();
                    stored.ApplyTo(transaction.Attachment);
                }
                await EnsurePrincipalNeverNegativeAsync(loanId, transaction, null, cancellationToken);
                _context.LoanTransactions.Add(transaction);
                await _context.SaveChangesAsync(cancellationToken);
                if (guard != null) await guard.CommitAsync(cancellationToken);
                committed = true;
                return await GetTransactionAsync(transaction.Id, cancellationToken);
            }
            catch
            {
                if (guard != null && !committed) await guard.RollbackAsync(cancellationToken);
                throw;
            }
        }

        public async Task<LoanTransactionDto> UpdateTransactionAsync(int loanId, int transactionId, SaveLoanTransactionDto dto, int? userId,
            FinanceAttachmentUpload? attachment = null, bool removeAttachment = false, CancellationToken cancellationToken = default)
        {
            if (attachment != null && removeAttachment)
                throw new InvalidOperationException("Choose either a replacement attachment or removing the existing one, not both.");
            var stored = attachment == null ? null : await _attachments.StoreAsync(attachment, cancellationToken);
            string? replaced;
            try
            {
                LoanTransactionDto result;
                (result, replaced) = await ExecuteResilientlyAsync(() =>
                    UpdateTransactionCoreAsync(loanId, transactionId, dto, userId, stored, removeAttachment, cancellationToken));
                // Only once the row is safely saved: a failed save leaves the record still pointing
                // at the old file, so deleting it before the commit would break a movement that was
                // never changed.
                await _attachments.ForgetAsync(replaced);
                return result;
            }
            catch
            {
                await _attachments.DiscardAsync(stored?.StoredFileName);
                throw;
            }
        }

        private async Task<(LoanTransactionDto Transaction, string? ReplacedFile)> UpdateTransactionCoreAsync(
            int loanId, int transactionId, SaveLoanTransactionDto dto, int? userId,
            StoredFinanceAttachment? stored, bool removeAttachment, CancellationToken cancellationToken)
        {
            ValidateTransaction(dto);
            await using var guard = await BeginGuardAsync(cancellationToken);
            var committed = false;
            try
            {
                var loan = await _context.Loans.AsNoTracking().SingleOrDefaultAsync(l => l.Id == loanId, cancellationToken)
                    ?? throw new InvalidOperationException("Loan not found.");
                var transaction = await _context.LoanTransactions.Include(t => t.Attachment)
                    .SingleOrDefaultAsync(t => t.Id == transactionId && t.LoanId == loanId, cancellationToken)
                    ?? throw new InvalidOperationException("Loan transaction not found.");
                ApplyToken(transaction, dto.ConcurrencyToken);
                await ValidateTransactionDateAsync(dto.Date, cancellationToken);
                await _accounts.EnsureSelectableAsync(dto.FinanceAccountId, transaction.FinanceAccountId, cancellationToken);
                // Validate a detached prospective shape first. If an overpayment is rejected, the
                // tracked row remains untouched even when a caller deliberately reuses this service
                // and DbContext after handling the validation error.
                var candidate = new LoanTransaction
                {
                    Type = dto.Type, PrincipalAmount = Money(dto.PrincipalAmount),
                    InterestAmount = Money(dto.InterestAmount), Date = dto.Date.Date
                };
                var resultingBalance = await EnsurePrincipalNeverNegativeAsync(
                    loan.Id, candidate, null, cancellationToken, transaction.Id);
                if (!loan.IsActive && resultingBalance != 0m)
                    throw new InvalidOperationException(
                        "This correction would leave principal outstanding on an inactive loan. Reactivate the loan first.");
                transaction.Type = candidate.Type;
                transaction.PrincipalAmount = candidate.PrincipalAmount;
                transaction.InterestAmount = candidate.InterestAmount;
                transaction.Date = candidate.Date;
                transaction.FinanceAccountId = dto.FinanceAccountId;
                transaction.Reference = Clean(dto.Reference);
                transaction.Note = Clean(dto.Note);
                transaction.UpdatedByUserId = userId;
                transaction.UpdatedAt = DateTime.UtcNow;
                var replaced = transaction.Attachment?.StoredFileName;
                if (stored != null)
                {
                    transaction.Attachment ??= new FinanceAttachment { LoanTransactionId = transaction.Id };
                    stored.ApplyTo(transaction.Attachment);
                }
                else if (removeAttachment && transaction.Attachment != null)
                {
                    _context.FinanceAttachments.Remove(transaction.Attachment);
                    transaction.Attachment = null;
                }
                else
                {
                    // Nothing asked for: the existing file stays, so nothing is queued for deletion.
                    replaced = null;
                }
                await _context.SaveChangesAsync(cancellationToken);
                if (guard != null) await guard.CommitAsync(cancellationToken);
                committed = true;
                return (await GetTransactionAsync(transaction.Id, cancellationToken), replaced);
            }
            catch
            {
                if (guard != null && !committed) await guard.RollbackAsync(cancellationToken);
                throw;
            }
        }

        public async Task DeleteTransactionAsync(int loanId, int transactionId, string concurrencyToken, CancellationToken cancellationToken = default) =>
            await ExecuteResilientlyAsync(async () =>
            {
                await DeleteTransactionCoreAsync(loanId, transactionId, concurrencyToken, cancellationToken);
                return true;
            });

        private async Task DeleteTransactionCoreAsync(int loanId, int transactionId, string concurrencyToken, CancellationToken cancellationToken)
        {
            await using var guard = await BeginGuardAsync(cancellationToken);
            var committed = false;
            try
            {
                var transaction = await _context.LoanTransactions.Include(t => t.Attachment)
                    .SingleOrDefaultAsync(t => t.Id == transactionId && t.LoanId == loanId, cancellationToken)
                    ?? throw new InvalidOperationException("Loan transaction not found.");
                ApplyToken(transaction, concurrencyToken);
                var loanIsActive = await _context.Loans.AsNoTracking().Where(l => l.Id == loanId)
                    .Select(l => l.IsActive).SingleAsync(cancellationToken);
                var resultingBalance = await EnsurePrincipalNeverNegativeAsync(
                    loanId, null, null, cancellationToken, transaction.Id);
                if (!loanIsActive && resultingBalance != 0m)
                    throw new InvalidOperationException(
                        "Deleting this movement would leave principal outstanding on an inactive loan. Reactivate the loan first.");
                var orphanedFile = transaction.Attachment?.StoredFileName;
                _context.LoanTransactions.Remove(transaction);
                await _context.SaveChangesAsync(cancellationToken);
                if (guard != null) await guard.CommitAsync(cancellationToken);
                committed = true;
                // After the commit: a rolled-back delete must leave the evidence where it was.
                await _attachments.ForgetAsync(orphanedFile);
            }
            catch
            {
                if (guard != null && !committed) await guard.RollbackAsync(cancellationToken);
                throw;
            }
        }

        private async Task<LoanDto> GetLoanAsync(int id, CancellationToken cancellationToken)
        {
            var loan = await LoanAggregates(_context.Loans.AsNoTracking().Where(l => l.Id == id))
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("Loan not found.");
            return MapLoan(loan);
        }

        public async Task<FinanceAttachmentDownload> GetTransactionAttachmentAsync(
            int loanId, int transactionId, CancellationToken cancellationToken = default)
        {
            var attachment = await _context.LoanTransactions.AsNoTracking()
                .Where(t => t.Id == transactionId && t.LoanId == loanId && t.Attachment != null)
                .Select(t => t.Attachment!)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new FileNotFoundException("This loan movement does not have an attachment.");
            var content = await _attachments.OpenAsync(attachment.StoredFileName, cancellationToken)
                ?? throw new FileNotFoundException("The attachment file is missing from storage. Please replace it from the edit form.");
            return new FinanceAttachmentDownload
            {
                Content = content,
                FileName = attachment.OriginalFileName,
                ContentType = attachment.ContentType
            };
        }

        public async Task RemoveTransactionAttachmentAsync(
            int loanId, int transactionId, CancellationToken cancellationToken = default)
        {
            var transaction = await _context.LoanTransactions.Include(t => t.Attachment)
                .SingleOrDefaultAsync(t => t.Id == transactionId && t.LoanId == loanId, cancellationToken)
                ?? throw new InvalidOperationException("Loan transaction not found.");
            if (transaction.Attachment == null) return;
            var storedFileName = transaction.Attachment.StoredFileName;
            _context.FinanceAttachments.Remove(transaction.Attachment);
            await _context.SaveChangesAsync(cancellationToken);
            await _attachments.ForgetAsync(storedFileName);
        }

        private async Task<LoanTransactionDto> GetTransactionAsync(int id, CancellationToken cancellationToken)
        {
            var transaction = await _context.LoanTransactions.AsNoTracking()
                .Include(t => t.FinanceAccount).Include(t => t.Attachment)
                .SingleAsync(t => t.Id == id, cancellationToken);
            var balance = await _context.Loans.AsNoTracking().Where(l => l.Id == transaction.LoanId)
                .Select(l => l.FinanceAccount.OpeningBalance + l.Transactions
                    .Where(t => t.Date < transaction.Date
                        || (t.Date == transaction.Date && ((int)t.Type < (int)transaction.Type
                            || (t.Type == transaction.Type && (t.CreatedAt < transaction.CreatedAt
                                || (t.CreatedAt == transaction.CreatedAt && t.Id <= transaction.Id))))))
                    .Sum(t => t.Type == LoanTransactionType.Drawdown ? t.PrincipalAmount : -t.PrincipalAmount))
                .SingleAsync(cancellationToken);
            return MapTransaction(transaction, Money(balance));
        }

        private async Task<decimal> EnsurePrincipalNeverNegativeAsync(int loanId, LoanTransaction? candidate, int? financeAccountId,
            CancellationToken cancellationToken, int? excludingTransactionId = null)
        {
            var opening = await _context.Loans.AsNoTracking().Where(l => l.Id == loanId)
                .Select(l => financeAccountId.HasValue
                    ? _context.FinanceAccounts.Where(a => a.Id == financeAccountId.Value).Select(a => a.OpeningBalance).Single()
                    : l.FinanceAccount.OpeningBalance)
                .SingleOrDefaultAsync(cancellationToken);
            if (opening < 0m) throw new InvalidOperationException("A loan liability cannot have a negative opening balance.");
            var rows = await _context.LoanTransactions.AsNoTracking()
                .Where(t => t.LoanId == loanId && (!excludingTransactionId.HasValue || t.Id != excludingTransactionId.Value))
                .Select(t => new PrincipalMovement(t.Date, t.Type == LoanTransactionType.Drawdown ? t.PrincipalAmount : -t.PrincipalAmount))
                .ToListAsync(cancellationToken);
            if (candidate != null) rows.Add(new PrincipalMovement(candidate.Date.Date,
                candidate.Type == LoanTransactionType.Drawdown ? candidate.PrincipalAmount : -candidate.PrincipalAmount));
            var running = opening;
            foreach (var day in rows.GroupBy(t => t.Date.Date).OrderBy(g => g.Key))
            {
                running += day.Sum(t => t.Amount);
                if (running < 0m)
                    throw new InvalidOperationException($"Principal repayments exceed the amount owed on {day.Key:dd MMM yyyy}.");
            }
            return Money(running);
        }

        private async Task ValidateLoanAccountAsync(int accountId, int? loanId, bool requireActive, CancellationToken cancellationToken)
        {
            var account = await _context.FinanceAccounts.AsNoTracking()
                .Where(a => a.Id == accountId).Select(a => new { a.Type, a.SystemRole, a.IsActive, a.OpeningBalance })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("Selected loan account does not exist.");
            if (account.Type != FinanceAccountType.Liability || account.SystemRole != FinanceSystemAccountRole.None)
                throw new InvalidOperationException("A loan must be linked to a regular Liability account.");
            if (requireActive && !account.IsActive) throw new InvalidOperationException("Selected loan account is inactive.");
            if (account.OpeningBalance < 0m) throw new InvalidOperationException("A loan liability cannot have a negative opening balance.");
            if (await _context.Loans.AnyAsync(l => l.FinanceAccountId == accountId && (!loanId.HasValue || l.Id != loanId.Value), cancellationToken))
                throw new InvalidOperationException("This liability account is already linked to another loan.");
        }

        // The bounds a loan movement has always enforced. They now live in FinanceDateRules, which
        // is where expenses, revenue, purchases, receipts and FBR deposits read them from too — one
        // rule and one wording instead of a copy per service.
        private Task ValidateTransactionDateAsync(DateTime date, CancellationToken cancellationToken) =>
            FinanceDateRules.EnsureAsync(_context, date, "Transaction date", cancellationToken);

        private async Task EnsureNameUniqueAsync(string name, int? excludingId, CancellationToken cancellationToken)
        {
            var clean = name.Trim();
            if (await _context.Loans.AnyAsync(l => l.Name == clean && (!excludingId.HasValue || l.Id != excludingId.Value), cancellationToken))
                throw new InvalidOperationException("A loan with this name already exists.");
        }

        private async Task<IDbContextTransaction?> BeginGuardAsync(CancellationToken cancellationToken)
        {
            if (!_context.Database.IsRelational() || _context.Database.CurrentTransaction != null) return null;
            return await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        }

        private Task<T> ExecuteResilientlyAsync<T>(Func<Task<T>> operation) =>
            _context.Database.CreateExecutionStrategy().ExecuteAsync(operation);

        private void ApplyToken(Loan loan, string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                if (loan.RowVersion.Length == 0) return;
                throw new InvalidOperationException("The loan version is missing. Refresh and try again.");
            }
            try { _context.Entry(loan).Property(l => l.RowVersion).OriginalValue = Convert.FromBase64String(token); }
            catch (FormatException) { throw new InvalidOperationException("The loan version is invalid. Refresh and try again."); }
        }

        private void ApplyToken(LoanTransaction transaction, string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                if (transaction.RowVersion.Length == 0) return;
                throw new InvalidOperationException("The transaction version is missing. Refresh and try again.");
            }
            try { _context.Entry(transaction).Property(t => t.RowVersion).OriginalValue = Convert.FromBase64String(token); }
            catch (FormatException) { throw new InvalidOperationException("The transaction version is invalid. Refresh and try again."); }
        }

        private static void ValidateLoan(SaveLoanDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) throw new InvalidOperationException("Loan name is required.");
            if (dto.Name.Trim().Length > 200) throw new InvalidOperationException("Loan name cannot exceed 200 characters.");
            if (dto.LenderName?.Trim().Length > 200) throw new InvalidOperationException("Lender name cannot exceed 200 characters.");
            if (dto.FinanceAccountId <= 0) throw new InvalidOperationException("Select the liability account for this loan.");
        }

        private static void ValidateTransaction(SaveLoanTransactionDto dto)
        {
            if (!Enum.IsDefined(dto.Type)) throw new InvalidOperationException("Loan transaction type is invalid.");
            if (dto.Date == default) throw new InvalidOperationException("Transaction date is required.");
            if (dto.FinanceAccountId <= 0) throw new InvalidOperationException("Select the cash or bank account used.");
            ValidateMoney(dto.PrincipalAmount, "Principal amount");
            ValidateMoney(dto.InterestAmount, "Interest amount");
            if (dto.Type == LoanTransactionType.Drawdown && (dto.PrincipalAmount <= 0m || dto.InterestAmount != 0m))
                throw new InvalidOperationException("A drawdown requires principal greater than zero and cannot include interest.");
            if (dto.Type == LoanTransactionType.Repayment && dto.PrincipalAmount == 0m && dto.InterestAmount == 0m)
                throw new InvalidOperationException("A repayment must include principal, interest, or both.");
            if (dto.Reference?.Trim().Length > 200) throw new InvalidOperationException("Reference cannot exceed 200 characters.");
            if (dto.Note?.Trim().Length > 1000) throw new InvalidOperationException("Note cannot exceed 1000 characters.");
        }

        private static void ValidateMoney(decimal value, string field)
        {
            if (value < 0m) throw new InvalidOperationException($"{field} cannot be negative.");
            if (value > MaximumAmount) throw new InvalidOperationException($"{field} is outside the supported range.");
            if (value != Money(value)) throw new InvalidOperationException($"{field} cannot have more than two decimal places.");
        }

        private static IQueryable<LoanAggregate> LoanAggregates(IQueryable<Loan> query) => query.Select(l => new LoanAggregate
        {
            Id = l.Id, Name = l.Name, LenderName = l.LenderName, FinanceAccountId = l.FinanceAccountId,
            FinanceAccountName = l.FinanceAccount.Name, IsActive = l.IsActive,
            OpeningBalance = l.FinanceAccount.OpeningBalance,
            DrawnPrincipal = l.Transactions.Where(t => t.Type == LoanTransactionType.Drawdown)
                .Sum(t => (decimal?)t.PrincipalAmount) ?? 0m,
            RepaidPrincipal = l.Transactions.Where(t => t.Type == LoanTransactionType.Repayment)
                .Sum(t => (decimal?)t.PrincipalAmount) ?? 0m,
            InterestPaid = l.Transactions.Where(t => t.Type == LoanTransactionType.Repayment)
                .Sum(t => (decimal?)t.InterestAmount) ?? 0m,
            TransactionCount = l.Transactions.Count,
            RowVersion = l.RowVersion
        });

        private static LoanDto MapLoan(LoanAggregate loan)
        {
            return new LoanDto
            {
                Id = loan.Id, Name = loan.Name, LenderName = loan.LenderName,
                FinanceAccountId = loan.FinanceAccountId, FinanceAccountName = loan.FinanceAccountName,
                IsActive = loan.IsActive, OpeningBalance = Money(loan.OpeningBalance), DrawnPrincipal = Money(loan.DrawnPrincipal),
                RepaidPrincipal = Money(loan.RepaidPrincipal), InterestPaid = Money(loan.InterestPaid),
                CurrentBalance = Money(loan.OpeningBalance + loan.DrawnPrincipal - loan.RepaidPrincipal), TransactionCount = loan.TransactionCount,
                ConcurrencyToken = Convert.ToBase64String(loan.RowVersion)
            };
        }

        private static LoanTransactionDto MapTransaction(LoanTransaction transaction, decimal runningBalance) => new()
        {
            Id = transaction.Id, LoanId = transaction.LoanId, Type = transaction.Type,
            PrincipalAmount = transaction.PrincipalAmount, InterestAmount = transaction.InterestAmount,
            TotalCashMovement = transaction.Type == LoanTransactionType.Drawdown
                ? transaction.PrincipalAmount : transaction.PrincipalAmount + transaction.InterestAmount,
            Date = transaction.Date, FinanceAccountId = transaction.FinanceAccountId,
            FinanceAccountName = transaction.FinanceAccount.Name, Reference = transaction.Reference, Note = transaction.Note,
            RunningBalance = Money(runningBalance), CreatedAt = transaction.CreatedAt, UpdatedAt = transaction.UpdatedAt,
            ConcurrencyToken = Convert.ToBase64String(transaction.RowVersion),
            Attachment = transaction.Attachment == null ? null : new FinanceAttachmentDto
            {
                FileName = transaction.Attachment.OriginalFileName,
                ContentType = transaction.Attachment.ContentType,
                FileSize = transaction.Attachment.FileSize,
                UploadedAt = transaction.Attachment.UploadedAt
            }
        };

        private static decimal SignedPrincipal(LoanTransaction transaction) =>
            transaction.Type == LoanTransactionType.Drawdown ? transaction.PrincipalAmount : -transaction.PrincipalAmount;
        private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        private sealed record PrincipalMovement(DateTime Date, decimal Amount);
        private sealed class LoanAggregate
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string? LenderName { get; set; }
            public int FinanceAccountId { get; set; }
            public string FinanceAccountName { get; set; } = string.Empty;
            public bool IsActive { get; set; }
            public decimal OpeningBalance { get; set; }
            public decimal DrawnPrincipal { get; set; }
            public decimal RepaidPrincipal { get; set; }
            public decimal InterestPaid { get; set; }
            public int TransactionCount { get; set; }
            public byte[] RowVersion { get; set; } = [];
        }
    }
}
