using DAMS.Application.Common;
using DAMS.Application.DTOs.ExpenseDtos;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DAMS.Application.Services
{
    public class FinanceService : IFinanceService
    {
        private readonly AppDbContext _context;
        private readonly IFinanceAttachmentStorage _attachmentStorage;
        private readonly IFinanceAccountService _accountService;
        private readonly ILogger<FinanceService> _logger;

        public FinanceService(AppDbContext context, IFinanceAttachmentStorage attachmentStorage, IFinanceAccountService accountService, ILogger<FinanceService> logger)
        {
            _context = context;
            _attachmentStorage = attachmentStorage;
            _accountService = accountService;
            _logger = logger;
        }

        public async Task<FinancialSummaryDto> GetSummaryAsync(int? projectId, DateTime? from, DateTime? to, int? accountId = null, bool unassigned = false)
        {
            var fromValue = from?.Date;
            var toExclusive = to?.Date.AddDays(1);

            // All totals are computed in SQL — no rows are materialised for the cards.
            var automaticRevenue = accountId.HasValue ? 0m : await PaymentsQuery(projectId, fromValue, toExclusive)
                .SumAsync(p => (decimal?)p.Amount) ?? 0m;
            var manualRevenue = await ManualQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .SumAsync(r => (decimal?)r.Amount) ?? 0m;
            var totalExpenses = await ExpenseQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .SumAsync(e => (decimal?)e.Amount) ?? 0m;

            // Outstanding/overdue are balance snapshots (not date-filtered); totals are the
            // sum of the same positive per-row balances shown in the paged tables. Materialise
            // the per-row balances (a small, bounded set — same projection the tables use) and
            // sum the positive ones in memory; filtering a projected scalar in SQL does not
            // translate.
            var accountFilterApplied = accountId.HasValue || unassigned;
            var outstandingTotal = 0m;
            var overdueTotal = 0m;
            if (!accountFilterApplied)
            {
                var outstandingBalances = await OutstandingBookings(projectId)
                    .Select(b => (b.AgreedSalePrice - b.DiscountAmount) - ((decimal?)b.Payments.Sum(p => (decimal?)p.Amount) ?? 0m))
                    .ToListAsync();
                outstandingTotal = outstandingBalances.Where(v => v > 0).Sum();

                var overdueBalances = await OverdueInstallments(projectId)
                    .Select(i => i.Amount - ((decimal?)i.Payments.Sum(p => (decimal?)p.Amount) ?? 0m))
                    .ToListAsync();
                overdueTotal = overdueBalances.Where(v => v > 0).Sum();
            }

            // When a single account is selected, surface its running balance up to the end of
            // the selected period. This is account-wide (project filter is ignored) because the
            // balance is a property of the account, matching the Accounts detail view.
            decimal? accountOpeningBalance = null;
            decimal? accountCurrentBalance = null;
            if (accountId.HasValue)
            {
                var opening = await _context.FinanceAccounts.AsNoTracking()
                    .Where(a => a.Id == accountId.Value)
                    .Select(a => (decimal?)a.OpeningBalance)
                    .SingleOrDefaultAsync();
                if (opening.HasValue)
                {
                    var cumulativeRevenue = await ManualQuery(null, null, toExclusive, accountId, false)
                        .SumAsync(r => (decimal?)r.Amount) ?? 0m;
                    var cumulativeExpenses = await ExpenseQuery(null, null, toExclusive, accountId, false)
                        .SumAsync(e => (decimal?)e.Amount) ?? 0m;
                    accountOpeningBalance = opening.Value;
                    accountCurrentBalance = opening.Value + cumulativeRevenue - cumulativeExpenses;
                }
            }

            var totalRevenue = automaticRevenue + manualRevenue;
            return new FinancialSummaryDto
            {
                AutomaticRevenue = automaticRevenue,
                ManualRevenue = manualRevenue,
                TotalRevenue = totalRevenue,
                TotalExpenses = totalExpenses,
                NetProfit = totalRevenue - totalExpenses,
                OutstandingAmount = outstandingTotal,
                OverdueAmount = overdueTotal,
                AccountOpeningBalance = accountOpeningBalance,
                AccountCurrentBalance = accountCurrentBalance
            };
        }

        // ── Paged table rows ────────────────────────────────────────────────────────
        // Each method fetches `take + 1` rows in SQL (OFFSET/FETCH) so HasMore is known
        // without a separate COUNT query.

        public async Task<PagedResult<RevenueLineDto>> GetRevenuePageAsync(int? projectId, DateTime? from, DateTime? to, int skip, int take, int? accountId = null, bool unassigned = false)
        {
            var fromValue = from?.Date;
            var toExclusive = to?.Date.AddDays(1);

            // Payments and manual revenue are unioned (UNION ALL) into one shape, ordered
            // and paged in SQL. A stable secondary key (Source + entity Id) keeps paging
            // deterministic across chunks.
            var payments = PaymentsQuery(projectId, fromValue, toExclusive).Where(_ => !accountId.HasValue)
                .Select(p => new RevenueRow
                {
                    SortId = p.Id,
                    Date = p.PaidAt,
                    ProjectId = p.Booking.Unit.ProjectId,
                    ProjectName = p.Booking.Unit.Project.ProjectName,
                    Amount = p.Amount,
                    Source = "Payment",
                    ManualRevenueId = null,
                    PaymentType = p.Type,
                    InstallmentType = p.Installment != null ? (InstallmentType?)p.Installment.Type : null,
                    ReceiptNumber = p.ReceiptNumber,
                    BookingReference = p.Booking.BookingReference,
                    CustomerName = p.Booking.Customer.FullName,
                    RevenueType = null,
                    Reference = null,
                    Description = null,
                    AttachmentFileName = null,
                    AttachmentContentType = null,
                    AttachmentFileSize = null,
                    AttachmentUploadedAt = null,
                    FinanceAccountId = null,
                    FinanceAccountName = null,
                    AccountHolderName = null
                });

            var manual = ManualQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Select(r => new RevenueRow
                {
                    SortId = r.Id,
                    Date = r.Date,
                    ProjectId = r.ProjectId,
                    ProjectName = r.Project!.ProjectName,
                    Amount = r.Amount,
                    Source = "Manual Revenue",
                    ManualRevenueId = r.Id,
                    PaymentType = null,
                    InstallmentType = null,
                    ReceiptNumber = null,
                    BookingReference = null,
                    CustomerName = null,
                    RevenueType = r.RevenueType,
                    Reference = r.Reference,
                    Description = r.Description,
                    AttachmentFileName = r.Attachment != null ? r.Attachment.OriginalFileName : null,
                    AttachmentContentType = r.Attachment != null ? r.Attachment.ContentType : null,
                    AttachmentFileSize = r.Attachment != null ? r.Attachment.FileSize : null,
                    AttachmentUploadedAt = r.Attachment != null ? r.Attachment.UploadedAt : null,
                    FinanceAccountId = r.FinanceAccountId,
                    FinanceAccountName = r.FinanceAccount != null ? r.FinanceAccount.Name : null,
                    AccountHolderName = r.FinanceAccount != null ? r.FinanceAccount.AccountHolderName : null
                });

            var raw = await payments.Concat(manual)
                .OrderByDescending(x => x.Date)
                .ThenBy(x => x.Source)
                .ThenByDescending(x => x.SortId)
                .Skip(skip).Take(take + 1)
                .ToListAsync();

            var items = raw.Take(take).Select(r => new RevenueLineDto
            {
                Date = r.Date,
                ProjectId = r.ProjectId,
                ProjectName = r.ProjectName ?? "General",
                Amount = r.Amount,
                Source = r.Source,
                ManualRevenueId = r.ManualRevenueId,
                RevenueType = r.Source == "Payment"
                    ? ResolveAutomaticRevenueType(r.PaymentType!.Value, r.InstallmentType)
                    : (r.RevenueType ?? string.Empty),
                Reference = r.Source == "Payment"
                    ? BuildPaymentReference(r.ReceiptNumber, r.BookingReference ?? string.Empty, r.CustomerName ?? string.Empty)
                    : r.Reference,
                Description = r.Description,
                FinanceAccountId = r.FinanceAccountId,
                FinanceAccountName = r.FinanceAccountName,
                AccountHolderName = r.AccountHolderName,
                Attachment = MapAttachment(r.AttachmentFileName, r.AttachmentContentType, r.AttachmentFileSize, r.AttachmentUploadedAt)
            }).ToList();

            return new PagedResult<RevenueLineDto> { Items = items, HasMore = raw.Count > take };
        }

        public async Task<PagedResult<ExpenseLineDto>> GetExpensePageAsync(int? projectId, DateTime? from, DateTime? to, int skip, int take, int? accountId = null, bool unassigned = false)
        {
            var fromValue = from?.Date;
            var toExclusive = to?.Date.AddDays(1);

            var rows = await ExpenseQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .OrderByDescending(e => e.Date)
                .ThenByDescending(e => e.Id)
                .Skip(skip).Take(take + 1)
                .Select(e => new ExpenseLineDto
                {
                    Id = e.Id,
                    Date = e.Date,
                    ProjectId = e.ProjectId,
                    ProjectName = e.Project != null ? e.Project.ProjectName : "General",
                    Category = e.Category,
                    Amount = e.Amount,
                    Description = e.Description,
                    Reference = e.Vendor,
                    FinanceAccountId = e.FinanceAccountId,
                    FinanceAccountName = e.FinanceAccount != null ? e.FinanceAccount.Name : null,
                    AccountHolderName = e.FinanceAccount != null ? e.FinanceAccount.AccountHolderName : null,
                    Attachment = e.Attachment == null ? null : new FinanceAttachmentDto
                    {
                        FileName = e.Attachment.OriginalFileName,
                        ContentType = e.Attachment.ContentType,
                        FileSize = e.Attachment.FileSize,
                        UploadedAt = e.Attachment.UploadedAt
                    }
                })
                .ToListAsync();

            return Page(rows, take);
        }

        public async Task<PagedResult<OutstandingLineDto>> GetOutstandingPageAsync(int? projectId, int skip, int take)
        {
            var rows = await OutstandingBookings(projectId)
                .Where(b => (b.AgreedSalePrice - b.DiscountAmount) - ((decimal?)b.Payments.Sum(p => (decimal?)p.Amount) ?? 0m) > 0)
                .OrderByDescending(b => (b.AgreedSalePrice - b.DiscountAmount) - ((decimal?)b.Payments.Sum(p => (decimal?)p.Amount) ?? 0m))
                .ThenBy(b => b.Id)
                .Skip(skip).Take(take + 1)
                .Select(b => new OutstandingLineDto
                {
                    BookingReference = b.BookingReference,
                    CustomerName = b.Customer.FullName,
                    ProjectId = b.Unit.ProjectId,
                    ProjectName = b.Unit.Project != null ? b.Unit.Project.ProjectName : "General",
                    UnitNumber = b.Unit.UnitNumber,
                    // Show the net (post-discount) price so the row ties out: price − received = outstanding.
                    AgreedSalePrice = b.AgreedSalePrice - b.DiscountAmount,
                    ReceivedAmount = (decimal?)b.Payments.Sum(p => (decimal?)p.Amount) ?? 0m,
                    OutstandingAmount = (b.AgreedSalePrice - b.DiscountAmount) - ((decimal?)b.Payments.Sum(p => (decimal?)p.Amount) ?? 0m)
                })
                .ToListAsync();

            return Page(rows, take);
        }

        public async Task<PagedResult<OverdueLineDto>> GetOverduePageAsync(int? projectId, int skip, int take)
        {
            // Fetch the page with Type as an enum, then format it in memory (enum.ToString
            // is not reliably translatable to SQL).
            var raw = await OverdueInstallments(projectId)
                .Where(i => i.Amount - ((decimal?)i.Payments.Sum(p => (decimal?)p.Amount) ?? 0m) > 0)
                .OrderBy(i => i.DueDate)
                .ThenBy(i => i.Id)
                .Skip(skip).Take(take + 1)
                .Select(i => new
                {
                    BookingReference = i.Booking.BookingReference,
                    CustomerName = i.Booking.Customer.FullName,
                    ProjectId = (int?)i.Booking.Unit.ProjectId,
                    ProjectName = i.Booking.Unit.Project != null ? i.Booking.Unit.Project.ProjectName : "General",
                    i.Booking.Unit.UnitNumber,
                    i.SequenceNumber,
                    i.Type,
                    i.DueDate,
                    i.Amount,
                    PaidAmount = (decimal?)i.Payments.Sum(p => (decimal?)p.Amount) ?? 0m
                })
                .ToListAsync();

            var items = raw.Take(take).Select(r => new OverdueLineDto
            {
                BookingReference = r.BookingReference,
                CustomerName = r.CustomerName,
                ProjectId = r.ProjectId,
                ProjectName = r.ProjectName,
                UnitNumber = r.UnitNumber,
                SequenceNumber = r.SequenceNumber,
                InstallmentType = r.Type.ToString(),
                DueDate = r.DueDate,
                Amount = r.Amount,
                PaidAmount = r.PaidAmount,
                OverdueAmount = r.Amount - r.PaidAmount
            }).ToList();

            return new PagedResult<OverdueLineDto> { Items = items, HasMore = raw.Count > take };
        }

        public async Task<PagedResult<NetProfitLineDto>> GetNetProfitPageAsync(int? projectId, DateTime? from, DateTime? to, int skip, int take, int? accountId = null, bool unassigned = false)
        {
            var fromValue = from?.Date;
            var toExclusive = to?.Date.AddDays(1);

            // Net Profit = every revenue line (+) and expense line (−). Three sources are
            // unioned, ordered and paged in SQL.
            var payments = PaymentsQuery(projectId, fromValue, toExclusive).Where(_ => !accountId.HasValue)
                .Select(p => new NetProfitRow
                {
                    SortId = p.Id,
                    Date = p.PaidAt,
                    ProjectName = p.Booking.Unit.Project.ProjectName,
                    Kind = "revenue",
                    Amount = p.Amount,
                    IsPayment = true,
                    PaymentType = p.Type,
                    InstallmentType = p.Installment != null ? (InstallmentType?)p.Installment.Type : null,
                    Label = null
                });

            var manual = ManualQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Select(r => new NetProfitRow
                {
                    SortId = r.Id,
                    Date = r.Date,
                    ProjectName = r.Project!.ProjectName,
                    Kind = "revenue",
                    Amount = r.Amount,
                    IsPayment = false,
                    PaymentType = null,
                    InstallmentType = null,
                    Label = r.RevenueType
                });

            var expenses = ExpenseQuery(projectId, fromValue, toExclusive, accountId, unassigned)
                .Select(e => new NetProfitRow
                {
                    SortId = e.Id,
                    Date = e.Date,
                    ProjectName = e.Project!.ProjectName,
                    Kind = "expense",
                    Amount = e.Amount,
                    IsPayment = false,
                    PaymentType = null,
                    InstallmentType = null,
                    Label = e.Category
                });

            var raw = await payments.Concat(manual).Concat(expenses)
                .OrderByDescending(x => x.Date)
                .ThenBy(x => x.Kind)
                .ThenByDescending(x => x.SortId)
                .Skip(skip).Take(take + 1)
                .ToListAsync();

            var items = raw.Take(take).Select(r => new NetProfitLineDto
            {
                Date = r.Date,
                ProjectName = r.ProjectName ?? "General",
                Kind = r.Kind,
                Label = r.IsPayment
                    ? ResolveAutomaticRevenueType(r.PaymentType!.Value, r.InstallmentType)
                    : (r.Label ?? string.Empty),
                Amount = r.Kind == "expense" ? -r.Amount : r.Amount
            }).ToList();

            return new PagedResult<NetProfitLineDto> { Items = items, HasMore = raw.Count > take };
        }

        // ── Filtered base queries (shared by summary totals and paged rows) ──
        private IQueryable<Payment> PaymentsQuery(int? projectId, DateTime? fromValue, DateTime? toExclusive)
        {
            // Payments on cancelled bookings stay in revenue: the money was really
            // received, and cancelling a booking must not rewrite finance history.
            var q = _context.Payments.AsNoTracking().AsQueryable();
            if (projectId.HasValue) q = q.Where(p => p.Booking.Unit.ProjectId == projectId.Value);
            if (fromValue.HasValue) q = q.Where(p => p.PaidAt >= fromValue.Value);
            if (toExclusive.HasValue) q = q.Where(p => p.PaidAt < toExclusive.Value);
            return q;
        }

        private IQueryable<ManualRevenue> ManualQuery(int? projectId, DateTime? fromValue, DateTime? toExclusive, int? accountId = null, bool unassigned = false)
        {
            var q = _context.ManualRevenues.AsNoTracking().AsQueryable();
            if (projectId.HasValue) q = q.Where(r => r.ProjectId == projectId.Value);
            if (fromValue.HasValue) q = q.Where(r => r.Date >= fromValue.Value);
            if (toExclusive.HasValue) q = q.Where(r => r.Date < toExclusive.Value);
            if (accountId.HasValue) q = q.Where(r => r.FinanceAccountId == accountId.Value);
            else if (unassigned) q = q.Where(r => r.FinanceAccountId == null);
            return q;
        }

        private IQueryable<Expense> ExpenseQuery(int? projectId, DateTime? fromValue, DateTime? toExclusive, int? accountId = null, bool unassigned = false)
        {
            var q = _context.Expenses.AsNoTracking().AsQueryable();
            if (projectId.HasValue) q = q.Where(e => e.ProjectId == projectId.Value);
            if (fromValue.HasValue) q = q.Where(e => e.Date >= fromValue.Value);
            if (toExclusive.HasValue) q = q.Where(e => e.Date < toExclusive.Value);
            if (accountId.HasValue) q = q.Where(e => e.FinanceAccountId == accountId.Value);
            else if (unassigned) q = q.Where(e => e.FinanceAccountId == null);
            return q;
        }

        private IQueryable<Booking> OutstandingBookings(int? projectId)
        {
            var q = _context.Bookings.AsNoTracking().Where(b => b.Status != BookingStatus.Cancelled);
            if (projectId.HasValue) q = q.Where(b => b.Unit.ProjectId == projectId.Value);
            return q;
        }

        private IQueryable<Installment> OverdueInstallments(int? projectId)
        {
            var today = PakistanTime.Today;
            var q = _context.Installments.AsNoTracking()
                .Where(i => i.DueDate < today
                    && i.Status != InstallmentStatus.Paid
                    && i.Booking.Status != BookingStatus.Cancelled);
            if (projectId.HasValue) q = q.Where(i => i.Booking.Unit.ProjectId == projectId.Value);
            return q;
        }

        private static PagedResult<T> Page<T>(List<T> rows, int take) =>
            new() { Items = rows.Take(take).ToList(), HasMore = rows.Count > take };

        // Shapes used only inside SQL UNION ALL projections.
        private sealed class RevenueRow
        {
            public int SortId { get; set; }
            public DateTime Date { get; set; }
            public int? ProjectId { get; set; }
            public string? ProjectName { get; set; }
            public decimal Amount { get; set; }
            public string Source { get; set; } = string.Empty;
            public int? ManualRevenueId { get; set; }
            public PaymentType? PaymentType { get; set; }
            public InstallmentType? InstallmentType { get; set; }
            public string? ReceiptNumber { get; set; }
            public string? BookingReference { get; set; }
            public string? CustomerName { get; set; }
            public string? RevenueType { get; set; }
            public string? Reference { get; set; }
            public string? Description { get; set; }
            public string? AttachmentFileName { get; set; }
            public string? AttachmentContentType { get; set; }
            public long? AttachmentFileSize { get; set; }
            public DateTime? AttachmentUploadedAt { get; set; }
            public int? FinanceAccountId { get; set; }
            public string? FinanceAccountName { get; set; }
            public string? AccountHolderName { get; set; }
        }

        private sealed class NetProfitRow
        {
            public int SortId { get; set; }
            public DateTime Date { get; set; }
            public string? ProjectName { get; set; }
            public string Kind { get; set; } = string.Empty;
            public decimal Amount { get; set; }
            public bool IsPayment { get; set; }
            public PaymentType? PaymentType { get; set; }
            public InstallmentType? InstallmentType { get; set; }
            public string? Label { get; set; }
        }

        public async Task<ManualRevenueResponseDto> CreateManualRevenueAsync(
            CreateManualRevenueDto dto,
            int? adminUserId,
            FinanceAttachmentUpload? attachment = null,
            CancellationToken cancellationToken = default)
        {
            ValidateRevenue(dto.Amount, dto.RevenueType);
            await EnsureProjectExistsAsync(dto.ProjectId, cancellationToken);
            if (!dto.FinanceAccountId.HasValue)
                throw new InvalidOperationException("Received In Account is required.");
            await _accountService.EnsureSelectableAsync(dto.FinanceAccountId.Value, null, cancellationToken);

            var revenue = new ManualRevenue
            {
                ProjectId = dto.ProjectId,
                FinanceAccountId = dto.FinanceAccountId,
                Amount = dto.Amount,
                RevenueType = dto.RevenueType.Trim(),
                Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
                Reference = string.IsNullOrWhiteSpace(dto.Reference) ? null : dto.Reference.Trim(),
                Date = dto.Date?.Date ?? DateTime.UtcNow,
                CreatedByUserId = adminUserId,
                CreatedAt = DateTime.UtcNow
            };

            string? newStoredFileName = null;
            if (attachment != null)
            {
                var saved = await SaveAttachmentAsync(attachment, cancellationToken);
                newStoredFileName = saved.StoredFileName;
                revenue.Attachment = new FinanceAttachment
                {
                    StoredFileName = saved.StoredFileName,
                    OriginalFileName = saved.Metadata.OriginalFileName,
                    ContentType = saved.Metadata.ContentType,
                    FileSize = saved.Metadata.FileSize,
                    UploadedAt = DateTime.UtcNow
                };
            }

            _context.ManualRevenues.Add(revenue);
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                await DeleteNewFileAfterFailureAsync(newStoredFileName);
                throw;
            }

            return await MapManualRevenueAsync(revenue);
        }

        public async Task<ManualRevenueResponseDto> UpdateManualRevenueAsync(
            int id,
            UpdateManualRevenueDto dto,
            FinanceAttachmentUpload? attachment = null,
            bool removeAttachment = false,
            CancellationToken cancellationToken = default)
        {
            ValidateAttachmentChange(attachment, removeAttachment);
            var revenue = await _context.ManualRevenues
                .Include(r => r.Attachment)
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
            if (revenue == null)
                throw new InvalidOperationException("Manual revenue entry not found.");
            ValidateRevenue(dto.Amount, dto.RevenueType);
            await EnsureProjectExistsAsync(dto.ProjectId, cancellationToken);
            if (!dto.FinanceAccountId.HasValue)
                throw new InvalidOperationException("Received In Account is required.");
            await _accountService.EnsureSelectableAsync(dto.FinanceAccountId.Value, revenue.FinanceAccountId, cancellationToken);

            var oldStoredFileName = revenue.Attachment?.StoredFileName;
            string? newStoredFileName = null;

            if (attachment != null)
            {
                var saved = await SaveAttachmentAsync(attachment, cancellationToken);
                newStoredFileName = saved.StoredFileName;
                if (revenue.Attachment == null)
                {
                    revenue.Attachment = new FinanceAttachment { ManualRevenueId = revenue.Id };
                }
                ApplyAttachment(revenue.Attachment, saved);
            }
            else if (removeAttachment && revenue.Attachment != null)
            {
                _context.FinanceAttachments.Remove(revenue.Attachment);
                revenue.Attachment = null;
            }

            revenue.ProjectId = dto.ProjectId;
            revenue.FinanceAccountId = dto.FinanceAccountId;
            revenue.Amount = dto.Amount;
            revenue.RevenueType = dto.RevenueType.Trim();
            revenue.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
            revenue.Reference = string.IsNullOrWhiteSpace(dto.Reference) ? null : dto.Reference.Trim();
            if (dto.Date.HasValue)
                revenue.Date = dto.Date.Value.Date;

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                await DeleteNewFileAfterFailureAsync(newStoredFileName);
                throw;
            }

            if ((attachment != null || removeAttachment) && oldStoredFileName != null)
                await DeleteObsoleteFileAsync(oldStoredFileName);

            return await MapManualRevenueAsync(revenue);
        }

        public async Task DeleteManualRevenueAsync(int id, CancellationToken cancellationToken = default)
        {
            var revenue = await _context.ManualRevenues
                .Include(r => r.Attachment)
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
            if (revenue == null)
                throw new InvalidOperationException("Manual revenue entry not found.");

            var storedFileName = revenue.Attachment?.StoredFileName;
            _context.ManualRevenues.Remove(revenue);
            await _context.SaveChangesAsync(cancellationToken);
            await DeleteObsoleteFileAsync(storedFileName);
        }

        public async Task<ExpenseResponseDto> CreateExpenseAsync(
            CreateExpenseDto dto,
            int? adminUserId,
            FinanceAttachmentUpload? attachment = null,
            CancellationToken cancellationToken = default)
        {
            ValidateExpense(dto.Amount, dto.Category);
            await EnsureProjectExistsAsync(dto.ProjectId, cancellationToken);
            if (!dto.FinanceAccountId.HasValue)
                throw new InvalidOperationException("Paid From Account is required.");
            await _accountService.EnsureSelectableAsync(dto.FinanceAccountId.Value, null, cancellationToken);

            var expense = new Expense
            {
                ProjectId = dto.ProjectId,
                FinanceAccountId = dto.FinanceAccountId,
                Amount = dto.Amount,
                Category = dto.Category.Trim(),
                Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
                Vendor = string.IsNullOrWhiteSpace(dto.Vendor) ? null : dto.Vendor.Trim(),
                Date = dto.Date?.Date ?? DateTime.UtcNow,
                CreatedByUserId = adminUserId,
                CreatedAt = DateTime.UtcNow
            };

            string? newStoredFileName = null;
            if (attachment != null)
            {
                var saved = await SaveAttachmentAsync(attachment, cancellationToken);
                newStoredFileName = saved.StoredFileName;
                expense.Attachment = new FinanceAttachment
                {
                    StoredFileName = saved.StoredFileName,
                    OriginalFileName = saved.Metadata.OriginalFileName,
                    ContentType = saved.Metadata.ContentType,
                    FileSize = saved.Metadata.FileSize,
                    UploadedAt = DateTime.UtcNow
                };
            }

            _context.Expenses.Add(expense);
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                await DeleteNewFileAfterFailureAsync(newStoredFileName);
                throw;
            }

            return await MapExpenseAsync(expense);
        }

        public async Task<ExpenseResponseDto> UpdateExpenseAsync(
            int id,
            UpdateExpenseDto dto,
            FinanceAttachmentUpload? attachment = null,
            bool removeAttachment = false,
            CancellationToken cancellationToken = default)
        {
            ValidateAttachmentChange(attachment, removeAttachment);
            var expense = await _context.Expenses
                .Include(e => e.Attachment)
                .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
            if (expense == null)
                throw new InvalidOperationException("Expense not found.");
            ValidateExpense(dto.Amount, dto.Category);
            await EnsureProjectExistsAsync(dto.ProjectId, cancellationToken);
            if (!dto.FinanceAccountId.HasValue)
                throw new InvalidOperationException("Paid From Account is required.");
            await _accountService.EnsureSelectableAsync(dto.FinanceAccountId.Value, expense.FinanceAccountId, cancellationToken);

            var oldStoredFileName = expense.Attachment?.StoredFileName;
            string? newStoredFileName = null;

            if (attachment != null)
            {
                var saved = await SaveAttachmentAsync(attachment, cancellationToken);
                newStoredFileName = saved.StoredFileName;
                if (expense.Attachment == null)
                {
                    expense.Attachment = new FinanceAttachment { ExpenseId = expense.Id };
                }
                ApplyAttachment(expense.Attachment, saved);
            }
            else if (removeAttachment && expense.Attachment != null)
            {
                _context.FinanceAttachments.Remove(expense.Attachment);
                expense.Attachment = null;
            }

            expense.ProjectId = dto.ProjectId;
            expense.FinanceAccountId = dto.FinanceAccountId;
            expense.Amount = dto.Amount;
            expense.Category = dto.Category.Trim();
            expense.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
            expense.Vendor = string.IsNullOrWhiteSpace(dto.Vendor) ? null : dto.Vendor.Trim();
            if (dto.Date.HasValue)
                expense.Date = dto.Date.Value.Date;

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                await DeleteNewFileAfterFailureAsync(newStoredFileName);
                throw;
            }

            if ((attachment != null || removeAttachment) && oldStoredFileName != null)
                await DeleteObsoleteFileAsync(oldStoredFileName);

            return await MapExpenseAsync(expense);
        }

        public async Task DeleteExpenseAsync(int id, CancellationToken cancellationToken = default)
        {
            var expense = await _context.Expenses
                .Include(e => e.Attachment)
                .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
            if (expense == null)
                throw new InvalidOperationException("Expense not found.");

            var storedFileName = expense.Attachment?.StoredFileName;
            _context.Expenses.Remove(expense);
            await _context.SaveChangesAsync(cancellationToken);
            await DeleteObsoleteFileAsync(storedFileName);
        }

        public async Task<FinanceAttachmentDownload> GetAttachmentAsync(
            FinanceRecordKind kind,
            int recordId,
            CancellationToken cancellationToken = default)
        {
            var attachment = kind == FinanceRecordKind.Revenue
                ? await _context.ManualRevenues.AsNoTracking()
                    .Where(r => r.Id == recordId && r.Attachment != null)
                    .Select(r => r.Attachment!)
                    .FirstOrDefaultAsync(cancellationToken)
                : await _context.Expenses.AsNoTracking()
                    .Where(e => e.Id == recordId && e.Attachment != null)
                    .Select(e => e.Attachment!)
                    .FirstOrDefaultAsync(cancellationToken);

            if (attachment == null)
                throw new FileNotFoundException("This finance record does not have an attachment.");

            var content = await _attachmentStorage.OpenReadAsync(attachment.StoredFileName, cancellationToken);
            if (content == null)
                throw new FileNotFoundException("The attachment file is missing from storage. Please replace it from the edit form.");

            return new FinanceAttachmentDownload
            {
                Content = content,
                FileName = attachment.OriginalFileName,
                ContentType = attachment.ContentType
            };
        }

        public async Task RemoveAttachmentAsync(
            FinanceRecordKind kind,
            int recordId,
            CancellationToken cancellationToken = default)
        {
            FinanceAttachment? attachment;
            if (kind == FinanceRecordKind.Revenue)
            {
                var revenue = await _context.ManualRevenues
                    .Include(r => r.Attachment)
                    .FirstOrDefaultAsync(r => r.Id == recordId, cancellationToken)
                    ?? throw new InvalidOperationException("Manual revenue entry not found.");
                attachment = revenue.Attachment;
            }
            else
            {
                var expense = await _context.Expenses
                    .Include(e => e.Attachment)
                    .FirstOrDefaultAsync(e => e.Id == recordId, cancellationToken)
                    ?? throw new InvalidOperationException("Expense not found.");
                attachment = expense.Attachment;
            }

            if (attachment == null)
                return;

            var storedFileName = attachment.StoredFileName;
            _context.FinanceAttachments.Remove(attachment);
            await _context.SaveChangesAsync(cancellationToken);
            await DeleteObsoleteFileAsync(storedFileName);
        }

        private async Task EnsureProjectExistsAsync(int? projectId, CancellationToken cancellationToken = default)
        {
            if (!projectId.HasValue)
                return;
            var exists = await _context.Projects.AnyAsync(p => p.Id == projectId.Value, cancellationToken);
            if (!exists)
                throw new InvalidOperationException("Selected project does not exist.");
        }

        private async Task<string?> GetProjectNameAsync(int? projectId)
        {
            if (!projectId.HasValue)
                return null;
            return await _context.Projects
                .Where(p => p.Id == projectId.Value)
                .Select(p => p.ProjectName)
                .FirstOrDefaultAsync();
        }

        private async Task<ManualRevenueResponseDto> MapManualRevenueAsync(ManualRevenue r)
        {
            var account = await GetAccountIdentityAsync(r.FinanceAccountId);
            return new ManualRevenueResponseDto
            {
                Id = r.Id,
                ProjectId = r.ProjectId,
                ProjectName = await GetProjectNameAsync(r.ProjectId),
                FinanceAccountId = r.FinanceAccountId,
                FinanceAccountName = account?.Name,
                AccountHolderName = account?.Holder,
                Amount = r.Amount,
                RevenueType = r.RevenueType,
                Description = r.Description,
                Reference = r.Reference,
                Date = r.Date,
                CreatedAt = r.CreatedAt,
                Attachment = MapAttachment(r.Attachment)
            };
        }

        private async Task<ExpenseResponseDto> MapExpenseAsync(Expense e)
        {
            var account = await GetAccountIdentityAsync(e.FinanceAccountId);
            return new ExpenseResponseDto
            {
                Id = e.Id,
                ProjectId = e.ProjectId,
                ProjectName = await GetProjectNameAsync(e.ProjectId),
                FinanceAccountId = e.FinanceAccountId,
                FinanceAccountName = account?.Name,
                AccountHolderName = account?.Holder,
                Amount = e.Amount,
                Category = e.Category,
                Description = e.Description,
                Vendor = e.Vendor,
                Date = e.Date,
                CreatedAt = e.CreatedAt,
                Attachment = MapAttachment(e.Attachment)
            };
        }

        private async Task<(string Name, string Holder)?> GetAccountIdentityAsync(int? accountId, CancellationToken cancellationToken = default)
        {
            if (!accountId.HasValue)
                return null;

            var account = await _context.FinanceAccounts.AsNoTracking()
                .Where(a => a.Id == accountId.Value)
                .Select(a => new { a.Name, a.AccountHolderName })
                .SingleOrDefaultAsync(cancellationToken);
            return account == null ? null : (account.Name, account.AccountHolderName);
        }

        private async Task<(string StoredFileName, ValidatedFinanceAttachment Metadata)> SaveAttachmentAsync(
            FinanceAttachmentUpload upload,
            CancellationToken cancellationToken)
        {
            var metadata = FinanceAttachmentFileValidator.Validate(upload);
            var storedFileName = await _attachmentStorage.SaveAsync(upload.Content, metadata.Extension, cancellationToken);
            return (storedFileName, metadata);
        }

        private static void ApplyAttachment(
            FinanceAttachment attachment,
            (string StoredFileName, ValidatedFinanceAttachment Metadata) saved)
        {
            attachment.StoredFileName = saved.StoredFileName;
            attachment.OriginalFileName = saved.Metadata.OriginalFileName;
            attachment.ContentType = saved.Metadata.ContentType;
            attachment.FileSize = saved.Metadata.FileSize;
            attachment.UploadedAt = DateTime.UtcNow;
        }

        private async Task DeleteNewFileAfterFailureAsync(string? storedFileName)
        {
            if (storedFileName == null)
                return;
            try
            {
                await _attachmentStorage.DeleteAsync(storedFileName, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not clean up failed finance attachment {StoredFileName}", storedFileName);
            }
        }

        private async Task DeleteObsoleteFileAsync(string? storedFileName)
        {
            if (storedFileName == null)
                return;
            try
            {
                await _attachmentStorage.DeleteAsync(storedFileName, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Metadata is already removed/changed, so the orphan is inaccessible. Keep the
                // successful finance operation successful and surface the cleanup issue in logs.
                _logger.LogWarning(ex, "Could not delete obsolete finance attachment {StoredFileName}", storedFileName);
            }
        }

        private static FinanceAttachmentDto? MapAttachment(FinanceAttachment? attachment) =>
            attachment == null
                ? null
                : new FinanceAttachmentDto
                {
                    FileName = attachment.OriginalFileName,
                    ContentType = attachment.ContentType,
                    FileSize = attachment.FileSize,
                    UploadedAt = attachment.UploadedAt
                };

        private static FinanceAttachmentDto? MapAttachment(
            string? fileName,
            string? contentType,
            long? fileSize,
            DateTime? uploadedAt) =>
            fileName == null || contentType == null || fileSize == null || uploadedAt == null
                ? null
                : new FinanceAttachmentDto
                {
                    FileName = fileName,
                    ContentType = contentType,
                    FileSize = fileSize.Value,
                    UploadedAt = uploadedAt.Value
                };

        private static void ValidateAttachmentChange(FinanceAttachmentUpload? attachment, bool removeAttachment)
        {
            if (attachment != null && removeAttachment)
                throw new InvalidOperationException("Choose either a replacement attachment or removal, not both.");
        }

        private static void ValidateRevenue(decimal amount, string revenueType)
        {
            if (amount <= 0)
                throw new InvalidOperationException("Amount must be greater than zero.");
            if (string.IsNullOrWhiteSpace(revenueType))
                throw new InvalidOperationException("Revenue type is required.");
        }

        private static void ValidateExpense(decimal amount, string category)
        {
            if (amount <= 0)
                throw new InvalidOperationException("Amount must be greater than zero.");
            if (string.IsNullOrWhiteSpace(category))
                throw new InvalidOperationException("Category is required.");
        }

        private static string ResolveAutomaticRevenueType(PaymentType type, InstallmentType? installmentType)
        {
            if (type == PaymentType.BookingAmount)
                return "Booking Amount";
            if (installmentType == InstallmentType.Possession)
                return "Possession Payment";
            return "Installment Payment";
        }

        private static string BuildPaymentReference(string? receiptNumber, string bookingReference, string customerName)
        {
            var primary = string.IsNullOrWhiteSpace(receiptNumber) ? bookingReference : receiptNumber;
            return $"{primary} • {customerName}";
        }
    }
}
