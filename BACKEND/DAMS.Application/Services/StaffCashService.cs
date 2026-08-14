using DAMS.Application.Common;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public sealed class StaffCashService : IStaffCashService
    {
        private const decimal MaximumAmount = 999_999_999_999_999.99m;
        private static readonly DateTime SqlStart = new(1753, 1, 1);
        private readonly AppDbContext _context;
        private readonly IFinanceAccountService _accounts;

        public StaffCashService(AppDbContext context, IFinanceAccountService accounts)
        {
            _context = context;
            _accounts = accounts;
        }

        public async Task<StaffCashOverviewDto> GetOverviewAsync(
            bool includeSettled,
            CancellationToken cancellationToken = default)
        {
            var accountRows = await StaffAccounts().ToListAsync(cancellationToken);
            var ledgers = await LoadLedgersAsync(accountRows.Select(a => a.Id).ToList(), cancellationToken);
            var openingDate = await OpeningDateAsync(cancellationToken);
            var holders = accountRows
                .Select(a => BuildHolder(a, LedgerFor(ledgers, a.Id), openingDate))
                .Where(h => includeSettled || h.CurrentBalance != 0m)
                .OrderByDescending(h => h.CurrentBalance > 0m)
                .ThenByDescending(h => Math.Abs(h.CurrentBalance))
                .ThenBy(h => h.PersonName)
                .ToList();
            var allBalances = accountRows
                .Select(a => BuildHolder(a, LedgerFor(ledgers, a.Id), openingDate).CurrentBalance)
                .ToList();
            var cash = (await _accounts.GetOverviewAsync(cancellationToken)).TotalBalance;
            var net = Money(allBalances.Sum());
            return new StaffCashOverviewDto
            {
                TotalHeldByStaff = Money(allBalances.Where(x => x > 0m).Sum()),
                TotalOwedToStaff = Money(-allBalances.Where(x => x < 0m).Sum()),
                NetStaffBalance = net,
                CashAndBankBalance = Money(cash),
                TrackedCompanyCash = Money(cash + net),
                HoldingCount = allBalances.Count(x => x > 0m),
                OwedCount = allBalances.Count(x => x < 0m),
                Holders = holders
            };
        }

        public async Task<StaffCashHolderDto> CreateHolderAsync(
            CreateStaffCashHolderDto dto,
            CancellationToken cancellationToken = default)
        {
            var person = dto.PersonName?.Trim() ?? string.Empty;
            if (person.Length == 0) throw new InvalidOperationException("Person name is required.");
            if (person.Length > 100) throw new InvalidOperationException("Person name cannot exceed 100 characters.");
            if (await _context.FinanceAccounts.AnyAsync(a =>
                    a.Type == FinanceAccountType.StaffFloat && a.AccountHolderName == person, cancellationToken))
                throw new InvalidOperationException("This person already has a staff float.");

            var accountName = $"{person} — Staff float";
            if (await _context.FinanceAccounts.AnyAsync(a => a.Name == accountName, cancellationToken))
                throw new InvalidOperationException("A finance account with this name already exists.");
            var account = new FinanceAccount
            {
                Name = accountName,
                Type = FinanceAccountType.StaffFloat,
                AccountHolderName = person,
                OpeningBalance = 0m,
                DisplayOrder = 450,
                Description = "Company money held by this staff member for business spending.",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.FinanceAccounts.Add(account);
            await _context.SaveChangesAsync(cancellationToken);
            return BuildHolder(new StaffAccountRow
            {
                Id = account.Id, Name = account.Name, PersonName = account.AccountHolderName,
                OpeningBalance = account.OpeningBalance, IsActive = account.IsActive,
                CreatedAt = account.CreatedAt
            }, [], await OpeningDateAsync(cancellationToken));
        }

        public async Task<StaffCashStatementDto> GetStatementAsync(
            int staffFinanceAccountId,
            int skip,
            int take,
            CancellationToken cancellationToken = default)
        {
            skip = Math.Max(0, skip);
            take = Math.Clamp(take, 1, 200);
            var account = await StaffAccounts().SingleOrDefaultAsync(a => a.Id == staffFinanceAccountId, cancellationToken)
                ?? throw new InvalidOperationException("Staff float not found.");
            var ledgers = await LoadLedgersAsync([staffFinanceAccountId], cancellationToken);
            var ledger = LedgerFor(ledgers, staffFinanceAccountId);
            var holder = BuildHolder(account, ledger, await OpeningDateAsync(cancellationToken));
            var newestFirst = ledger
                .OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAt)
                .ThenByDescending(e => e.SortOrder).ThenByDescending(e => e.RecordId)
                .ToList();
            return new StaffCashStatementDto
            {
                Holder = holder,
                Items = newestFirst.Skip(skip).Take(take).Select(MapHistory).ToList(),
                HasMore = newestFirst.Count > skip + take
            };
        }

        public async Task<StaffCashHistoryItemDto> RecordTransferAsync(
            int staffFinanceAccountId,
            SaveStaffCashTransferDto dto,
            int? userId,
            CancellationToken cancellationToken = default)
        {
            ValidateTransfer(dto);
            await ValidateDateAsync(dto.Date, cancellationToken);
            await EnsureStaffAccountAsync(staffFinanceAccountId, requireActive: true, cancellationToken);
            await _accounts.EnsureSelectableAsync(dto.CounterpartyFinanceAccountId, null, cancellationToken);
            var transfer = new StaffCashTransfer
            {
                StaffFinanceAccountId = staffFinanceAccountId,
                CounterpartyFinanceAccountId = dto.CounterpartyFinanceAccountId,
                Type = dto.Type,
                Amount = Money(dto.Amount),
                Date = dto.Date.Date,
                Reference = Clean(dto.Reference),
                Note = Clean(dto.Note),
                CreatedByUserId = userId,
                UpdatedByUserId = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.StaffCashTransfers.Add(transfer);
            await _context.SaveChangesAsync(cancellationToken);
            return await GetTransferHistoryAsync(staffFinanceAccountId, transfer.Id, cancellationToken);
        }

        public async Task<StaffCashHistoryItemDto> UpdateTransferAsync(
            int staffFinanceAccountId,
            int transferId,
            SaveStaffCashTransferDto dto,
            int? userId,
            CancellationToken cancellationToken = default)
        {
            ValidateTransfer(dto);
            await ValidateDateAsync(dto.Date, cancellationToken);
            await EnsureStaffAccountAsync(staffFinanceAccountId, requireActive: true, cancellationToken);
            var transfer = await _context.StaffCashTransfers.SingleOrDefaultAsync(
                    t => t.Id == transferId && t.StaffFinanceAccountId == staffFinanceAccountId, cancellationToken)
                ?? throw new InvalidOperationException("Staff cash transfer not found.");
            ApplyToken(transfer, dto.ConcurrencyToken);
            await _accounts.EnsureSelectableAsync(
                dto.CounterpartyFinanceAccountId, transfer.CounterpartyFinanceAccountId, cancellationToken);
            transfer.Type = dto.Type;
            transfer.Amount = Money(dto.Amount);
            transfer.Date = dto.Date.Date;
            transfer.CounterpartyFinanceAccountId = dto.CounterpartyFinanceAccountId;
            transfer.Reference = Clean(dto.Reference);
            transfer.Note = Clean(dto.Note);
            transfer.UpdatedByUserId = userId;
            transfer.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
            return await GetTransferHistoryAsync(staffFinanceAccountId, transfer.Id, cancellationToken);
        }

        public async Task DeleteTransferAsync(
            int staffFinanceAccountId,
            int transferId,
            string concurrencyToken,
            CancellationToken cancellationToken = default)
        {
            await EnsureStaffAccountAsync(staffFinanceAccountId, requireActive: true, cancellationToken);
            var transfer = await _context.StaffCashTransfers.SingleOrDefaultAsync(
                    t => t.Id == transferId && t.StaffFinanceAccountId == staffFinanceAccountId, cancellationToken)
                ?? throw new InvalidOperationException("Staff cash transfer not found.");
            ApplyToken(transfer, concurrencyToken);
            _context.StaffCashTransfers.Remove(transfer);
            await _context.SaveChangesAsync(cancellationToken);
        }

        private IQueryable<StaffAccountRow> StaffAccounts() =>
            _context.FinanceAccounts.AsNoTracking()
                .Where(a => a.Type == FinanceAccountType.StaffFloat)
                .OrderByDescending(a => a.IsActive).ThenBy(a => a.AccountHolderName)
                .Select(a => new StaffAccountRow
                {
                    Id = a.Id, Name = a.Name, PersonName = a.AccountHolderName,
                    OpeningBalance = a.OpeningBalance, IsActive = a.IsActive, CreatedAt = a.CreatedAt
                });

        private async Task<Dictionary<int, List<LedgerEvent>>> LoadLedgersAsync(
            List<int> accountIds,
            CancellationToken cancellationToken)
        {
            if (accountIds.Count == 0) return [];
            var transferRows = await _context.StaffCashTransfers.AsNoTracking()
                .Where(t => accountIds.Contains(t.StaffFinanceAccountId))
                .Select(t => new
                {
                    AccountId = t.StaffFinanceAccountId, t.Id, t.Type, t.Amount, t.Date,
                    t.Reference, t.Note, t.CreatedAt, t.CounterpartyFinanceAccountId,
                    CounterpartyName = t.CounterpartyFinanceAccount.Name, t.RowVersion
                }).ToListAsync(cancellationToken);
            var expenseRows = await _context.Expenses.AsNoTracking()
                .Where(e => e.FinanceAccountId.HasValue && accountIds.Contains(e.FinanceAccountId.Value))
                .Select(e => new
                {
                    AccountId = e.FinanceAccountId!.Value, e.Id, e.Date, e.CreatedAt,
                    e.Category, e.Description, e.Vendor, e.Amount, e.WhtAmount,
                    ProjectName = e.Project != null ? e.Project.ProjectName : "General"
                }).ToListAsync(cancellationToken);
            var result = accountIds.Distinct().ToDictionary(id => id, _ => new List<LedgerEvent>());
            foreach (var row in transferRows)
            {
                result[row.AccountId].Add(new LedgerEvent
                {
                    RecordType = "Transfer", RecordId = row.Id,
                    Kind = row.Type == StaffCashMovementType.FundsGiven ? "Money received" : "Money returned",
                    Date = row.Date, CreatedAt = row.CreatedAt, SortOrder = (int)row.Type,
                    Description = row.Type == StaffCashMovementType.FundsGiven
                        ? $"Received from {row.CounterpartyName}"
                        : $"Returned to {row.CounterpartyName}",
                    Reference = row.Reference, Note = row.Note,
                    Amount = row.Type == StaffCashMovementType.FundsGiven ? row.Amount : -row.Amount,
                    GrossAmount = row.Amount, MovementType = row.Type,
                    CounterpartyFinanceAccountId = row.CounterpartyFinanceAccountId,
                    CounterpartyFinanceAccountName = row.CounterpartyName,
                    ConcurrencyToken = Convert.ToBase64String(row.RowVersion)
                });
            }
            foreach (var row in expenseRows)
            {
                result[row.AccountId].Add(new LedgerEvent
                {
                    RecordType = "Expense", RecordId = row.Id, Kind = "Expense",
                    Date = row.Date, CreatedAt = row.CreatedAt, SortOrder = 3,
                    Description = row.Category, Reference = row.Vendor,
                    Note = row.Description, ProjectName = row.ProjectName,
                    Amount = -(row.Amount - row.WhtAmount), GrossAmount = row.Amount,
                    WhtAmount = row.WhtAmount
                });
            }
            foreach (var ledger in result.Values)
                ledger.Sort((left, right) =>
                {
                    var value = left.Date.CompareTo(right.Date);
                    if (value != 0) return value;
                    value = left.CreatedAt.CompareTo(right.CreatedAt);
                    if (value != 0) return value;
                    value = left.SortOrder.CompareTo(right.SortOrder);
                    return value != 0 ? value : left.RecordId.CompareTo(right.RecordId);
                });
            return result;
        }

        private static StaffCashHolderDto BuildHolder(
            StaffAccountRow account,
            List<LedgerEvent> ledger,
            DateTime? openingDate)
        {
            var running = Money(account.OpeningBalance);
            DateTime? outstandingSince = running == 0m ? null : (openingDate ?? account.CreatedAt.Date);
            foreach (var item in ledger)
            {
                var prior = running;
                running = Money(running + item.Amount);
                item.RunningBalance = running;
                if (prior == 0m && running != 0m) outstandingSince = item.Date.Date;
                if (running == 0m) outstandingSince = null;
            }
            var days = outstandingSince.HasValue
                ? Math.Max(0, (PakistanTime.Today - outstandingSince.Value.Date).Days)
                : (int?)null;
            return new StaffCashHolderDto
            {
                FinanceAccountId = account.Id, PersonName = account.PersonName,
                AccountName = account.Name, IsActive = account.IsActive,
                OpeningBalance = Money(account.OpeningBalance), CurrentBalance = running,
                OutstandingSince = outstandingSince, DaysOutstanding = days,
                LastActivityDate = ledger.Count == 0 ? null : ledger[^1].Date,
                TransactionCount = ledger.Count
            };
        }

        private async Task<StaffCashHistoryItemDto> GetTransferHistoryAsync(
            int staffFinanceAccountId,
            int transferId,
            CancellationToken cancellationToken)
        {
            var account = await StaffAccounts().SingleAsync(a => a.Id == staffFinanceAccountId, cancellationToken);
            var ledgers = await LoadLedgersAsync([staffFinanceAccountId], cancellationToken);
            var ledger = LedgerFor(ledgers, staffFinanceAccountId);
            _ = BuildHolder(account, ledger, await OpeningDateAsync(cancellationToken));
            var row = ledger.Single(e => e.RecordType == "Transfer" && e.RecordId == transferId);
            return MapHistory(row);
        }

        private async Task EnsureStaffAccountAsync(
            int accountId,
            bool requireActive,
            CancellationToken cancellationToken)
        {
            var account = await _context.FinanceAccounts.AsNoTracking()
                .Where(a => a.Id == accountId)
                .Select(a => new { a.Type, a.IsActive })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("Staff float not found.");
            if (account.Type != FinanceAccountType.StaffFloat)
                throw new InvalidOperationException("Selected account is not a staff float.");
            if (requireActive && !account.IsActive)
                throw new InvalidOperationException("This staff float is inactive.");
        }

        private async Task ValidateDateAsync(DateTime date, CancellationToken cancellationToken)
        {
            var value = date.Date;
            if (value < SqlStart) throw new InvalidOperationException("Transfer date is outside the supported range.");
            if (value > PakistanTime.Today) throw new InvalidOperationException("Transfer date cannot be in the future.");
            var openingDate = await OpeningDateAsync(cancellationToken);
            if (openingDate.HasValue && value < openingDate.Value.Date)
                throw new InvalidOperationException(
                    $"Transfer date cannot be before the committed opening balance date ({openingDate:dd MMM yyyy}).");
        }

        private static void ValidateTransfer(SaveStaffCashTransferDto dto)
        {
            if (!Enum.IsDefined(dto.Type)) throw new InvalidOperationException("Select a valid movement type.");
            if (dto.Date == default) throw new InvalidOperationException("Transfer date is required.");
            if (dto.CounterpartyFinanceAccountId <= 0)
                throw new InvalidOperationException("Select the company cash or bank account used.");
            if (dto.Amount <= 0m) throw new InvalidOperationException("Amount must be greater than zero.");
            if (dto.Amount > MaximumAmount) throw new InvalidOperationException("Amount is outside the supported range.");
            if (dto.Amount != Money(dto.Amount))
                throw new InvalidOperationException("Amount cannot have more than two decimal places.");
            if (dto.Reference?.Trim().Length > 200)
                throw new InvalidOperationException("Reference cannot exceed 200 characters.");
            if (dto.Note?.Trim().Length > 1000)
                throw new InvalidOperationException("Note cannot exceed 1000 characters.");
        }

        private void ApplyToken(StaffCashTransfer transfer, string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                if (transfer.RowVersion.Length == 0) return;
                throw new InvalidOperationException("The transfer version is missing. Refresh and try again.");
            }
            try
            {
                _context.Entry(transfer).Property(t => t.RowVersion).OriginalValue = Convert.FromBase64String(token);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException("The transfer version is invalid. Refresh and try again.");
            }
        }

        private Task<DateTime?> OpeningDateAsync(CancellationToken cancellationToken) =>
            _context.OpeningBalanceSets.AsNoTracking().Where(s => s.CommittedAt != null)
                .Select(s => (DateTime?)s.AsAtDate).SingleOrDefaultAsync(cancellationToken);

        private static List<LedgerEvent> LedgerFor(
            Dictionary<int, List<LedgerEvent>> ledgers,
            int id) => ledgers.TryGetValue(id, out var ledger) ? ledger : [];

        private static StaffCashHistoryItemDto MapHistory(LedgerEvent row) => new()
        {
            RecordType = row.RecordType, RecordId = row.RecordId, Kind = row.Kind,
            Date = row.Date, Description = row.Description, Reference = row.Reference,
            ProjectName = row.ProjectName, Amount = Money(row.Amount), GrossAmount = Money(row.GrossAmount),
            WhtAmount = Money(row.WhtAmount), RunningBalance = Money(row.RunningBalance),
            MovementType = row.MovementType,
            CounterpartyFinanceAccountId = row.CounterpartyFinanceAccountId,
            CounterpartyFinanceAccountName = row.CounterpartyFinanceAccountName,
            Note = row.Note, ConcurrencyToken = row.ConcurrencyToken
        };

        private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private sealed class StaffAccountRow
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string PersonName { get; set; } = string.Empty;
            public decimal OpeningBalance { get; set; }
            public bool IsActive { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        private sealed class LedgerEvent
        {
            public string RecordType { get; set; } = string.Empty;
            public int RecordId { get; set; }
            public string Kind { get; set; } = string.Empty;
            public DateTime Date { get; set; }
            public DateTime CreatedAt { get; set; }
            public int SortOrder { get; set; }
            public string Description { get; set; } = string.Empty;
            public string? Reference { get; set; }
            public string? ProjectName { get; set; }
            public decimal Amount { get; set; }
            public decimal GrossAmount { get; set; }
            public decimal WhtAmount { get; set; }
            public decimal RunningBalance { get; set; }
            public StaffCashMovementType? MovementType { get; set; }
            public int? CounterpartyFinanceAccountId { get; set; }
            public string? CounterpartyFinanceAccountName { get; set; }
            public string? Note { get; set; }
            public string? ConcurrencyToken { get; set; }
        }
    }
}
