using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public sealed class FinanceAccountService : IFinanceAccountService
    {
        private readonly AppDbContext _context;

        public FinanceAccountService(AppDbContext context) => _context = context;

        public async Task<PagedResult<FinanceAccountResponseDto>> GetPageAsync(
            string? search, FinanceAccountType? type, string? holder, bool? isActive,
            int skip, int take, CancellationToken cancellationToken = default)
        {
            var query = _context.FinanceAccounts.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(a => a.Name.Contains(term)
                    || a.AccountHolderName.Contains(term)
                    || (a.BankOrWalletName != null && a.BankOrWalletName.Contains(term))
                    || (a.Description != null && a.Description.Contains(term)));
            }
            if (type.HasValue) query = query.Where(a => a.Type == type.Value);
            if (!string.IsNullOrWhiteSpace(holder)) query = query.Where(a => a.AccountHolderName == holder.Trim());
            if (isActive.HasValue) query = query.Where(a => a.IsActive == isActive.Value);

            var rows = await Project(query.OrderByDescending(a => a.IsActive).ThenBy(a => a.Name))
                .Skip(skip).Take(take + 1).ToListAsync(cancellationToken);
            return new PagedResult<FinanceAccountResponseDto>
            {
                Items = rows.Take(take).ToList(),
                HasMore = rows.Count > take
            };
        }

        public Task<List<FinanceAccountOptionDto>> GetOptionsAsync(bool includeInactive, CancellationToken cancellationToken = default) =>
            _context.FinanceAccounts.AsNoTracking()
                .Where(a => includeInactive || a.IsActive)
                .OrderByDescending(a => a.IsActive).ThenBy(a => a.Name)
                .Select(a => new FinanceAccountOptionDto
                {
                    Id = a.Id, Name = a.Name, Type = a.Type,
                    AccountHolderName = a.AccountHolderName, IsActive = a.IsActive
                }).ToListAsync(cancellationToken);

        public async Task<FinanceAccountResponseDto> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            await Project(_context.FinanceAccounts.AsNoTracking().Where(a => a.Id == id))
                .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Finance account not found.");

        public async Task<PagedResult<FinanceAccountTransactionDto>> GetTransactionsAsync(
            int id, int skip, int take, CancellationToken cancellationToken = default)
        {
            if (!await _context.FinanceAccounts.AnyAsync(a => a.Id == id, cancellationToken))
                throw new InvalidOperationException("Finance account not found.");

            var revenue = _context.ManualRevenues.AsNoTracking().Where(r => r.FinanceAccountId == id)
                .Select(r => new FinanceAccountTransactionDto
                {
                    Kind = "Revenue", RecordId = r.Id, Date = r.Date, Label = r.RevenueType,
                    Reference = r.Reference, ProjectName = r.Project != null ? r.Project.ProjectName : "General",
                    Amount = r.Amount
                });
            var expenses = _context.Expenses.AsNoTracking().Where(e => e.FinanceAccountId == id)
                .Select(e => new FinanceAccountTransactionDto
                {
                    Kind = "Expense", RecordId = e.Id, Date = e.Date, Label = e.Category,
                    Reference = e.Vendor, ProjectName = e.Project != null ? e.Project.ProjectName : "General",
                    Amount = -e.Amount
                });
            var commissionPayouts = _context.CommissionPayouts.AsNoTracking().Where(p => p.FinanceAccountId == id)
                .Select(p => new FinanceAccountTransactionDto
                {
                    Kind = "Commission payout", RecordId = p.Id, Date = p.PaymentDate,
                    Label = p.Commission.Partner.Name, Reference = p.PaymentReference,
                    ProjectName = p.Commission.Booking.Unit.Project.ProjectName, Amount = -p.Amount
                });
            var commissionReversals = _context.CommissionPayoutReversals.AsNoTracking()
                .Where(r => r.Payout.FinanceAccountId == id).Select(r => new FinanceAccountTransactionDto
                {
                    Kind = "Commission reversal", RecordId = r.Id, Date = r.ReversedAt,
                    Label = r.Payout.Commission.Partner.Name, Reference = r.Reason,
                    ProjectName = r.Payout.Commission.Booking.Unit.Project.ProjectName, Amount = r.Amount
                });
            var rebatePayments = _context.RebateDisbursements.AsNoTracking()
                .Where(d => d.FinanceAccountId == id && d.Method == CustomerRebateMethod.CashOrBankPayment)
                .Select(d => new FinanceAccountTransactionDto
                {
                    Kind = "Customer rebate", RecordId = d.Id, Date = d.AppliedAt,
                    Label = d.Rebate.Customer.FullName, Reference = d.Reference,
                    ProjectName = d.Rebate.Booking.Unit.Project.ProjectName, Amount = -d.Amount
                });
            var rebateReversals = _context.RebateDisbursementReversals.AsNoTracking()
                .Where(r => r.Disbursement.FinanceAccountId == id
                    && r.Disbursement.Method == CustomerRebateMethod.CashOrBankPayment)
                .Select(r => new FinanceAccountTransactionDto
                {
                    Kind = "Rebate reversal", RecordId = r.Id, Date = r.ReversedAt,
                    Label = r.Disbursement.Rebate.Customer.FullName, Reference = r.Reason,
                    ProjectName = r.Disbursement.Rebate.Booking.Unit.Project.ProjectName, Amount = r.Amount
                });
            var rows = await revenue.Concat(expenses).Concat(commissionPayouts).Concat(commissionReversals)
                .Concat(rebatePayments).Concat(rebateReversals)
                .OrderByDescending(t => t.Date).ThenByDescending(t => t.RecordId)
                .Skip(skip).Take(take + 1).ToListAsync(cancellationToken);
            return new PagedResult<FinanceAccountTransactionDto>
            {
                Items = rows.Take(take).ToList(), HasMore = rows.Count > take
            };
        }

        public async Task<FinanceAccountsOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default)
        {
            var accounts = await Project(_context.FinanceAccounts.AsNoTracking()).ToListAsync(cancellationToken);
            return new FinanceAccountsOverviewDto
            {
                ActiveAccounts = accounts.Count(a => a.IsActive),
                InactiveAccounts = accounts.Count(a => !a.IsActive),
                TotalBalance = accounts.Sum(a => a.CurrentBalance),
                HolderBalances = accounts.GroupBy(a => a.AccountHolderName, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new FinanceHolderBalanceDto
                    {
                        AccountHolderName = g.First().AccountHolderName,
                        AccountCount = g.Count(),
                        CurrentBalance = g.Sum(a => a.CurrentBalance)
                    }).OrderByDescending(h => h.CurrentBalance).ToList()
            };
        }

        public async Task<FinanceAccountResponseDto> CreateAsync(CreateFinanceAccountDto dto, CancellationToken cancellationToken = default)
        {
            Validate(dto);
            await EnsureUniqueName(dto.Name, null, cancellationToken);
            var account = new FinanceAccount
            {
                Name = dto.Name.Trim(), Type = dto.Type,
                AccountHolderName = dto.AccountHolderName.Trim(), OpeningBalance = dto.OpeningBalance,
                BankOrWalletName = Clean(dto.BankOrWalletName), Description = Clean(dto.Description),
                IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            };
            _context.FinanceAccounts.Add(account);
            await _context.SaveChangesAsync(cancellationToken);
            return await GetByIdAsync(account.Id, cancellationToken);
        }

        public async Task<FinanceAccountResponseDto> UpdateAsync(int id, UpdateFinanceAccountDto dto, CancellationToken cancellationToken = default)
        {
            Validate(dto);
            var account = await _context.FinanceAccounts.SingleOrDefaultAsync(a => a.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("Finance account not found.");
            ApplyConcurrencyToken(account, dto.ConcurrencyToken);
            await EnsureUniqueName(dto.Name, id, cancellationToken);
            account.Name = dto.Name.Trim();
            account.Type = dto.Type;
            account.AccountHolderName = dto.AccountHolderName.Trim();
            account.OpeningBalance = dto.OpeningBalance;
            account.BankOrWalletName = Clean(dto.BankOrWalletName);
            account.Description = Clean(dto.Description);
            account.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
            return await GetByIdAsync(id, cancellationToken);
        }

        public async Task<FinanceAccountResponseDto> SetActiveAsync(
            int id, bool isActive, string concurrencyToken, CancellationToken cancellationToken = default)
        {
            var account = await _context.FinanceAccounts.SingleOrDefaultAsync(a => a.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("Finance account not found.");
            ApplyConcurrencyToken(account, concurrencyToken);
            account.IsActive = isActive;
            account.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
            return await GetByIdAsync(id, cancellationToken);
        }

        public async Task DeleteUnusedAsync(int id, CancellationToken cancellationToken = default)
        {
            var account = await _context.FinanceAccounts.SingleOrDefaultAsync(a => a.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("Finance account not found.");
            var used = await _context.ManualRevenues.AnyAsync(r => r.FinanceAccountId == id, cancellationToken)
                || await _context.Expenses.AnyAsync(e => e.FinanceAccountId == id, cancellationToken)
                || await _context.CommissionPayouts.AnyAsync(p => p.FinanceAccountId == id, cancellationToken)
                || await _context.RebateDisbursements.AnyAsync(d => d.FinanceAccountId == id, cancellationToken);
            if (used) throw new InvalidOperationException("This account has transactions and cannot be deleted. Make it inactive instead.");
            _context.FinanceAccounts.Remove(account);
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task EnsureSelectableAsync(int accountId, int? currentAccountId = null, CancellationToken cancellationToken = default)
        {
            var account = await _context.FinanceAccounts.AsNoTracking()
                .Where(a => a.Id == accountId)
                .Select(a => new { a.IsActive })
                .SingleOrDefaultAsync(cancellationToken);
            if (account == null) throw new InvalidOperationException("Selected finance account does not exist.");
            if (!account.IsActive && currentAccountId != accountId)
                throw new InvalidOperationException("Selected finance account is inactive. Choose an active account.");
        }

        private IQueryable<FinanceAccountResponseDto> Project(IQueryable<FinanceAccount> query) => query.Select(a =>
            new FinanceAccountResponseDto
            {
                Id = a.Id, Name = a.Name, Type = a.Type, AccountHolderName = a.AccountHolderName,
                OpeningBalance = a.OpeningBalance, BankOrWalletName = a.BankOrWalletName,
                Description = a.Description, IsActive = a.IsActive,
                RevenueReceived = a.ManualRevenues.Sum(r => (decimal?)r.Amount) ?? 0m,
                ExpensesPaid = (a.Expenses.Sum(e => (decimal?)e.Amount) ?? 0m)
                    + (a.CommissionPayouts.Sum(p => (decimal?)p.Amount) ?? 0m)
                    - (a.CommissionPayouts.SelectMany(p => p.Reversals).Sum(r => (decimal?)r.Amount) ?? 0m)
                    + (a.RebateDisbursements.Where(d => d.Method == CustomerRebateMethod.CashOrBankPayment)
                        .Sum(d => (decimal?)d.Amount) ?? 0m)
                    - (a.RebateDisbursements.Where(d => d.Method == CustomerRebateMethod.CashOrBankPayment)
                        .SelectMany(d => d.Reversals).Sum(r => (decimal?)r.Amount) ?? 0m),
                NetMovement = (a.ManualRevenues.Sum(r => (decimal?)r.Amount) ?? 0m)
                    - (a.Expenses.Sum(e => (decimal?)e.Amount) ?? 0m)
                    - (a.CommissionPayouts.Sum(p => (decimal?)p.Amount) ?? 0m)
                    + (a.CommissionPayouts.SelectMany(p => p.Reversals).Sum(r => (decimal?)r.Amount) ?? 0m)
                    - (a.RebateDisbursements.Where(d => d.Method == CustomerRebateMethod.CashOrBankPayment)
                        .Sum(d => (decimal?)d.Amount) ?? 0m)
                    + (a.RebateDisbursements.Where(d => d.Method == CustomerRebateMethod.CashOrBankPayment)
                        .SelectMany(d => d.Reversals).Sum(r => (decimal?)r.Amount) ?? 0m),
                CurrentBalance = a.OpeningBalance + (a.ManualRevenues.Sum(r => (decimal?)r.Amount) ?? 0m)
                    - (a.Expenses.Sum(e => (decimal?)e.Amount) ?? 0m)
                    - (a.CommissionPayouts.Sum(p => (decimal?)p.Amount) ?? 0m)
                    + (a.CommissionPayouts.SelectMany(p => p.Reversals).Sum(r => (decimal?)r.Amount) ?? 0m)
                    - (a.RebateDisbursements.Where(d => d.Method == CustomerRebateMethod.CashOrBankPayment)
                        .Sum(d => (decimal?)d.Amount) ?? 0m)
                    + (a.RebateDisbursements.Where(d => d.Method == CustomerRebateMethod.CashOrBankPayment)
                        .SelectMany(d => d.Reversals).Sum(r => (decimal?)r.Amount) ?? 0m),
                TransactionCount = a.ManualRevenues.Count + a.Expenses.Count + a.CommissionPayouts.Count
                    + a.CommissionPayouts.SelectMany(p => p.Reversals).Count()
                    + a.RebateDisbursements.Count(d => d.Method == CustomerRebateMethod.CashOrBankPayment)
                    + a.RebateDisbursements.Where(d => d.Method == CustomerRebateMethod.CashOrBankPayment)
                        .SelectMany(d => d.Reversals).Count(),
                CreatedAt = a.CreatedAt, UpdatedAt = a.UpdatedAt,
                ConcurrencyToken = Convert.ToBase64String(a.RowVersion)
            });

        private async Task EnsureUniqueName(string name, int? excludingId, CancellationToken cancellationToken)
        {
            var normalized = name.Trim();
            if (await _context.FinanceAccounts.AnyAsync(a => a.Name == normalized && (!excludingId.HasValue || a.Id != excludingId), cancellationToken))
                throw new InvalidOperationException("A finance account with this name already exists.");
        }

        private static void Validate(CreateFinanceAccountDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) throw new InvalidOperationException("Account name is required.");
            if (dto.Name.Trim().Length > 120) throw new InvalidOperationException("Account name cannot exceed 120 characters.");
            if (!Enum.IsDefined(dto.Type)) throw new InvalidOperationException("Select a valid account type.");
            if (string.IsNullOrWhiteSpace(dto.AccountHolderName)) throw new InvalidOperationException("Account holder name is required.");
            if (dto.AccountHolderName.Trim().Length > 150) throw new InvalidOperationException("Account holder name cannot exceed 150 characters.");
            if (Math.Abs(dto.OpeningBalance) > 999_999_999_999_999.99m) throw new InvalidOperationException("Opening balance is outside the supported range.");
            if (dto.BankOrWalletName?.Trim().Length > 150) throw new InvalidOperationException("Bank or wallet name cannot exceed 150 characters.");
            if (dto.Description?.Trim().Length > 1000) throw new InvalidOperationException("Description cannot exceed 1000 characters.");
        }

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private void ApplyConcurrencyToken(FinanceAccount account, string token)
        {
            if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("The account version is missing. Refresh and try again.");
            try { _context.Entry(account).Property(a => a.RowVersion).OriginalValue = Convert.FromBase64String(token); }
            catch (FormatException) { throw new InvalidOperationException("The account version is invalid. Refresh and try again."); }
        }
    }
}
