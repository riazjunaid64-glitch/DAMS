using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DAMS.Application.Services
{
    public sealed class CapitalPartnerService : ICapitalPartnerService
    {
        private const decimal ShareTolerance = 0.01m;
        private readonly AppDbContext _context;
        private readonly IFinanceAccountService _accounts;
        private readonly IFinanceAttachmentWriter _attachments;
        public CapitalPartnerService(AppDbContext context, IFinanceAccountService accounts, IFinanceAttachmentWriter attachments)
        {
            _context = context;
            _accounts = accounts;
            _attachments = attachments;
        }

        public async Task<List<CapitalPartnerDto>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken = default)
        {
            return await LoadPartnersAsync(
                _context.CapitalPartners.AsNoTracking().Where(p => includeInactive || p.IsActive)
                    .OrderByDescending(p => p.IsActive).ThenBy(p => p.Name),
                cancellationToken);
        }

        public async Task<CapitalPartnerDto> CreateAsync(SaveCapitalPartnerDto dto, CancellationToken cancellationToken = default)
        {
            Validate(dto);
            await EnsureNameUnique(dto.Name, null, cancellationToken);
            await ValidateCapitalAccount(dto.FinanceAccountId, null, cancellationToken);
            await ValidateProspectiveTotal(null, dto.ProfitSharePercent, dto.IsActive, false, cancellationToken);
            var partner = new CapitalPartner
            {
                Name = dto.Name.Trim(), Cnic = Clean(dto.Cnic), Ntn = Clean(dto.Ntn),
                ProfitSharePercent = Share(dto.ProfitSharePercent), FinanceAccountId = dto.FinanceAccountId,
                IsActive = dto.IsActive, JoinedDate = dto.JoinedDate?.Date, ExitedDate = dto.ExitedDate?.Date,
                CreatedAt = DateTime.UtcNow
            };
            _context.CapitalPartners.Add(partner);
            await _context.SaveChangesAsync(cancellationToken);
            return await GetAsync(partner.Id, cancellationToken);
        }

        public async Task<CapitalPartnerDto> UpdateAsync(int id, SaveCapitalPartnerDto dto, CancellationToken cancellationToken = default)
        {
            Validate(dto);
            // "Has this partner any history?" and "write the new link" have to be one indivisible
            // step. The RowVersion cannot make them one: recording a movement inserts into
            // CapitalTransactions and never touches the partner row, so the token this update
            // carries is still current even though the answer it was checked against has changed.
            // Two callers therefore interleave cleanly — the guard reads no movements, a
            // contribution is recorded, and the relink commits on top of it — which is the very
            // history rewrite the guard exists to refuse. RecordTransactionAsync takes the same
            // row lock, so one of the two now always sees the other.
            await ExecuteResilientlyAsync(async () =>
            {
                await using var guard = await BeginPartnerGuardAsync(id, cancellationToken);
                // Read from the database rather than from whatever the tracked entity holds: the
                // execution strategy can re-run this delegate, and on a second pass the tracked
                // partner already carries the values the first pass assigned.
                var before = await _context.CapitalPartners.AsNoTracking().Where(p => p.Id == id)
                    .Select(p => new { p.FinanceAccountId, p.IsActive }).SingleOrDefaultAsync(cancellationToken)
                    ?? throw new InvalidOperationException("Capital partner not found.");
                var partner = await _context.CapitalPartners.SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
                    ?? throw new InvalidOperationException("Capital partner not found.");
                ApplyToken(partner, dto.ConcurrencyToken);
                await EnsureCapitalAccountNotRewritten(id, before.FinanceAccountId, dto.FinanceAccountId, cancellationToken);
                await EnsureNameUnique(dto.Name, id, cancellationToken);
                await ValidateCapitalAccount(dto.FinanceAccountId, id, cancellationToken);
                await ValidateProspectiveTotal(id, dto.ProfitSharePercent, dto.IsActive, before.IsActive, cancellationToken);
                partner.Name = dto.Name.Trim(); partner.Cnic = Clean(dto.Cnic); partner.Ntn = Clean(dto.Ntn);
                partner.ProfitSharePercent = Share(dto.ProfitSharePercent); partner.FinanceAccountId = dto.FinanceAccountId;
                partner.IsActive = dto.IsActive; partner.JoinedDate = dto.JoinedDate?.Date; partner.ExitedDate = dto.ExitedDate?.Date;
                await _context.SaveChangesAsync(cancellationToken);
                if (guard != null) await guard.CommitAsync(cancellationToken);
            });
            return await GetAsync(id, cancellationToken);
        }

        public async Task<List<CapitalPartnerDto>> UpdateSharesAsync(SaveCapitalPartnerSharesDto dto, CancellationToken cancellationToken = default)
        {
            if (dto.Partners.Count == 0 || dto.Partners.GroupBy(p => p.Id).Any(g => g.Count() > 1))
                throw new InvalidOperationException("Provide each partner once.");
            var ids = dto.Partners.Select(p => p.Id).ToList();
            var partners = await _context.CapitalPartners.Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
            if (partners.Count != ids.Count) throw new InvalidOperationException("A capital partner no longer exists.");
            foreach (var input in dto.Partners)
            {
                if (input.ProfitSharePercent < 0m || input.ProfitSharePercent > 100m)
                    throw new InvalidOperationException("A profit share must be between 0% and 100%.");
                var partner = partners.Single(p => p.Id == input.Id);
                ApplyToken(partner, input.ConcurrencyToken);
                partner.ProfitSharePercent = Share(input.ProfitSharePercent);
                partner.IsActive = input.IsActive;
            }
            var activeOthers = await _context.CapitalPartners.AsNoTracking()
                .Where(p => p.IsActive && !ids.Contains(p.Id)).SumAsync(p => (decimal?)p.ProfitSharePercent, cancellationToken) ?? 0m;
            var total = activeOthers + partners.Where(p => p.IsActive).Sum(p => p.ProfitSharePercent);
            ValidateShareTotal(total);
            await _context.SaveChangesAsync(cancellationToken);
            return await GetAllAsync(true, cancellationToken);
        }

        public async Task<CapitalPartnerStatementDto> GetStatementAsync(int id, DateTime? from, DateTime? to, CancellationToken cancellationToken = default)
        {
            if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
                throw new InvalidOperationException("From date cannot be after to date.");
            var partner = await _context.CapitalPartners.AsNoTracking().Include(p => p.FinanceAccount)
                .SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("Capital partner not found.");
            var fromDate = from?.Date;
            var toExclusive = to?.Date.AddDays(1);
            var query = _context.CapitalTransactions.AsNoTracking().Where(t => t.CapitalPartnerId == id);
            // Only the signed total is needed before the selected range. Let SQL return one scalar
            // instead of materialising a partner's entire historical statement.
            var before = fromDate.HasValue
                ? await query.Where(t => t.Date < fromDate.Value)
                    .SumAsync(t => (decimal?)(t.Type == CapitalTransactionType.Withdrawal
                        || t.Type == CapitalTransactionType.LossShare ? -t.Amount : t.Amount), cancellationToken) ?? 0m
                : 0m;
            if (fromDate.HasValue) query = query.Where(t => t.Date >= fromDate.Value);
            if (toExclusive.HasValue) query = query.Where(t => t.Date < toExclusive.Value);
            var rows = await query.Include(t => t.Attachment).OrderBy(t => t.Date).ThenBy(t => t.Id).ToListAsync(cancellationToken);
            var transactions = rows.Select(MapTransaction).ToList();
            var baseline = partner.FinanceAccount?.OpeningBalance ?? 0m;
            var baselineDate = await _context.OpeningBalanceSets.AsNoTracking().Where(s => s.CommittedAt != null)
                .Select(s => (DateTime?)s.AsAtDate).SingleOrDefaultAsync(cancellationToken);
            var baselineWithinEnd = !baselineDate.HasValue || !toExclusive.HasValue || baselineDate.Value < toExclusive.Value;
            var baselineBeforeRange = !baselineDate.HasValue || !fromDate.HasValue || baselineDate.Value < fromDate.Value;
            var opening = before;
            if (baselineWithinEnd && baselineBeforeRange) opening += baseline;
            else if (baselineWithinEnd && baseline != 0m)
            {
                transactions.Add(new CapitalTransactionDto
                {
                    Id = -partner.Id, Type = CapitalTransactionType.OpeningBalance, Amount = baseline,
                    Date = baselineDate!.Value, Note = "Committed opening balance"
                });
                transactions = transactions.OrderBy(t => t.Date).ThenBy(t => t.Id).ToList();
            }
            return new CapitalPartnerStatementDto
            {
                PartnerId = partner.Id, PartnerName = partner.Name, From = fromDate, To = to?.Date,
                OpeningBalance = Money(opening), Transactions = transactions,
                ClosingBalance = Money(opening + transactions.Sum(Signed))
            };
        }

        public async Task<CapitalTransactionDto> RecordTransactionAsync(int id, SaveCapitalTransactionDto dto, int? userId,
            FinanceAttachmentUpload? attachment = null, CancellationToken cancellationToken = default)
        {
            if (!Enum.IsDefined(dto.Type)) throw new InvalidOperationException("Capital transaction type is invalid.");
            // Every other type is double-sided: a contribution or withdrawal moves the named bank
            // account, and a profit or loss share is the counterpart of the allocated Net Profit the
            // Trial Balance already carries. An OpeningBalance row is the one type with no other
            // side anywhere — it raises the partner's capital account and nothing else, so each one
            // pushes the Balance Sheet out by its own amount. Opening capital is not recorded here
            // at all: it belongs to the committed opening balance set, which posts it to the capital
            // account against the rest of the baseline. GetStatementAsync shows it from there.
            if (dto.Type == CapitalTransactionType.OpeningBalance)
                throw new InvalidOperationException("Opening capital cannot be recorded as a movement. Enter it in the opening balances for the partner's capital account instead.");
            if (dto.Amount <= 0m) throw new InvalidOperationException("Amount must be greater than zero.");
            if (dto.Amount > 999_999_999_999_999.99m) throw new InvalidOperationException("Amount is outside the supported range.");
            if (dto.Date == default) throw new InvalidOperationException("Transaction date is required.");
            if (dto.Reference?.Trim().Length > 200) throw new InvalidOperationException("Reference cannot exceed 200 characters.");
            if (dto.Note?.Trim().Length > 1000) throw new InvalidOperationException("Note cannot exceed 1000 characters.");
            // A contribution or withdrawal moves cash the same day it is saved, and every capital
            // type moves the partner's Capital balance on the Balance Sheet — so the date takes the
            // same three bounds as an expense or a loan drawdown. See FinanceDateRules: without them
            // next month's contribution inflates today's bank, and one dated before the committed
            // opening balances is counted twice.
            await FinanceDateRules.EnsureAsync(_context, dto.Date, "Transaction date", cancellationToken);
            var partner = await _context.CapitalPartners.AsNoTracking().Include(p => p.FinanceAccount)
                .SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("Capital partner not found.");
            if (!partner.IsActive) throw new InvalidOperationException("Transactions cannot be recorded for an inactive capital partner.");
            if (!partner.FinanceAccountId.HasValue) throw new InvalidOperationException("Link the partner to a capital account first.");
            if (partner.FinanceAccount is not { IsActive: true, Type: FinanceAccountType.Capital })
                throw new InvalidOperationException("The linked capital account must be active and have the Capital type.");
            var movesCash = dto.Type is CapitalTransactionType.Contribution or CapitalTransactionType.Withdrawal;
            if (movesCash && !dto.FinanceAccountId.HasValue)
                throw new InvalidOperationException("A contribution or withdrawal must name the cash or bank account used.");
            if (movesCash) await _accounts.EnsureSelectableAsync(dto.FinanceAccountId!.Value, null, cancellationToken);
            if (!movesCash && dto.FinanceAccountId.HasValue)
                throw new InvalidOperationException("Only contributions and withdrawals use a cash or bank account.");
            if (dto.Type == CapitalTransactionType.ProfitShare)
            {
                var activeTotal = await _context.CapitalPartners.AsNoTracking().Where(p => p.IsActive)
                    .SumAsync(p => (decimal?)p.ProfitSharePercent, cancellationToken) ?? 0m;
                ValidateShareTotal(activeTotal);
            }
            var transaction = new CapitalTransaction
            {
                CapitalPartnerId = id, Type = dto.Type, Amount = Money(dto.Amount), Date = dto.Date.Date,
                FinanceAccountId = dto.FinanceAccountId, Reference = Clean(dto.Reference), Note = Clean(dto.Note),
                ProfitSharePercentSnapshot = dto.Type == CapitalTransactionType.ProfitShare ? partner.ProfitSharePercent : null,
                RecordedByUserId = userId, CreatedAt = DateTime.UtcNow
            };
            // Stored after every validation above and before the save, so a rejected amount or date
            // never leaves a file on disk, and a save that fails takes its upload with it.
            if (attachment != null)
            {
                var stored = await _attachments.StoreAsync(attachment, cancellationToken);
                transaction.Attachment = new FinanceAttachment();
                stored.ApplyTo(transaction.Attachment);
                _context.CapitalTransactions.Add(transaction);
                try
                {
                    await SaveUnderPartnerGuardAsync(id, partner.FinanceAccountId, cancellationToken);
                }
                catch
                {
                    await _attachments.DiscardAsync(stored.StoredFileName);
                    throw;
                }
                return MapTransaction(transaction);
            }
            _context.CapitalTransactions.Add(transaction);
            await SaveUnderPartnerGuardAsync(id, partner.FinanceAccountId, cancellationToken);
            return MapTransaction(transaction);
        }

        public async Task<FinanceAttachmentDownload> GetTransactionAttachmentAsync(
            int partnerId, int transactionId, CancellationToken cancellationToken = default)
        {
            var attachment = await _context.CapitalTransactions.AsNoTracking()
                .Where(t => t.Id == transactionId && t.CapitalPartnerId == partnerId && t.Attachment != null)
                .Select(t => t.Attachment!)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new FileNotFoundException("This capital movement does not have an attachment.");
            var content = await _attachments.OpenAsync(attachment.StoredFileName, cancellationToken)
                ?? throw new FileNotFoundException("The attachment file is missing from storage. Please attach it again.");
            return new FinanceAttachmentDownload
            {
                Content = content,
                FileName = attachment.OriginalFileName,
                ContentType = attachment.ContentType
            };
        }

        public async Task RemoveTransactionAttachmentAsync(
            int partnerId, int transactionId, CancellationToken cancellationToken = default)
        {
            var transaction = await _context.CapitalTransactions.Include(t => t.Attachment)
                .SingleOrDefaultAsync(t => t.Id == transactionId && t.CapitalPartnerId == partnerId, cancellationToken)
                ?? throw new InvalidOperationException("Capital transaction not found.");
            if (transaction.Attachment == null) return;
            var storedFileName = transaction.Attachment.StoredFileName;
            _context.FinanceAttachments.Remove(transaction.Attachment);
            await _context.SaveChangesAsync(cancellationToken);
            await _attachments.ForgetAsync(storedFileName);
        }

        /// <summary>
        /// Commits a pending capital movement while holding the partner's row lock, having first
        /// confirmed the capital account it was validated against is still the one linked.
        /// <para>
        /// The movement carries no capital account of its own: every report attributes its equity
        /// side through <c>CapitalPartner.FinanceAccountId</c> as it reads at the moment of the
        /// read. Recording against a link that has just moved would therefore post this equity to
        /// an account the money never went to. <see cref="UpdateAsync"/> holds the same lock while
        /// it decides whether a relink is allowed, so between the two of them the loser always sees
        /// what the winner did — either the relink is refused because a movement now exists, or the
        /// movement is refused because the link now differs.
        /// </para>
        /// </summary>
        private Task SaveUnderPartnerGuardAsync(int partnerId, int? expectedAccountId, CancellationToken cancellationToken) =>
            ExecuteResilientlyAsync(async () =>
            {
                await using var guard = await BeginPartnerGuardAsync(partnerId, cancellationToken);
                var linkedAccountId = await _context.CapitalPartners.AsNoTracking()
                    .Where(p => p.Id == partnerId).Select(p => p.FinanceAccountId)
                    .SingleOrDefaultAsync(cancellationToken);
                if (linkedAccountId != expectedAccountId)
                    throw new InvalidOperationException(
                        "This partner's capital account changed while the movement was being saved, so it was not recorded. Reload the partner and try again.");
                await _context.SaveChangesAsync(cancellationToken);
                if (guard != null) await guard.CommitAsync(cancellationToken);
            });

        /// <summary>
        /// Locks one partner row for the life of the returned transaction, in the shape
        /// <c>FinanceService.BeginThresholdGuardAsync</c> uses for a vendor. Null on a
        /// non-relational provider, where the tests run single-threaded anyway, and null when the
        /// caller already owns a transaction — the outer one is the window in that case.
        /// </summary>
        private async Task<IDbContextTransaction?> BeginPartnerGuardAsync(int partnerId, CancellationToken cancellationToken)
        {
            if (!_context.Database.IsRelational() || _context.Database.CurrentTransaction != null) return null;
            var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await _context.Database.ExecuteSqlRawAsync(
                    "SELECT TOP 1 1 FROM [CapitalPartners] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {0}",
                    new object[] { partnerId }, cancellationToken);
                return transaction;
            }
            catch
            {
                await transaction.DisposeAsync();
                throw;
            }
        }

        /// <summary>
        /// Runs one transactional unit through the retrying execution strategy. Not optional: the
        /// API registers SQL Server with <c>EnableRetryOnFailure</c>, and EF refuses to run anything
        /// inside a caller-opened transaction unless the whole unit is retriable. The delegate holds
        /// only database work on entities the change tracker already knows about — the attachment is
        /// written to disk outside it, so a retry cannot orphan a second copy.
        /// </summary>
        private Task ExecuteResilientlyAsync(Func<Task> operation) =>
            _context.Database.CreateExecutionStrategy().ExecuteAsync(operation);

        private async Task<CapitalPartnerDto> GetAsync(int id, CancellationToken cancellationToken)
        {
            var partners = await LoadPartnersAsync(
                _context.CapitalPartners.AsNoTracking().Where(p => p.Id == id),
                cancellationToken);
            return partners.SingleOrDefault()
                ?? throw new InvalidOperationException("Capital partner not found.");
        }

        private async Task<List<CapitalPartnerDto>> LoadPartnersAsync(
            IQueryable<CapitalPartner> query,
            CancellationToken cancellationToken)
        {
            // Read the partner rows first, then one conditional aggregate per partner. The old
            // Include loaded every transaction ever recorded just to calculate five totals.
            var partners = await query.Select(partner => new CapitalPartnerRow
            {
                Id = partner.Id, Name = partner.Name, Cnic = partner.Cnic, Ntn = partner.Ntn,
                ProfitSharePercent = partner.ProfitSharePercent, FinanceAccountId = partner.FinanceAccountId,
                FinanceAccountName = partner.FinanceAccount == null ? null : partner.FinanceAccount.Name,
                IsActive = partner.IsActive,
                JoinedDate = partner.JoinedDate, ExitedDate = partner.ExitedDate, CreatedAt = partner.CreatedAt,
                AccountOpeningBalance = partner.FinanceAccount == null ? 0m : partner.FinanceAccount.OpeningBalance,
                RowVersion = partner.RowVersion
            }).ToListAsync(cancellationToken);

            if (partners.Count == 0) return [];

            var partnerIds = partners.Select(partner => partner.Id).ToArray();
            var totals = await _context.CapitalTransactions.AsNoTracking()
                .Where(transaction => partnerIds.Contains(transaction.CapitalPartnerId))
                .GroupBy(transaction => transaction.CapitalPartnerId)
                .Select(group => new CapitalPartnerTotals
                {
                    PartnerId = group.Key,
                    Opening = group.Sum(transaction => transaction.Type == CapitalTransactionType.OpeningBalance
                        ? transaction.Amount : 0m),
                    Contributions = group.Sum(transaction => transaction.Type == CapitalTransactionType.Contribution
                        ? transaction.Amount : 0m),
                    Withdrawals = group.Sum(transaction => transaction.Type == CapitalTransactionType.Withdrawal
                        ? transaction.Amount : 0m),
                    ProfitShare = group.Sum(transaction => transaction.Type == CapitalTransactionType.ProfitShare
                        ? transaction.Amount : 0m),
                    LossShare = group.Sum(transaction => transaction.Type == CapitalTransactionType.LossShare
                        ? transaction.Amount : 0m)
                })
                .ToDictionaryAsync(total => total.PartnerId, cancellationToken);

            return partners.Select(partner =>
            {
                totals.TryGetValue(partner.Id, out var total);
                total ??= new CapitalPartnerTotals();
                var opening = partner.AccountOpeningBalance + total.Opening;
                return new CapitalPartnerDto
                {
                    Id = partner.Id, Name = partner.Name, Cnic = partner.Cnic, Ntn = partner.Ntn,
                    ProfitSharePercent = partner.ProfitSharePercent, FinanceAccountId = partner.FinanceAccountId,
                    FinanceAccountName = partner.FinanceAccountName, IsActive = partner.IsActive,
                    JoinedDate = partner.JoinedDate, ExitedDate = partner.ExitedDate, CreatedAt = partner.CreatedAt,
                    OpeningBalance = Money(opening), Contributions = Money(total.Contributions),
                    Withdrawals = Money(total.Withdrawals), ProfitShare = Money(total.ProfitShare),
                    LossShare = Money(total.LossShare),
                    ClosingBalance = Money(opening + total.Contributions + total.ProfitShare
                        - total.Withdrawals - total.LossShare),
                    ConcurrencyToken = Convert.ToBase64String(partner.RowVersion)
                };
            }).ToList();
        }

        private sealed class CapitalPartnerRow
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string? Cnic { get; set; }
            public string? Ntn { get; set; }
            public decimal ProfitSharePercent { get; set; }
            public int? FinanceAccountId { get; set; }
            public string? FinanceAccountName { get; set; }
            public bool IsActive { get; set; }
            public DateTime? JoinedDate { get; set; }
            public DateTime? ExitedDate { get; set; }
            public DateTime CreatedAt { get; set; }
            public decimal AccountOpeningBalance { get; set; }
            public byte[] RowVersion { get; set; } = [];
        }

        private sealed class CapitalPartnerTotals
        {
            public int PartnerId { get; set; }
            public decimal Opening { get; set; }
            public decimal Contributions { get; set; }
            public decimal Withdrawals { get; set; }
            public decimal ProfitShare { get; set; }
            public decimal LossShare { get; set; }
        }

        private static CapitalTransactionDto MapTransaction(CapitalTransaction transaction) => new()
        {
            Id = transaction.Id, Type = transaction.Type, Amount = transaction.Amount, Date = transaction.Date,
            FinanceAccountId = transaction.FinanceAccountId, Reference = transaction.Reference, Note = transaction.Note,
            ProfitSharePercentSnapshot = transaction.ProfitSharePercentSnapshot, CreatedAt = transaction.CreatedAt,
            Attachment = transaction.Attachment == null ? null : new FinanceAttachmentDto
            {
                FileName = transaction.Attachment.OriginalFileName,
                ContentType = transaction.Attachment.ContentType,
                FileSize = transaction.Attachment.FileSize,
                UploadedAt = transaction.Attachment.UploadedAt
            }
        };

        /// <summary>
        /// The equity side of every capital movement is not stored on the movement — the Trial
        /// Balance and the account ledger both post it to <c>CapitalPartner.FinanceAccountId</c>
        /// as it reads TODAY. The cash side is stored, on the movement's own bank account. So
        /// repointing or clearing a partner's capital account does not change the partner going
        /// forward, it rewrites history: a PKR 100,000 contribution keeps its bank debit and
        /// either lands on an account that never received it, or — when the link is cleared —
        /// disappears from equity altogether, leaving the Balance Sheet out by that amount.
        /// A partner with no history at all against the account can still be linked elsewhere.
        /// <para>
        /// Movements are not the only history. A partner's opening capital is not stored on the
        /// partner either — <see cref="GetStatementAsync"/> and <c>LoadPartnersAsync</c> both read
        /// it from <c>FinanceAccount.OpeningBalance</c> of whichever account is linked today, which
        /// is written once, when the opening balance set is committed. So a partner carrying a
        /// committed opening balance and no movements at all is still holding history: relink it
        /// and that opening capital silently becomes someone else's, or none of it stays anyone's.
        /// </para>
        /// </summary>
        private async Task EnsureCapitalAccountNotRewritten(
            int partnerId, int? currentAccountId, int? newAccountId, CancellationToken cancellationToken)
        {
            if (currentAccountId == newAccountId) return;
            if (await _context.CapitalTransactions.AnyAsync(t => t.CapitalPartnerId == partnerId, cancellationToken))
                throw new InvalidOperationException(newAccountId.HasValue
                    ? "This partner already has capital movements recorded, so its capital account cannot be changed — every past movement would move to the new account. Deactivate this partner and create a new one against the new account instead."
                    : "This partner already has capital movements recorded, so its capital account cannot be removed — the money they put in would vanish from equity while the bank balance kept it. Deactivate the partner instead.");
            if (!currentAccountId.HasValue) return;
            var openingCapital = await _context.FinanceAccounts.AsNoTracking()
                .Where(a => a.Id == currentAccountId.Value)
                .Select(a => (decimal?)a.OpeningBalance).SingleOrDefaultAsync(cancellationToken) ?? 0m;
            if (openingCapital != 0m)
                throw new InvalidOperationException(
                    $"This partner's capital account carries {openingCapital:N2} of committed opening capital, so the account cannot be changed — that opening balance is the partner's own history and would move with the link. Deactivate this partner and create a new one against the new account instead.");
        }

        private async Task ValidateCapitalAccount(int? accountId, int? partnerId, CancellationToken cancellationToken)
        {
            if (!accountId.HasValue) return;
            var account = await _context.FinanceAccounts.AsNoTracking().Where(a => a.Id == accountId).Select(a => new { a.Type, a.IsActive }).SingleOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("Selected capital account does not exist.");
            if (account.Type != FinanceAccountType.Capital) throw new InvalidOperationException("A partner must be linked to a Capital account.");
            if (!account.IsActive) throw new InvalidOperationException("Selected capital account is inactive.");
            if (await _context.CapitalPartners.AnyAsync(p => p.FinanceAccountId == accountId && (!partnerId.HasValue || p.Id != partnerId), cancellationToken))
                throw new InvalidOperationException("This capital account is already linked to another partner.");
        }

        private async Task ValidateProspectiveTotal(int? partnerId, decimal share, bool active, bool wasActive, CancellationToken cancellationToken)
        {
            // Creating or editing an inactive partner does not change the active allocation. This
            // permits staging partners before activating them together with the atomic batch API.
            if (!active && !wasActive) return;
            var total = await _context.CapitalPartners.AsNoTracking()
                .Where(p => p.IsActive && (!partnerId.HasValue || p.Id != partnerId)).SumAsync(p => (decimal?)p.ProfitSharePercent, cancellationToken) ?? 0m;
            if (active) total += Share(share);
            ValidateShareTotal(total);
        }

        private static void ValidateShareTotal(decimal total)
        {
            if (Math.Abs(total - 100m) > ShareTolerance)
                throw new InvalidOperationException($"Active partner profit shares must total 100%. The proposed total is {total:N4}%.");
        }

        private async Task EnsureNameUnique(string name, int? except, CancellationToken cancellationToken)
        {
            var clean = name.Trim();
            if (await _context.CapitalPartners.AnyAsync(p => p.Name == clean && (!except.HasValue || p.Id != except), cancellationToken))
                throw new InvalidOperationException("A capital partner with this name already exists.");
        }

        private void ApplyToken(CapitalPartner partner, string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                if (partner.RowVersion.Length == 0) return;
                throw new InvalidOperationException("The partner version is missing. Refresh and try again.");
            }
            try { _context.Entry(partner).Property(p => p.RowVersion).OriginalValue = Convert.FromBase64String(token); }
            catch (FormatException) { throw new InvalidOperationException("The partner version is invalid. Refresh and try again."); }
        }

        private static void Validate(SaveCapitalPartnerDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) throw new InvalidOperationException("Partner name is required.");
            if (dto.Name.Trim().Length > 200) throw new InvalidOperationException("Partner name cannot exceed 200 characters.");
            if (dto.ProfitSharePercent < 0m || dto.ProfitSharePercent > 100m) throw new InvalidOperationException("Profit share must be between 0% and 100%.");
            if (dto.Cnic?.Trim().Length > 20 || dto.Ntn?.Trim().Length > 30) throw new InvalidOperationException("CNIC or NTN is too long.");
            if (dto.JoinedDate.HasValue && dto.ExitedDate.HasValue && dto.ExitedDate.Value.Date < dto.JoinedDate.Value.Date)
                throw new InvalidOperationException("Exit date cannot be before joined date.");
        }

        private static decimal Signed(CapitalTransaction transaction) => transaction.Type is CapitalTransactionType.Withdrawal or CapitalTransactionType.LossShare ? -transaction.Amount : transaction.Amount;
        private static decimal Signed(CapitalTransactionDto transaction) => transaction.Type is CapitalTransactionType.Withdrawal or CapitalTransactionType.LossShare ? -transaction.Amount : transaction.Amount;
        private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
        private static decimal Share(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
