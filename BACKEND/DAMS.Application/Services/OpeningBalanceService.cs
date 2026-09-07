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
            FinanceDateRules.EnsureBaselineDate(asAtDate, "Go-live date");
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
            // A mistyped go-live date has to be correctable. Only one set may exist, so without this
            // a typo in the month left the client with a draft it could neither use nor replace, and
            // recovering meant editing the database by hand. Omitted means unchanged: the panel that
            // only edits amounts must not blank the date it never sent.
            string? dateChange = null;
            if (dto.AsAtDate.HasValue && dto.AsAtDate.Value.Date != set.AsAtDate.Date)
            {
                // Correctable only while the date has NEVER been committed, which is a stricter
                // test than "not currently committed". Reopening deliberately leaves CommittedAt
                // populated so the old baseline stays the ACTIVE one while the accountant edits
                // amounts — FinanceDateRules.BaselineAsync selects on CommittedAt != null, not on
                // IsCommitted. Letting the date move under that retires the live baseline to a date
                // the amounts on this sheet were never measured at: every posting between the two
                // dates changes meaning immediately, before anything is recommitted, and the
                // recommit that would have reconciled them can itself fail and strand the database
                // in exactly that state. Amounts may still be corrected; the date may not.
                if (set.CommittedAt.HasValue)
                    throw new InvalidOperationException(
                        $"The go-live date was committed on {set.CommittedAt:dd MMM yyyy} and cannot be "
                        + "changed. Reopening lets you correct the opening amounts, but the baseline "
                        + $"date stays {set.AsAtDate:dd MMM yyyy} — it is still the date every report "
                        + "and every posting-date check is measured against, and moving it would "
                        + "silently re-date financial history already recorded on top of it.");
                FinanceDateRules.EnsureBaselineDate(dto.AsAtDate.Value, "Go-live date");
                dateChange = $"Go-live date changed from {set.AsAtDate:dd MMM yyyy} to {dto.AsAtDate.Value:dd MMM yyyy}.";
                set.AsAtDate = dto.AsAtDate.Value.Date;
            }
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
            set.AuditEntries.Add(new OpeningBalanceAuditEntry
            {
                // The date is on the audit line because moving it moves the baseline every report is
                // measured from — a bigger change than any amount on the sheet.
                Action = "Saved", UserId = userId, OccurredAt = DateTime.UtcNow, Note = dateChange
            });
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

            // Balancing internally proves the figures were typed correctly; it says nothing about
            // whether they sit cleanly on top of what DAMS already holds. Every report reads this
            // baseline as an as-at position and then adds every movement ever recorded, so a record
            // dated before the cutover is counted twice — once inside the figure the accountant typed
            // and again as a movement on top of it. Both halves look right in isolation, the sheet
            // still balances, and nothing downstream can tell. The commit is the only point where the
            // question is still answerable, so it is a precondition here.
            var preBaseline = await FinanceDateRules.PreBaselineEventsAsync(_context, set.AsAtDate, cancellationToken);
            if (preBaseline.Count > 0)
            {
                var earliest = preBaseline.Min(p => p.Earliest);
                throw new InvalidOperationException(
                    $"DAMS already holds financial records dated before {set.AsAtDate:dd MMM yyyy}: "
                    + string.Join(", ", preBaseline.Select(p => $"{p.Count} {p.Label}"))
                    + $". The earliest is dated {earliest:dd MMM yyyy}. Committing would count every one "
                    + "of them twice — once inside these opening balances and again as a movement on top "
                    + "of them. Either move the go-live date to "
                    + $"{earliest:dd MMM yyyy} or earlier and enter the balances as at that date, or remove "
                    + "the records that the opening figures already contain. "
                    + FinanceDateRules.BoundaryConvention);
            }

            // Customer Deposits, Customer Receivables and Commission Payable are the balances on this
            // sheet that are NOT free-standing GL figures: every one of them is derived, per booking,
            // from Payment rows, recognition events or commission accruals. An aggregate opening
            // figure has no booking behind it, so nothing can ever clear it — give possession later
            // and the receivable is raised for the FULL sale value while the opening deposit sits
            // untouched, because the clearing side counts payments and there are none; pay a
            // commission in full and the payout clears only the accrual it belongs to, leaving the
            // aggregate owed for ever. The sheet still balances; the customer is simply chased for
            // money they already paid, and the partner still shows as owed. The cutover fix is data,
            // not code: enter those bookings' receipts as Payment rows and their commissions as
            // commission records, and leave these accounts at zero — either behind an earlier
            // go-live date on the dates the money moved, or on or after go-live with that money left
            // out of the opening bank figure. This is the only moment that choice is still
            // reversible, which is why it is a precondition here.
            var unbacked = set.Entries
                .Where(e => e.FinanceAccount != null
                    && e.FinanceAccount.SystemRole is FinanceSystemAccountRole.CustomerDeposits
                        or FinanceSystemAccountRole.CustomerReceivables
                        or FinanceSystemAccountRole.CommissionPayable
                    && (e.DebitAmount != 0m || e.CreditAmount != 0m))
                .ToList();
            if (unbacked.Count > 0)
                throw new InvalidOperationException(
                    "These opening balances cannot be entered as a single figure: "
                    + string.Join(", ", unbacked.Select(e =>
                        $"{e.FinanceAccount!.Name} {Money(e.DebitAmount + e.CreditAmount):N2}"))
                    + ". DAMS works each of these balances out per booking from the customer's own "
                    + "payments and possession date, or from the commissions agreed on the booking, "
                    + "so an aggregate here belongs to no booking and can never be cleared — the "
                    + "buyer would be invoiced again at possession for money already paid, and a "
                    + "partner commission would stay owed after it was paid in full. Enter each "
                    + "affected booking's receipts as payments and its commissions as commission "
                    + "records instead, and leave these accounts at zero. Either move the go-live "
                    + "date back before those movements and enter them on the dates they happened, "
                    + "or enter them on or after the go-live date and leave that money out of the "
                    + "opening bank figure — what must not happen is the same money appearing in an "
                    + "opening balance and as a movement.");

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
            var loans = await _context.Loans.AsNoTracking().Include(l => l.Transactions).ToListAsync(cancellationToken);
            foreach (var loan in loans)
            {
                var opening = accounts.Single(a => a.Id == loan.FinanceAccountId).OpeningBalance;
                if (opening < 0m) throw new InvalidOperationException($"{loan.Name} cannot have a negative opening balance.");
                var running = opening;
                foreach (var day in loan.Transactions.GroupBy(t => t.Date.Date).OrderBy(g => g.Key))
                {
                    running += day.Sum(t => t.Type == LoanTransactionType.Drawdown
                        ? t.PrincipalAmount : -t.PrincipalAmount);
                    if (running < 0m)
                        throw new InvalidOperationException($"The opening balance would make {loan.Name} negative on {day.Key:dd MMM yyyy}.");
                }
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
