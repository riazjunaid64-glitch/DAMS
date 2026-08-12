using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public sealed class OpeningBalanceService : IOpeningBalanceService
    {
        private readonly AppDbContext _context;
        public OpeningBalanceService(AppDbContext context) => _context = context;

        public async Task<OpeningBalanceSetDto?> GetCurrentAsync(CancellationToken cancellationToken = default)
        {
            var id = await _context.OpeningBalanceSets.AsNoTracking().OrderByDescending(s => s.AsAtDate)
                .Select(s => (int?)s.Id).FirstOrDefaultAsync(cancellationToken);
            return id.HasValue ? await MapAsync(id.Value, cancellationToken) : null;
        }

        public async Task<OpeningBalanceSetDto> CreateAsync(DateTime asAtDate, int? userId, CancellationToken cancellationToken = default)
        {
            if (asAtDate == default) throw new InvalidOperationException("Opening balance date is required.");
            if (await _context.OpeningBalanceSets.AnyAsync(cancellationToken))
                throw new InvalidOperationException("An opening balance set already exists. Reopen it instead of creating a second baseline.");
            var accounts = await _context.FinanceAccounts.OrderBy(a => a.DisplayOrder).ThenBy(a => a.Name).ToListAsync(cancellationToken);
            if (accounts.Count == 0) throw new InvalidOperationException("Create the chart of accounts before entering opening balances.");
            var set = new OpeningBalanceSet { AsAtDate = asAtDate.Date };
            foreach (var account in accounts)
                set.Entries.Add(new OpeningBalanceEntry { FinanceAccountId = account.Id });
            set.AuditEntries.Add(new OpeningBalanceAuditEntry { Action = "Created", UserId = userId, OccurredAt = DateTime.UtcNow });
            _context.OpeningBalanceSets.Add(set);
            await _context.SaveChangesAsync(cancellationToken);
            return await MapAsync(set.Id, cancellationToken);
        }

        public async Task<OpeningBalanceSetDto> SaveAsync(int id, SaveOpeningBalanceSetDto dto, int? userId, CancellationToken cancellationToken = default)
        {
            var set = await _context.OpeningBalanceSets.Include(s => s.Entries).SingleOrDefaultAsync(s => s.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("Opening balance set not found.");
            if (set.IsCommitted) throw new InvalidOperationException("This opening balance set is committed. Explicitly reopen it before editing.");
            ApplyToken(set, dto.ConcurrencyToken);
            if (dto.Entries.GroupBy(e => e.FinanceAccountId).Any(g => g.Count() > 1))
                throw new InvalidOperationException("Each account may appear only once.");
            var accountIds = await _context.FinanceAccounts.Select(a => a.Id).ToListAsync(cancellationToken);
            if (dto.Entries.Any(e => !accountIds.Contains(e.FinanceAccountId)))
                throw new InvalidOperationException("An opening balance entry refers to an account that no longer exists.");
            foreach (var input in dto.Entries) ValidateEntry(input);

            foreach (var accountId in accountIds)
            {
                var input = dto.Entries.SingleOrDefault(e => e.FinanceAccountId == accountId);
                var entry = set.Entries.SingleOrDefault(e => e.FinanceAccountId == accountId);
                if (entry == null)
                {
                    entry = new OpeningBalanceEntry { FinanceAccountId = accountId };
                    set.Entries.Add(entry);
                }
                entry.DebitAmount = Money(input?.DebitAmount ?? 0m);
                entry.CreditAmount = Money(input?.CreditAmount ?? 0m);
                entry.Note = Clean(input?.Note);
            }
            _context.Entry(set).Property(s => s.IsCommitted).IsModified = true;
            set.AuditEntries.Add(new OpeningBalanceAuditEntry { Action = "Saved", UserId = userId, OccurredAt = DateTime.UtcNow });
            await _context.SaveChangesAsync(cancellationToken);
            return await MapAsync(id, cancellationToken);
        }

        public async Task<OpeningBalanceSetDto> CommitAsync(int id, string concurrencyToken, int? userId, CancellationToken cancellationToken = default)
        {
            var set = await _context.OpeningBalanceSets.Include(s => s.Entries).ThenInclude(e => e.FinanceAccount)
                .SingleOrDefaultAsync(s => s.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("Opening balance set not found.");
            if (set.IsCommitted) throw new InvalidOperationException("This opening balance set is already committed.");
            ApplyToken(set, concurrencyToken);
            var debit = Money(set.Entries.Sum(e => e.DebitAmount));
            var credit = Money(set.Entries.Sum(e => e.CreditAmount));
            if (debit != credit)
                throw new InvalidOperationException($"Opening balances do not balance. Debits are {debit:N2}, credits are {credit:N2}, difference is {debit - credit:N2}.");

            // Accounts can be created after the draft baseline. Include every current account on
            // commit so a new account cannot retain an uncontrolled OpeningBalance value.
            var accounts = await _context.FinanceAccounts.ToListAsync(cancellationToken);
            foreach (var account in accounts)
            {
                var entry = set.Entries.SingleOrDefault(e => e.FinanceAccountId == account.Id);
                if (entry == null)
                {
                    entry = new OpeningBalanceEntry { FinanceAccountId = account.Id, FinanceAccount = account };
                    set.Entries.Add(entry);
                }
                account.OpeningBalance = Money(AccountBalanceDirection.ToNormalBalance(
                    account.Type, entry.DebitAmount, entry.CreditAmount));
            }
            set.IsCommitted = true;
            set.CommittedAt = DateTime.UtcNow;
            set.CommittedByUserId = userId;
            set.AuditEntries.Add(new OpeningBalanceAuditEntry { Action = "Committed", UserId = userId, OccurredAt = DateTime.UtcNow });
            await _context.SaveChangesAsync(cancellationToken);
            return await MapAsync(id, cancellationToken);
        }

        public async Task<OpeningBalanceSetDto> ReopenAsync(int id, ReopenOpeningBalanceSetDto dto, int? userId, CancellationToken cancellationToken = default)
        {
            if (!dto.WarningAccepted)
                throw new InvalidOperationException("Confirm that reopening can change every historical financial report.");
            var set = await _context.OpeningBalanceSets.SingleOrDefaultAsync(s => s.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("Opening balance set not found.");
            if (!set.IsCommitted) throw new InvalidOperationException("This opening balance set is already open for editing.");
            ApplyToken(set, dto.ConcurrencyToken);
            set.IsCommitted = false;
            set.AuditEntries.Add(new OpeningBalanceAuditEntry
            {
                Action = "Reopened", UserId = userId, OccurredAt = DateTime.UtcNow, Note = Clean(dto.Note)
            });
            await _context.SaveChangesAsync(cancellationToken);
            return await MapAsync(id, cancellationToken);
        }

        private async Task<OpeningBalanceSetDto> MapAsync(int id, CancellationToken cancellationToken)
        {
            var set = await _context.OpeningBalanceSets.AsNoTracking().Where(s => s.Id == id).Select(s => new OpeningBalanceSetDto
            {
                Id = s.Id, AsAtDate = s.AsAtDate, IsCommitted = s.IsCommitted, CommittedAt = s.CommittedAt,
                CommittedByUserId = s.CommittedByUserId,
                Entries = s.Entries.OrderBy(e => e.FinanceAccount.Type).ThenBy(e => e.FinanceAccount.DisplayOrder).ThenBy(e => e.FinanceAccount.Name)
                    .Select(e => new OpeningBalanceEntryDto
                    {
                        FinanceAccountId = e.FinanceAccountId, AccountName = e.FinanceAccount.Name,
                        LedgerCode = e.FinanceAccount.LedgerCode, AccountType = e.FinanceAccount.Type,
                        DisplayOrder = e.FinanceAccount.DisplayOrder, DebitAmount = e.DebitAmount,
                        CreditAmount = e.CreditAmount, Note = e.Note
                    }).ToList(),
                AuditEntries = s.AuditEntries.OrderByDescending(a => a.OccurredAt).Select(a => new OpeningBalanceAuditDto
                {
                    Action = a.Action, UserId = a.UserId, OccurredAt = a.OccurredAt, Note = a.Note
                }).ToList(),
                ConcurrencyToken = Convert.ToBase64String(s.RowVersion)
            }).SingleAsync(cancellationToken);
            set.TotalDebits = Money(set.Entries.Sum(e => e.DebitAmount));
            set.TotalCredits = Money(set.Entries.Sum(e => e.CreditAmount));
            set.Difference = Money(set.TotalDebits - set.TotalCredits);
            var existingIds = set.Entries.Select(e => e.FinanceAccountId).ToList();
            var missing = await _context.FinanceAccounts.AsNoTracking().Where(a => !existingIds.Contains(a.Id))
                .Select(a => new OpeningBalanceEntryDto
                {
                    FinanceAccountId = a.Id, AccountName = a.Name, LedgerCode = a.LedgerCode,
                    AccountType = a.Type, DisplayOrder = a.DisplayOrder
                }).ToListAsync(cancellationToken);
            set.Entries.AddRange(missing);
            set.Entries = set.Entries.OrderBy(e => GroupOrder(e.AccountType)).ThenBy(e => e.DisplayOrder)
                .ThenBy(e => e.AccountName).ToList();
            return set;
        }

        private static int GroupOrder(FinanceAccountType type) => type switch
        {
            FinanceAccountType.Cash or FinanceAccountType.Bank or FinanceAccountType.MobileWallet or FinanceAccountType.Other => 0,
            FinanceAccountType.FixedAsset => 1,
            FinanceAccountType.WorkInProgress => 2,
            FinanceAccountType.Receivable => 3,
            FinanceAccountType.Liability => 4,
            FinanceAccountType.Capital => 5,
            _ => 6
        };

        private void ApplyToken(OpeningBalanceSet set, string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                if (set.RowVersion.Length == 0) return;
                throw new InvalidOperationException("The opening balance version is missing. Refresh and try again.");
            }
            try { _context.Entry(set).Property(s => s.RowVersion).OriginalValue = Convert.FromBase64String(token); }
            catch (FormatException) { throw new InvalidOperationException("The opening balance version is invalid. Refresh and try again."); }
        }

        private static void ValidateEntry(SaveOpeningBalanceEntryDto entry)
        {
            if (entry.DebitAmount < 0m || entry.CreditAmount < 0m) throw new InvalidOperationException("Opening balance amounts cannot be negative.");
            if (entry.DebitAmount > 0m && entry.CreditAmount > 0m) throw new InvalidOperationException("An account cannot have both a debit and a credit opening balance.");
            if (entry.DebitAmount > 999_999_999_999_999.99m || entry.CreditAmount > 999_999_999_999_999.99m)
                throw new InvalidOperationException("An opening balance amount is outside the supported range.");
            if (entry.Note?.Trim().Length > 500) throw new InvalidOperationException("An opening balance note cannot exceed 500 characters.");
        }

        private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
