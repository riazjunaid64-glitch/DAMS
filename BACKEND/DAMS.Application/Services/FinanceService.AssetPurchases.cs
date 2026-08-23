using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    /// <summary>
    /// Recording the purchase of a fixed asset — something the company keeps.
    /// <para>
    /// Mechanically this is the expense flow: same form fields, same attachment handling, same
    /// withholding. What differs is what the company is left holding. An expense credits cash and is
    /// gone. A purchase credits cash and DEBITS A FIXED-ASSET ACCOUNT, so the asset stays on the
    /// Balance Sheet at cost.
    /// </para>
    /// <para>
    /// The books hold exactly one entry per purchase — Dr Fixed Asset, Cr the paying account (and Cr
    /// tax payable for anything withheld). Nothing here writes a second entry, and the asset account
    /// is never written down.
    /// </para>
    /// <para>
    /// The client's rule is that the purchase reduces Net Profit immediately, and DAMS has exactly one
    /// Net Profit: the rule is applied in <c>GetSummaryAsync</c>, in the Net Profit drill-down, and in
    /// the formal P&amp;L and its export, which all report the same figure for the same period.
    /// </para>
    /// <para>
    /// It is NOT applied to the Trial Balance or to the Balance Sheet's retained profit. Those two are
    /// double-entry positions and the asset above is still carried at full cost, so charging it there
    /// as well needs a credit no account has been approved to take — the deduction would not make them
    /// righter, it would put them out by exactly this amount. They disclose it instead
    /// (<c>BalanceSheetDto.UnpostedFixedAssetCharge</c>); no equity reserve, contra-asset or
    /// depreciation account is invented to absorb it. That ledger decision is the one open item here.
    /// </para>
    /// <para>
    /// Construction / work-in-progress spending does NOT come through here any more. The client
    /// confirmed it is a cost on the day it is paid, so it is recorded as an ordinary expense under
    /// its construction head. <see cref="IFinanceAccountService.EnsureAssetAccountAsync"/> refuses a
    /// work-in-progress destination for that reason; rows that already point at one stay editable so
    /// their history survives.
    /// </para>
    /// </summary>
    public partial class FinanceService
    {
        /// <summary>
        /// One fixed-asset purchase, in the same shape the drill-down list uses — concurrency token
        /// included, so an editor opened from the Total Expenses breakdown can actually save.
        /// Null when the id is not a purchase (or was deleted since the list was drawn).
        /// </summary>
        public async Task<AssetPurchaseLineDto?> GetAssetPurchaseAsync(int id, CancellationToken cancellationToken = default) =>
            await _context.AssetPurchases.AsNoTracking()
                .Where(p => p.Id == id)
                .Select(AssetPurchaseLineProjection)
                .SingleOrDefaultAsync(cancellationToken);

        public async Task<PagedResult<AssetPurchaseLineDto>> GetAssetPurchasePageAsync(
            int? projectId, DateTime? from, DateTime? to, int skip, int take,
            int? assetAccountId = null, int? accountId = null, bool unassigned = false,
            CancellationToken cancellationToken = default)
        {
            var rows = await AssetPurchaseQuery(projectId, from?.Date, ExclusiveEnd(to), assetAccountId, accountId, unassigned)
                .OrderByDescending(p => p.Date)
                .ThenByDescending(p => p.Id)
                .Skip(skip).Take(take + 1)
                .Select(AssetPurchaseLineProjection)
                .ToListAsync(cancellationToken);

            return Page(rows, take);
        }

        /// <summary>
        /// The single definition of a purchase row, shared by the list and the by-id lookup so the
        /// editor cannot be handed a differently-shaped record than the row it was opened from.
        /// </summary>
        private static readonly System.Linq.Expressions.Expression<Func<AssetPurchase, AssetPurchaseLineDto>> AssetPurchaseLineProjection =
            p => new AssetPurchaseLineDto
                {
                    Id = p.Id,
                    Date = p.Date,
                    ProjectId = p.ProjectId,
                    ProjectName = p.Project != null ? p.Project.ProjectName : "General",
                    AssetAccountId = p.AssetAccountId,
                    AssetAccountName = p.AssetAccount!.Name,
                    FinanceAccountId = p.FinanceAccountId,
                    FinanceAccountName = p.FinanceAccount!.Name,
                    AccountHolderName = p.FinanceAccount.AccountHolderName,
                    ItemName = p.ItemName,
                    Category = p.Category,
                    CategoryId = p.CategoryId,
                    Description = p.Description,
                    Vendor = p.Vendor,
                    VendorId = p.VendorId,
                    Amount = p.Amount,
                    WhtAmount = p.WhtAmount,
                    WhtRate = p.WhtRate,
                    NetPaid = p.Amount - p.WhtAmount,
                    WhtTaxSection = p.WhtTaxSection,
                    ConcurrencyToken = Convert.ToBase64String(p.RowVersion),
                    Attachment = p.Attachment == null ? null : new FinanceAttachmentDto
                    {
                        FileName = p.Attachment.OriginalFileName,
                        ContentType = p.Attachment.ContentType,
                        FileSize = p.Attachment.FileSize,
                        UploadedAt = p.Attachment.UploadedAt
                    }
                };

        public async Task<AssetPurchaseResponseDto> CreateAssetPurchaseAsync(
            CreateAssetPurchaseDto dto,
            int? adminUserId,
            FinanceAttachmentUpload? attachment = null,
            CancellationToken cancellationToken = default)
        {
            await EnsureProjectExistsAsync(dto.ProjectId, cancellationToken);
            await _accountService.EnsureSelectableAsync(dto.FinanceAccountId, null, cancellationToken);
            await _accountService.EnsureAssetAccountAsync(dto.AssetAccountId, null, cancellationToken);

            var purchaseDate = await FinanceDateRules.ResolveAsync(_context, dto.Date, "Purchase date", cancellationToken);

            var purchase = new AssetPurchase
            {
                ProjectId = dto.ProjectId,
                AssetAccountId = dto.AssetAccountId,
                FinanceAccountId = dto.FinanceAccountId,
                Amount = dto.Amount,
                Description = Clean(dto.Description),
                Date = purchaseDate,
                CreatedByUserId = adminUserId,
                CreatedAt = DateTime.UtcNow
            };

            string? newStoredFileName = null;
            if (attachment != null)
            {
                var saved = await SaveAttachmentAsync(attachment, cancellationToken);
                newStoredFileName = saved.StoredFileName;
                purchase.Attachment = new FinanceAttachment
                {
                    StoredFileName = saved.StoredFileName,
                    OriginalFileName = saved.Metadata.OriginalFileName,
                    ContentType = saved.Metadata.ContentType,
                    FileSize = saved.Metadata.FileSize,
                    UploadedAt = DateTime.UtcNow
                };
            }

            try
            {
                await ExecuteResilientlyAsync(async () =>
                {
                    // Same guard as expenses: the year-to-date read and the insert that depends on it
                    // must not interleave with another save for the same vendor.
                    await using var thresholdGuard = await BeginThresholdGuardAsync(
                        new[] { dto.VendorId }, cancellationToken);
                    await ApplyAssetPurchaseDetailsAsync(purchase, dto, cancellationToken);
                    _context.AssetPurchases.Add(purchase);
                    await _context.SaveChangesAsync(cancellationToken);
                    if (thresholdGuard != null)
                        await thresholdGuard.CommitAsync(cancellationToken);
                });
            }
            catch
            {
                await DeleteNewFileAfterFailureAsync(newStoredFileName);
                throw;
            }

            return await MapAssetPurchaseAsync(purchase);
        }

        public async Task<AssetPurchaseResponseDto> UpdateAssetPurchaseAsync(
            int id,
            UpdateAssetPurchaseDto dto,
            FinanceAttachmentUpload? attachment = null,
            bool removeAttachment = false,
            CancellationToken cancellationToken = default)
        {
            ValidateAttachmentChange(attachment, removeAttachment);
            var purchase = await _context.AssetPurchases
                .Include(p => p.Attachment)
                .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("Asset purchase not found.");
            ApplyAssetPurchaseToken(purchase, dto.ConcurrencyToken);

            await EnsureProjectExistsAsync(dto.ProjectId, cancellationToken);
            await _accountService.EnsureSelectableAsync(dto.FinanceAccountId, purchase.FinanceAccountId, cancellationToken);
            await _accountService.EnsureAssetAccountAsync(dto.AssetAccountId, purchase.AssetAccountId, cancellationToken);

            // Before the threshold transaction opens and before any upload is written: a rejected
            // date should cost neither a lock nor an orphaned file. An omitted date keeps the one
            // the row already carries.
            var purchaseDate = dto.Date.HasValue
                ? await FinanceDateRules.ResolveAsync(_context, dto.Date, "Purchase date", cancellationToken)
                : purchase.Date;

            var oldStoredFileName = purchase.Attachment?.StoredFileName;
            string? newStoredFileName = null;
            (string StoredFileName, ValidatedFinanceAttachment Metadata)? savedAttachment = null;
            if (attachment != null)
            {
                savedAttachment = await SaveAttachmentAsync(attachment, cancellationToken);
                newStoredFileName = savedAttachment.Value.StoredFileName;
            }

            // Tax withheld from a capital supplier is owed to FBR on the same terms as an expense, so
            // reducing it needs the same check against what has already been deposited.
            var withheldBefore = purchase.WhtAmount;

            try
            {
                await ExecuteResilientlyAsync(async () =>
                {
                    await using var thresholdGuard = await BeginThresholdGuardAsync(
                        new[] { purchase.VendorId, dto.VendorId }, cancellationToken,
                        serialisable: withheldBefore > 0m);

                    if (savedAttachment.HasValue)
                    {
                        purchase.Attachment ??= new FinanceAttachment { AssetPurchaseId = purchase.Id };
                        ApplyAttachment(purchase.Attachment, savedAttachment.Value);
                    }
                    else if (removeAttachment && purchase.Attachment != null)
                    {
                        _context.FinanceAttachments.Remove(purchase.Attachment);
                        purchase.Attachment = null;
                    }

                    purchase.ProjectId = dto.ProjectId;
                    purchase.AssetAccountId = dto.AssetAccountId;
                    purchase.FinanceAccountId = dto.FinanceAccountId;
                    purchase.Amount = dto.Amount;
                    purchase.Description = Clean(dto.Description);
                    purchase.Date = purchaseDate;
                    // After the date and amount have moved, because both feed the threshold and so the tax.
                    await ApplyAssetPurchaseDetailsAsync(purchase, dto, cancellationToken);
                    await _whtService.EnsureDepositsStayCoveredAsync(
                        purchase.WhtAmount - withheldBefore, cancellationToken);

                    await _context.SaveChangesAsync(cancellationToken);
                    if (thresholdGuard != null)
                        await thresholdGuard.CommitAsync(cancellationToken);
                });
            }
            catch
            {
                await DeleteNewFileAfterFailureAsync(newStoredFileName);
                throw;
            }

            if ((attachment != null || removeAttachment) && oldStoredFileName != null)
                await DeleteObsoleteFileAsync(oldStoredFileName);

            return await MapAssetPurchaseAsync(purchase);
        }

        public async Task DeleteAssetPurchaseAsync(int id, string concurrencyToken, CancellationToken cancellationToken = default)
        {
            var purchase = await _context.AssetPurchases
                .Include(p => p.Attachment)
                .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("Asset purchase not found.");
            ApplyAssetPurchaseToken(purchase, concurrencyToken);

            var storedFileName = purchase.Attachment?.StoredFileName;
            await ExecuteResilientlyAsync(async () =>
            {
                // Deleting lowers the vendor's year-to-date total, which is the same aggregate a save
                // reads — so it takes the lock for the same reason deleting an expense does.
                await using var thresholdGuard = await BeginThresholdGuardAsync(
                    new[] { purchase.VendorId }, cancellationToken, serialisable: purchase.WhtAmount > 0m);
                await _whtService.EnsureDepositsStayCoveredAsync(-purchase.WhtAmount, cancellationToken);
                _context.AssetPurchases.Remove(purchase);
                await _context.SaveChangesAsync(cancellationToken);
                if (thresholdGuard != null)
                    await thresholdGuard.CommitAsync(cancellationToken);
            });
            await DeleteObsoleteFileAsync(storedFileName);
        }

        /// <summary>
        /// Resolves the tax head and supplier a purchase was entered against, snapshots their names
        /// onto the row, then hands off to the withholding calculation — the asset-side twin of
        /// <see cref="ApplyExpenseDetailsAsync"/>, and deliberately as strict.
        /// </summary>
        private async Task ApplyAssetPurchaseDetailsAsync(
            AssetPurchase purchase, CreateAssetPurchaseDto dto, CancellationToken cancellationToken)
        {
            if (dto.Amount <= 0)
                throw new InvalidOperationException("Amount must be greater than zero.");
            if (string.IsNullOrWhiteSpace(dto.ItemName))
                throw new InvalidOperationException("Describe what was bought.");
            if (dto.ItemName.Trim().Length > 200)
                throw new InvalidOperationException("What was bought cannot exceed 200 characters.");
            purchase.ItemName = dto.ItemName.Trim();

            if (dto.CategoryId.HasValue)
            {
                var category = await _context.ExpenseCategories.AsNoTracking()
                    .Where(c => c.Id == dto.CategoryId.Value)
                    .Select(c => new { c.Id, c.Name, c.IsActive })
                    .SingleOrDefaultAsync(cancellationToken)
                    ?? throw new InvalidOperationException("Selected category does not exist.");
                if (!category.IsActive && purchase.CategoryId != category.Id)
                    throw new InvalidOperationException("Selected category is inactive. Choose an active category.");

                purchase.CategoryId = category.Id;
                purchase.Category = category.Name;
            }
            else
            {
                // No legacy exemption here, unlike expenses: asset purchases did not exist before
                // the managed rate table, so there is no history to keep editable and no reason to
                // let a taxable supplier be paid through a head with no rate behind it.
                throw new InvalidOperationException(
                    "Choose a category from the list. If the head you need is missing, add it under Finance ▸ Settings ▸ Expense heads & rates.");
            }

            if (dto.VendorId.HasValue)
            {
                var vendor = await _context.Vendors.AsNoTracking()
                    .Where(v => v.Id == dto.VendorId.Value)
                    .Select(v => new { v.Id, v.Name, v.IsActive })
                    .SingleOrDefaultAsync(cancellationToken)
                    ?? throw new InvalidOperationException("Selected vendor does not exist.");
                if (!vendor.IsActive && purchase.VendorId != vendor.Id)
                    throw new InvalidOperationException("Selected vendor is inactive. Choose an active vendor.");

                purchase.VendorId = vendor.Id;
                purchase.Vendor = vendor.Name;
            }
            else
            {
                purchase.VendorId = null;
                purchase.Vendor = Clean(dto.Vendor);
            }

            await _whtService.ApplyToAssetPurchaseAsync(
                purchase, dto.WhtRate, dto.WhtAmount, dto.WhtOverrideReason, cancellationToken);
        }

        private IQueryable<AssetPurchase> AssetPurchaseQuery(
            int? projectId, DateTime? fromValue, DateTime? toExclusive,
            int? assetAccountId, int? accountId, bool unassigned)
        {
            var query = _context.AssetPurchases.AsNoTracking().AsQueryable();
            if (projectId.HasValue) query = query.Where(p => p.ProjectId == projectId.Value);
            if (fromValue.HasValue) query = query.Where(p => p.Date >= fromValue.Value);
            if (toExclusive.HasValue) query = query.Where(p => p.Date < toExclusive.Value);
            if (assetAccountId.HasValue) query = query.Where(p => p.AssetAccountId == assetAccountId.Value);
            // A purchase always names the account that paid, so "unassigned" can never match one.
            // Returning nothing is the honest answer; filtering on accountId instead would put
            // purchases into a list the user asked to see only unassigned rows in.
            if (unassigned) return query.Where(_ => false);
            if (accountId.HasValue) query = query.Where(p => p.FinanceAccountId == accountId.Value);
            return query;
        }

        /// <summary>
        /// The purchases the client's profit rule applies to: the ones whose destination is a
        /// fixed-asset account. Used by the dashboard total, the Net Profit drill-down and the P&amp;L's
        /// pending-deduction disclosure, so all three count the same rows.
        /// <para>
        /// Work-in-progress destinations are deliberately outside it. New construction spend cannot
        /// reach this table at all any more (<see cref="IFinanceAccountService.EnsureAssetAccountAsync"/>
        /// refuses a work-in-progress destination, and it is recorded as an ordinary expense instead),
        /// so the only rows excluded here are the ones inherited from the previous ERP, where
        /// construction was accumulated as an asset. Those carry a real historical balance and what
        /// becomes of it is still an open question with the client — charging them to profit would
        /// answer that question by writing the whole inherited balance off, silently, on the day this
        /// shipped.
        /// </para>
        /// </summary>
        private IQueryable<AssetPurchase> FixedAssetChargeQuery(
            int? projectId, DateTime? fromValue, DateTime? toExclusive, int? accountId, bool unassigned) =>
            AssetPurchaseQuery(projectId, fromValue, toExclusive, null, accountId, unassigned)
                .Where(p => p.AssetAccount!.Type == FinanceAccountType.FixedAsset);

        private async Task<AssetPurchaseResponseDto> MapAssetPurchaseAsync(AssetPurchase p)
        {
            var paidFrom = await GetAccountIdentityAsync(p.FinanceAccountId);
            var assetAccount = await GetAccountIdentityAsync(p.AssetAccountId);
            return new AssetPurchaseResponseDto
            {
                Id = p.Id,
                ProjectId = p.ProjectId,
                ProjectName = await GetProjectNameAsync(p.ProjectId),
                AssetAccountId = p.AssetAccountId,
                AssetAccountName = assetAccount?.Name,
                FinanceAccountId = p.FinanceAccountId,
                FinanceAccountName = paidFrom?.Name,
                AccountHolderName = paidFrom?.Holder,
                Amount = p.Amount,
                ItemName = p.ItemName,
                Description = p.Description,
                Category = p.Category,
                CategoryId = p.CategoryId,
                Vendor = p.Vendor,
                VendorId = p.VendorId,
                Date = p.Date,
                WhtApplied = p.WhtApplied,
                WhtRate = p.WhtRate,
                WhtAmount = p.WhtAmount,
                NetPaid = p.NetPaid,
                WhtRateOverridden = p.WhtRateOverridden,
                WhtOverrideReason = p.WhtOverrideReason,
                WhtTaxSection = p.WhtTaxSection,
                VendorFilerStatusAtEntry = p.VendorFilerStatusAtEntry,
                CreatedAt = p.CreatedAt,
                Attachment = MapAttachment(p.Attachment),
                ConcurrencyToken = Convert.ToBase64String(p.RowVersion)
            };
        }

        private void ApplyAssetPurchaseToken(AssetPurchase purchase, string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                if (purchase.RowVersion.Length == 0) return;
                throw new DbUpdateConcurrencyException("The asset purchase version is missing. Refresh and try again.");
            }

            try
            {
                _context.Entry(purchase).Property(p => p.RowVersion).OriginalValue = Convert.FromBase64String(token);
            }
            catch (FormatException)
            {
                throw new DbUpdateConcurrencyException("The asset purchase version is invalid. Refresh and try again.");
            }
        }

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
