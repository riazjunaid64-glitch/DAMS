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

        /// <summary>How many movements the aging walk reads at a time. Large enough that a normal
        /// float is answered by one page, small enough that an unusual one still cannot pull a
        /// whole history into memory.</summary>
        private const int AgingPageSize = 200;
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
            var totals = await TotalsByAccountAsync(accountRows.Select(a => a.Id).ToList(), cancellationToken);
            var openingDate = await OpeningDateAsync(cancellationToken);

            // Each holder is built once, from one grouped aggregate rather than a replay of every
            // receipt they ever handled. Only a float that is still open needs its movements read
            // at all, and only back as far as the day it last stood at zero.
            var holders = new List<StaffCashHolderDto>(accountRows.Count);
            foreach (var account in accountRows)
                holders.Add(await BuildHolderAsync(
                    account, totals.GetValueOrDefault(account.Id), openingDate, cancellationToken));

            // Every holder counts towards the company totals; includeSettled only decides who is
            // listed. Filtering before summing would hide settled floats from the reconciliation.
            var balances = holders.Select(h => h.CurrentBalance).ToList();
            var cash = (await _accounts.GetOverviewAsync(cancellationToken)).TotalBalance;
            var net = Money(balances.Sum());
            return new StaffCashOverviewDto
            {
                TotalHeldByStaff = Money(balances.Where(x => x > 0m).Sum()),
                TotalOwedToStaff = Money(-balances.Where(x => x < 0m).Sum()),
                NetStaffBalance = net,
                CashAndBankBalance = Money(cash),
                TrackedCompanyCash = Money(cash + net),
                HoldingCount = balances.Count(x => x > 0m),
                OwedCount = balances.Count(x => x < 0m),
                Holders = holders
                    .Where(h => includeSettled || h.CurrentBalance != 0m)
                    .OrderByDescending(h => h.CurrentBalance > 0m)
                    .ThenByDescending(h => Math.Abs(h.CurrentBalance))
                    .ThenBy(h => h.PersonName)
                    .ToList()
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
            return await BuildHolderAsync(new StaffAccountRow
            {
                Id = account.Id, Name = account.Name, PersonName = account.AccountHolderName,
                OpeningBalance = account.OpeningBalance, IsActive = account.IsActive,
                CreatedAt = account.CreatedAt
            }, null, await OpeningDateAsync(cancellationToken), cancellationToken);
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
            var ids = new List<int> { staffFinanceAccountId };
            var holder = await BuildHolderAsync(
                account,
                (await TotalsByAccountAsync(ids, cancellationToken)).GetValueOrDefault(staffFinanceAccountId),
                await OpeningDateAsync(cancellationToken),
                cancellationToken);

            // Only the requested page is read. A running balance normally needs every earlier
            // movement, but read newest-first it needs only the later ones: the closing balance
            // less everything above the page is the balance its first row left behind, and each
            // step down the page undoes one more movement. So a single SUM stands in for the
            // history above the page, and nothing below it is touched at all.
            var newestFirst = NewestFirst(LedgerQuery(ids));
            var newerSum = skip == 0
                ? 0m
                : await newestFirst.Take(skip).SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m;
            var page = await newestFirst.Skip(skip).Take(take + 1).ToListAsync(cancellationToken);

            var running = Money(holder.CurrentBalance - newerSum);
            var items = new List<StaffCashHistoryItemDto>(Math.Min(page.Count, take));
            foreach (var row in page.Take(take))
            {
                items.Add(MapHistory(row, running));
                running = Money(running - row.Amount);
            }
            return new StaffCashStatementDto { Holder = holder, Items = items, HasMore = page.Count > take };
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

        /// <summary>
        /// Every movement across a staff float — the transfers in and out, and the expenses paid
        /// from it — as one queryable stream. Left as a query rather than a list so callers can
        /// aggregate, order and page it in the database; a float's history only ever grows, so
        /// nothing here may assume it fits in memory.
        /// </summary>
        private IQueryable<LedgerRow> LedgerQuery(List<int> accountIds)
        {
            var transfers = _context.StaffCashTransfers.AsNoTracking()
                .Where(t => accountIds.Contains(t.StaffFinanceAccountId))
                .Select(t => new LedgerRow
                {
                    AccountId = t.StaffFinanceAccountId, RecordType = "Transfer", TypeOrder = 0,
                    RecordId = t.Id,
                    Kind = t.Type == StaffCashMovementType.FundsGiven ? "Money received" : "Money returned",
                    Date = t.Date, CreatedAt = t.CreatedAt, SortOrder = (int)t.Type,
                    Description = t.Type == StaffCashMovementType.FundsGiven
                        ? "Received from " + t.CounterpartyFinanceAccount.Name
                        : "Returned to " + t.CounterpartyFinanceAccount.Name,
                    Reference = t.Reference, Note = t.Note, ProjectName = null,
                    Amount = t.Type == StaffCashMovementType.FundsGiven ? t.Amount : -t.Amount,
                    GrossAmount = t.Amount, WhtAmount = 0m, MovementType = t.Type,
                    CounterpartyFinanceAccountId = t.CounterpartyFinanceAccountId,
                    CounterpartyFinanceAccountName = t.CounterpartyFinanceAccount.Name,
                    RowVersion = t.RowVersion
                });
            var expenses = _context.Expenses.AsNoTracking()
                .Where(e => e.FinanceAccountId.HasValue && accountIds.Contains(e.FinanceAccountId.Value))
                .Select(e => new LedgerRow
                {
                    AccountId = e.FinanceAccountId!.Value, RecordType = "Expense", TypeOrder = 1,
                    RecordId = e.Id, Kind = "Expense",
                    Date = e.Date, CreatedAt = e.CreatedAt, SortOrder = 3,
                    Description = e.Category, Reference = e.Vendor, Note = e.Description,
                    ProjectName = e.Project != null ? e.Project.ProjectName : "General",
                    // Only the cash that actually left the float. Tax withheld never reaches the
                    // supplier, so it never leaves the person's pocket either.
                    Amount = -(e.Amount - e.WhtAmount), GrossAmount = e.Amount, WhtAmount = e.WhtAmount,
                    MovementType = null, CounterpartyFinanceAccountId = null,
                    CounterpartyFinanceAccountName = null, RowVersion = null
                });
            return transfers.Concat(expenses);
        }

        /// <summary>
        /// Newest movement first. TypeOrder is the final tiebreaker because a transfer and an
        /// expense can share an id — without it two rows could compare equal, and a page boundary
        /// falling between them could repeat or lose one.
        /// </summary>
        private static IQueryable<LedgerRow> NewestFirst(IQueryable<LedgerRow> ledger) =>
            ledger.OrderByDescending(r => r.Date).ThenByDescending(r => r.CreatedAt)
                .ThenByDescending(r => r.SortOrder).ThenByDescending(r => r.RecordId)
                .ThenByDescending(r => r.TypeOrder);

        private async Task<Dictionary<int, LedgerTotals>> TotalsByAccountAsync(
            List<int> accountIds,
            CancellationToken cancellationToken)
        {
            if (accountIds.Count == 0) return [];
            var rows = await LedgerQuery(accountIds)
                .GroupBy(r => r.AccountId)
                .Select(g => new LedgerTotals
                {
                    AccountId = g.Key,
                    Net = g.Sum(r => r.Amount),
                    Count = g.Count(),
                    LastActivityDate = g.Max(r => (DateTime?)r.Date)
                })
                .ToListAsync(cancellationToken);
            return rows.ToDictionary(r => r.AccountId);
        }

        private async Task<StaffCashHolderDto> BuildHolderAsync(
            StaffAccountRow account,
            LedgerTotals? totals,
            DateTime? openingDate,
            CancellationToken cancellationToken)
        {
            var balance = Money(account.OpeningBalance + (totals?.Net ?? 0m));
            // A settled float has nothing outstanding by definition, so it never needs its history
            // walked — which is most of them, most of the time.
            var outstandingSince = balance == 0m
                ? null
                : await OutstandingSinceAsync(account, balance, openingDate, cancellationToken);
            return new StaffCashHolderDto
            {
                FinanceAccountId = account.Id, PersonName = account.PersonName,
                AccountName = account.Name, IsActive = account.IsActive,
                OpeningBalance = Money(account.OpeningBalance), CurrentBalance = balance,
                OutstandingSince = outstandingSince,
                DaysOutstanding = outstandingSince.HasValue
                    ? Math.Max(0, (PakistanTime.Today - outstandingSince.Value.Date).Days)
                    : (int?)null,
                LastActivityDate = totals?.LastActivityDate,
                TransactionCount = totals?.Count ?? 0
            };
        }

        /// <summary>
        /// The day this float last left zero and stayed there — what "open for N days" counts from.
        /// <para>
        /// Read backwards from the newest movement, undoing one at a time, the answer is the first
        /// movement that started from a zero balance. That bounds the work by how long the money
        /// has been outstanding rather than by how much has ever passed through the float: a person
        /// who settles up each month is answered by one small page no matter how many years of
        /// receipts sit behind them.
        /// </para>
        /// </summary>
        private async Task<DateTime?> OutstandingSinceAsync(
            StaffAccountRow account,
            decimal currentBalance,
            DateTime? openingDate,
            CancellationToken cancellationToken)
        {
            var newestFirst = NewestFirst(LedgerQuery([account.Id]));
            var balanceAfter = currentBalance;
            for (var skip = 0; ; skip += AgingPageSize)
            {
                var page = await newestFirst.Skip(skip).Take(AgingPageSize)
                    .Select(r => new { r.Date, r.Amount })
                    .ToListAsync(cancellationToken);
                foreach (var row in page)
                {
                    var balanceBefore = Money(balanceAfter - row.Amount);
                    if (balanceBefore == 0m) return row.Date.Date;
                    balanceAfter = balanceBefore;
                }
                if (page.Count < AgingPageSize) break;
            }

            // The balance never came back to zero, so what is outstanding traces to the opening
            // balance the float was set up with.
            return (openingDate ?? account.CreatedAt).Date;
        }

        private async Task<StaffCashHistoryItemDto> GetTransferHistoryAsync(
            int staffFinanceAccountId,
            int transferId,
            CancellationToken cancellationToken)
        {
            var ledger = LedgerQuery([staffFinanceAccountId]);
            var row = await ledger.SingleAsync(
                r => r.RecordType == "Transfer" && r.RecordId == transferId, cancellationToken);
            var opening = await _context.FinanceAccounts.AsNoTracking()
                .Where(a => a.Id == staffFinanceAccountId)
                .Select(a => a.OpeningBalance)
                .SingleAsync(cancellationToken);
            // The balance this movement left behind, summed in the database rather than by
            // replaying the float's whole history to find one row.
            var upToAndIncluding = await AtOrBefore(ledger, row)
                .SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m;
            return MapHistory(row, Money(opening + upToAndIncluding));
        }

        /// <summary>Every movement at or before <paramref name="anchor"/> in ledger order.</summary>
        private static IQueryable<LedgerRow> AtOrBefore(IQueryable<LedgerRow> ledger, LedgerRow anchor)
        {
            DateTime date = anchor.Date, createdAt = anchor.CreatedAt;
            int sortOrder = anchor.SortOrder, recordId = anchor.RecordId, typeOrder = anchor.TypeOrder;
            return ledger.Where(r =>
                r.Date < date
                || (r.Date == date
                    && (r.CreatedAt < createdAt
                        || (r.CreatedAt == createdAt
                            && (r.SortOrder < sortOrder
                                || (r.SortOrder == sortOrder
                                    && (r.RecordId < recordId
                                        || (r.RecordId == recordId && r.TypeOrder <= typeOrder))))))));
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

        private static StaffCashHistoryItemDto MapHistory(LedgerRow row, decimal runningBalance) => new()
        {
            RecordType = row.RecordType, RecordId = row.RecordId, Kind = row.Kind,
            Date = row.Date, Description = row.Description, Reference = row.Reference,
            ProjectName = row.ProjectName, Amount = Money(row.Amount), GrossAmount = Money(row.GrossAmount),
            WhtAmount = Money(row.WhtAmount), RunningBalance = Money(runningBalance),
            MovementType = row.MovementType,
            CounterpartyFinanceAccountId = row.CounterpartyFinanceAccountId,
            CounterpartyFinanceAccountName = row.CounterpartyFinanceAccountName,
            Note = row.Note,
            // Only transfers can be corrected here; an expense is edited on the expense screen and
            // carries no token of its own on this ledger.
            ConcurrencyToken = row.RowVersion == null ? null : Convert.ToBase64String(row.RowVersion)
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

        /// <summary>One movement across a staff float, shaped so transfers and expenses can be
        /// unioned into a single ordered, pageable stream in the database.</summary>
        private sealed class LedgerRow
        {
            public int AccountId { get; set; }
            public string RecordType { get; set; } = string.Empty;

            /// <summary>Breaks ties between a transfer and an expense that share an id.</summary>
            public int TypeOrder { get; set; }
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
            public StaffCashMovementType? MovementType { get; set; }
            public int? CounterpartyFinanceAccountId { get; set; }
            public string? CounterpartyFinanceAccountName { get; set; }
            public string? Note { get; set; }
            public byte[]? RowVersion { get; set; }
        }

        private sealed class LedgerTotals
        {
            public int AccountId { get; set; }
            public decimal Net { get; set; }
            public int Count { get; set; }
            public DateTime? LastActivityDate { get; set; }
        }
    }
}
