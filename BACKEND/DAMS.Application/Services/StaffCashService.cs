using DAMS.Application.Common;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Data.Common;
using System.Text;
using System.Text.Json;

namespace DAMS.Application.Services
{
    public sealed class StaffCashService : IStaffCashService
    {
        private const decimal MaximumAmount = 999_999_999_999_999.99m;

        /// <summary>How many movements the aging walk reads at a time. Large enough that a normal
        /// float is answered by one page, small enough that an unusual one still cannot pull a
        /// whole history into memory.</summary>
        private const int AgingPageSize = 200;
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
            var balances = accountRows.ToDictionary(
                a => a.Id,
                a => Money(a.OpeningBalance + (totals.GetValueOrDefault(a.Id)?.Net ?? 0m)));
            var outstandingSince = await OutstandingSinceByAccountAsync(
                accountRows, balances, openingDate, null, cancellationToken);

            // Each holder is built once, from grouped aggregates plus one set-based aging query.
            // A permanent running float must not make the overview replay its whole lifetime every
            // time the page opens.
            var holders = new List<StaffCashHolderDto>(accountRows.Count);
            foreach (var account in accountRows)
                holders.Add(BuildHolder(
                    account,
                    totals.GetValueOrDefault(account.Id),
                    balances[account.Id],
                    outstandingSince.GetValueOrDefault(account.Id)));

            // Every holder counts towards the company totals; includeSettled only decides who is
            // listed. Filtering before summing would hide settled floats from the reconciliation.
            var holderBalances = holders.Select(h => h.CurrentBalance).ToList();
            var cash = (await _accounts.GetOverviewAsync(cancellationToken)).TotalBalance;
            var net = Money(holderBalances.Sum());
            return new StaffCashOverviewDto
            {
                TotalHeldByStaff = Money(holderBalances.Where(x => x > 0m).Sum()),
                TotalOwedToStaff = Money(-holderBalances.Where(x => x < 0m).Sum()),
                NetStaffBalance = net,
                CashAndBankBalance = Money(cash),
                TrackedCompanyCash = Money(cash + net),
                HoldingCount = holderBalances.Count(x => x > 0m),
                OwedCount = holderBalances.Count(x => x < 0m),
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
            return BuildHolder(new StaffAccountRow
            {
                Id = account.Id, Name = account.Name, PersonName = account.AccountHolderName,
                OpeningBalance = account.OpeningBalance, IsActive = account.IsActive,
                CreatedAt = account.CreatedAt
            }, null, 0m, null);
        }

        public async Task<StaffCashStatementDto> GetStatementAsync(
            int staffFinanceAccountId,
            string? cursor,
            int take,
            CancellationToken cancellationToken = default)
        {
            take = Math.Clamp(take, 1, 200);
            var parsedCursor = DecodeCursor(cursor);
            if (parsedCursor != null && parsedCursor.AccountId != staffFinanceAccountId)
                throw new InvalidOperationException("The staff cash page cursor belongs to another person. Refresh and try again.");
            var snapshotUtc = parsedCursor?.SnapshotUtc ?? DateTime.UtcNow;
            var account = await StaffAccounts().SingleOrDefaultAsync(a => a.Id == staffFinanceAccountId, cancellationToken)
                ?? throw new InvalidOperationException("Staff float not found.");
            var ids = new List<int> { staffFinanceAccountId };

            // A statement page must be internally consistent even if another admin records the
            // next receipt while this request is in flight. The snapshot boundary makes all reads
            // talk about the same ledger, and the cursor carries the balance at the next page
            // boundary so older pages never have to re-count a moving offset window.
            var totals = await TotalsByAccountAsync(ids, snapshotUtc, cancellationToken);
            var balance = Money(account.OpeningBalance + (totals.GetValueOrDefault(staffFinanceAccountId)?.Net ?? 0m));
            var outstandingSince = await OutstandingSinceByAccountAsync(
                [account],
                new Dictionary<int, decimal> { [staffFinanceAccountId] = balance },
                await OpeningDateAsync(cancellationToken),
                snapshotUtc,
                cancellationToken);
            var holder = BuildHolder(
                account,
                totals.GetValueOrDefault(staffFinanceAccountId),
                balance,
                outstandingSince.GetValueOrDefault(staffFinanceAccountId));

            var ledger = LedgerQuery(ids, snapshotUtc);
            var pageQuery = parsedCursor == null ? NewestFirst(ledger) : NewestFirst(OlderThan(ledger, parsedCursor));
            var page = await pageQuery.Take(take + 1).ToListAsync(cancellationToken);

            var running = parsedCursor?.BalanceBeforeCursor ?? holder.CurrentBalance;
            var items = new List<StaffCashHistoryItemDto>(Math.Min(page.Count, take));
            foreach (var row in page.Take(take))
            {
                items.Add(MapHistory(row, running));
                running = Money(running - row.Amount);
            }
            var last = page.Take(take).LastOrDefault();
            return new StaffCashStatementDto
            {
                Holder = holder,
                Items = items,
                HasMore = page.Count > take,
                NextCursor = page.Count > take && last != null
                    ? EncodeCursor(new LedgerCursor(staffFinanceAccountId, snapshotUtc, last.Date, last.CreatedAt,
                        last.SortOrder, last.RecordId, last.TypeOrder, running))
                    : null
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

        /// <summary>
        /// Every movement across a staff float — the transfers in and out, and the expenses paid
        /// from it — as one queryable stream. Left as a query rather than a list so callers can
        /// aggregate, order and page it in the database; a float's history only ever grows, so
        /// nothing here may assume it fits in memory.
        /// </summary>
        private IQueryable<LedgerRow> LedgerQuery(List<int> accountIds, DateTime? createdAtOrBefore = null)
        {
            var transfers = _context.StaffCashTransfers.AsNoTracking()
                .Where(t => accountIds.Contains(t.StaffFinanceAccountId));
            if (createdAtOrBefore.HasValue)
                transfers = transfers.Where(t => t.CreatedAt <= createdAtOrBefore.Value);

            var transferRows = transfers
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
                .Where(e => e.FinanceAccountId.HasValue && accountIds.Contains(e.FinanceAccountId.Value));
            if (createdAtOrBefore.HasValue)
                expenses = expenses.Where(e => e.CreatedAt <= createdAtOrBefore.Value);

            var expenseRows = expenses
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
            return transferRows.Concat(expenseRows);
        }

        private async Task<Dictionary<int, DateTime?>> OutstandingSinceByAccountAsync(
            List<StaffAccountRow> accounts,
            Dictionary<int, decimal> balances,
            DateTime? openingDate,
            DateTime? createdAtOrBefore,
            CancellationToken cancellationToken)
        {
            var openAccounts = accounts.Where(a => balances.GetValueOrDefault(a.Id) != 0m).ToList();
            var result = accounts.ToDictionary(a => a.Id, _ => (DateTime?)null);
            if (openAccounts.Count == 0) return result;

            foreach (var account in openAccounts)
                result[account.Id] = (openingDate ?? account.CreatedAt).Date;

            if (_context.Database.IsSqlServer())
            {
                var departures = await OutstandingSinceSqlServerAsync(
                    openAccounts.Select(a => a.Id).ToList(), createdAtOrBefore, cancellationToken);
                foreach (var row in departures)
                    result[row.AccountId] = row.Date.Date;
                return result;
            }

            foreach (var account in openAccounts)
            {
                result[account.Id] = await OutstandingSinceFallbackAsync(
                    account, balances[account.Id], openingDate, createdAtOrBefore, cancellationToken);
            }
            return result;
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

        private static IQueryable<LedgerRow> OlderThan(IQueryable<LedgerRow> ledger, LedgerCursor cursor)
        {
            var date = cursor.Date;
            var createdAt = cursor.CreatedAt;
            var sortOrder = cursor.SortOrder;
            var recordId = cursor.RecordId;
            var typeOrder = cursor.TypeOrder;
            return ledger.Where(r =>
                r.Date < date
                || (r.Date == date
                    && (r.CreatedAt < createdAt
                        || (r.CreatedAt == createdAt
                            && (r.SortOrder < sortOrder
                                || (r.SortOrder == sortOrder
                                    && (r.RecordId < recordId
                                        || (r.RecordId == recordId && r.TypeOrder < typeOrder))))))));
        }

        private static string EncodeCursor(LedgerCursor cursor) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cursor)));

        private static LedgerCursor? DecodeCursor(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            try
            {
                return JsonSerializer.Deserialize<LedgerCursor>(
                    Encoding.UTF8.GetString(Convert.FromBase64String(value)));
            }
            catch (Exception ex) when (ex is FormatException or JsonException)
            {
                throw new InvalidOperationException("The staff cash page cursor is invalid. Refresh and try again.");
            }
        }

        private async Task<Dictionary<int, LedgerTotals>> TotalsByAccountAsync(
            List<int> accountIds,
            DateTime? createdAtOrBefore,
            CancellationToken cancellationToken)
        {
            if (accountIds.Count == 0) return [];
            var rows = await LedgerQuery(accountIds, createdAtOrBefore)
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

        private Task<Dictionary<int, LedgerTotals>> TotalsByAccountAsync(
            List<int> accountIds,
            CancellationToken cancellationToken) =>
            TotalsByAccountAsync(accountIds, null, cancellationToken);

        private StaffCashHolderDto BuildHolder(
            StaffAccountRow account,
            LedgerTotals? totals,
            decimal balance,
            DateTime? outstandingSince)
        {
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
        private async Task<DateTime?> OutstandingSinceFallbackAsync(
            StaffAccountRow account,
            decimal currentBalance,
            DateTime? openingDate,
            DateTime? createdAtOrBefore,
            CancellationToken cancellationToken)
        {
            var newestFirst = NewestFirst(LedgerQuery([account.Id], createdAtOrBefore));
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

        private async Task<List<OutstandingSinceRow>> OutstandingSinceSqlServerAsync(
            List<int> accountIds,
            DateTime? createdAtOrBefore,
            CancellationToken cancellationToken)
        {
            if (accountIds.Count == 0) return [];

            var names = accountIds.Select((_, index) => $"@id{index}").ToArray();
            var inList = string.Join(", ", names);
            var sql = $"""
                WITH [Staff] AS (
                    SELECT [Id], [OpeningBalance]
                    FROM [FinanceAccounts]
                    WHERE [Id] IN ({inList})
                ),
                [Ledger] AS (
                    SELECT
                        [StaffFinanceAccountId] AS [AccountId],
                        [Date],
                        [CreatedAt],
                        CAST([Type] AS int) AS [SortOrder],
                        [Id] AS [RecordId],
                        CAST(0 AS int) AS [TypeOrder],
                        CASE WHEN [Type] = 1 THEN [Amount] ELSE -[Amount] END AS [Amount]
                    FROM [StaffCashTransfers]
                    WHERE [StaffFinanceAccountId] IN ({inList})
                        AND (@asOf IS NULL OR [CreatedAt] <= @asOf)
                    UNION ALL
                    SELECT
                        [FinanceAccountId] AS [AccountId],
                        [Date],
                        [CreatedAt],
                        CAST(3 AS int) AS [SortOrder],
                        [Id] AS [RecordId],
                        CAST(1 AS int) AS [TypeOrder],
                        -([Amount] - [WhtAmount]) AS [Amount]
                    FROM [Expenses]
                    WHERE [FinanceAccountId] IN ({inList})
                        AND (@asOf IS NULL OR [CreatedAt] <= @asOf)
                ),
                [Ordered] AS (
                    SELECT
                        l.[AccountId],
                        l.[Date],
                        l.[CreatedAt],
                        l.[SortOrder],
                        l.[RecordId],
                        l.[TypeOrder],
                        s.[OpeningBalance] + COALESCE(SUM(l.[Amount]) OVER (
                            PARTITION BY l.[AccountId]
                            ORDER BY l.[Date], l.[CreatedAt], l.[SortOrder], l.[RecordId], l.[TypeOrder]
                            ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING
                        ), 0) AS [BalanceBefore]
                    FROM [Ledger] l
                    INNER JOIN [Staff] s ON s.[Id] = l.[AccountId]
                ),
                [Departures] AS (
                    SELECT
                        [AccountId],
                        [Date],
                        ROW_NUMBER() OVER (
                            PARTITION BY [AccountId]
                            ORDER BY [Date] DESC, [CreatedAt] DESC, [SortOrder] DESC, [RecordId] DESC, [TypeOrder] DESC
                        ) AS [Rank]
                    FROM [Ordered]
                    WHERE ROUND([BalanceBefore], 2) = 0
                )
                SELECT [AccountId], [Date]
                FROM [Departures]
                WHERE [Rank] = 1;
                """;

            var connection = _context.Database.GetDbConnection();
            var shouldClose = connection.State != ConnectionState.Open;
            if (shouldClose) await connection.OpenAsync(cancellationToken);

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
                if (_context.Database.GetCommandTimeout() is { } timeout)
                    command.CommandTimeout = timeout;

                for (var index = 0; index < accountIds.Count; index++)
                {
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = names[index];
                    parameter.Value = accountIds[index];
                    parameter.DbType = DbType.Int32;
                    command.Parameters.Add(parameter);
                }

                var asOf = command.CreateParameter();
                asOf.ParameterName = "@asOf";
                asOf.Value = createdAtOrBefore.HasValue ? createdAtOrBefore.Value : DBNull.Value;
                asOf.DbType = DbType.DateTime2;
                command.Parameters.Add(asOf);

                var rows = new List<OutstandingSinceRow>();
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    rows.Add(new OutstandingSinceRow(reader.GetInt32(0), reader.GetDateTime(1)));
                return rows;
            }
            finally
            {
                if (shouldClose) await connection.CloseAsync();
            }
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

        // Same three bounds as every other financial posting date — see FinanceDateRules.
        private Task ValidateDateAsync(DateTime date, CancellationToken cancellationToken) =>
            FinanceDateRules.EnsureAsync(_context, date, "Transfer date", cancellationToken);

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
            FinanceDateRules.BaselineAsync(_context, cancellationToken);

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

        private sealed record LedgerCursor(
            int AccountId,
            DateTime SnapshotUtc,
            DateTime Date,
            DateTime CreatedAt,
            int SortOrder,
            int RecordId,
            int TypeOrder,
            decimal BalanceBeforeCursor);

        private sealed record OutstandingSinceRow(int AccountId, DateTime Date);

        private sealed class LedgerTotals
        {
            public int AccountId { get; set; }
            public decimal Net { get; set; }
            public int Count { get; set; }
            public DateTime? LastActivityDate { get; set; }
        }
    }
}
