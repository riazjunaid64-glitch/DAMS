using DAMS.Application.DTOs.ExpenseDtos;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public class FinanceService : IFinanceService
    {
        private readonly AppDbContext _context;

        public FinanceService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<FinancialSummaryDto> GetSummaryAsync(int? projectId, DateTime? from, DateTime? to)
        {
            var fromValue = from?.Date;
            var toExclusive = to?.Date.AddDays(1);

            // All totals are computed in SQL — no rows are materialised for the cards.
            var automaticRevenue = await PaymentsQuery(projectId, fromValue, toExclusive)
                .SumAsync(p => (decimal?)p.Amount) ?? 0m;
            var manualRevenue = await ManualQuery(projectId, fromValue, toExclusive)
                .SumAsync(r => (decimal?)r.Amount) ?? 0m;
            var totalExpenses = await ExpenseQuery(projectId, fromValue, toExclusive)
                .SumAsync(e => (decimal?)e.Amount) ?? 0m;

            // Outstanding/overdue are balance snapshots (not date-filtered); totals are the
            // sum of the same positive per-row balances shown in the paged tables. Materialise
            // the per-row balances (a small, bounded set — same projection the tables use) and
            // sum the positive ones in memory; filtering a projected scalar in SQL does not
            // translate.
            var outstandingBalances = await OutstandingBookings(projectId)
                .Select(b => b.AgreedSalePrice - ((decimal?)b.Payments.Sum(p => (decimal?)p.Amount) ?? 0m))
                .ToListAsync();
            var outstandingTotal = outstandingBalances.Where(v => v > 0).Sum();

            var overdueBalances = await OverdueInstallments(projectId)
                .Select(i => i.Amount - ((decimal?)i.Payments.Sum(p => (decimal?)p.Amount) ?? 0m))
                .ToListAsync();
            var overdueTotal = overdueBalances.Where(v => v > 0).Sum();

            var totalRevenue = automaticRevenue + manualRevenue;
            return new FinancialSummaryDto
            {
                AutomaticRevenue = automaticRevenue,
                ManualRevenue = manualRevenue,
                TotalRevenue = totalRevenue,
                TotalExpenses = totalExpenses,
                NetProfit = totalRevenue - totalExpenses,
                OutstandingAmount = outstandingTotal,
                OverdueAmount = overdueTotal
            };
        }

        // ── Paged table rows ────────────────────────────────────────────────────────
        // Each method fetches `take + 1` rows in SQL (OFFSET/FETCH) so HasMore is known
        // without a separate COUNT query.

        public async Task<PagedResult<RevenueLineDto>> GetRevenuePageAsync(int? projectId, DateTime? from, DateTime? to, int skip, int take)
        {
            var fromValue = from?.Date;
            var toExclusive = to?.Date.AddDays(1);

            // Payments and manual revenue are unioned (UNION ALL) into one shape, ordered
            // and paged in SQL. A stable secondary key (Source + entity Id) keeps paging
            // deterministic across chunks.
            var payments = PaymentsQuery(projectId, fromValue, toExclusive)
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
                    Description = null
                });

            var manual = ManualQuery(projectId, fromValue, toExclusive)
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
                    Description = r.Description
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
                Description = r.Description
            }).ToList();

            return new PagedResult<RevenueLineDto> { Items = items, HasMore = raw.Count > take };
        }

        public async Task<PagedResult<ExpenseLineDto>> GetExpensePageAsync(int? projectId, DateTime? from, DateTime? to, int skip, int take)
        {
            var fromValue = from?.Date;
            var toExclusive = to?.Date.AddDays(1);

            var rows = await ExpenseQuery(projectId, fromValue, toExclusive)
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
                    Reference = e.Vendor
                })
                .ToListAsync();

            return Page(rows, take);
        }

        public async Task<PagedResult<OutstandingLineDto>> GetOutstandingPageAsync(int? projectId, int skip, int take)
        {
            var rows = await OutstandingBookings(projectId)
                .Where(b => b.AgreedSalePrice - ((decimal?)b.Payments.Sum(p => (decimal?)p.Amount) ?? 0m) > 0)
                .OrderByDescending(b => b.AgreedSalePrice - ((decimal?)b.Payments.Sum(p => (decimal?)p.Amount) ?? 0m))
                .ThenBy(b => b.Id)
                .Skip(skip).Take(take + 1)
                .Select(b => new OutstandingLineDto
                {
                    BookingReference = b.BookingReference,
                    CustomerName = b.Customer.FullName,
                    ProjectId = b.Unit.ProjectId,
                    ProjectName = b.Unit.Project != null ? b.Unit.Project.ProjectName : "General",
                    UnitNumber = b.Unit.UnitNumber,
                    AgreedSalePrice = b.AgreedSalePrice,
                    ReceivedAmount = (decimal?)b.Payments.Sum(p => (decimal?)p.Amount) ?? 0m,
                    OutstandingAmount = b.AgreedSalePrice - ((decimal?)b.Payments.Sum(p => (decimal?)p.Amount) ?? 0m)
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

        public async Task<PagedResult<NetProfitLineDto>> GetNetProfitPageAsync(int? projectId, DateTime? from, DateTime? to, int skip, int take)
        {
            var fromValue = from?.Date;
            var toExclusive = to?.Date.AddDays(1);

            // Net Profit = every revenue line (+) and expense line (−). Three sources are
            // unioned, ordered and paged in SQL.
            var payments = PaymentsQuery(projectId, fromValue, toExclusive)
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

            var manual = ManualQuery(projectId, fromValue, toExclusive)
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

            var expenses = ExpenseQuery(projectId, fromValue, toExclusive)
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
            var q = _context.Payments.AsNoTracking().Where(p => p.Booking.Status != BookingStatus.Cancelled);
            if (projectId.HasValue) q = q.Where(p => p.Booking.Unit.ProjectId == projectId.Value);
            if (fromValue.HasValue) q = q.Where(p => p.PaidAt >= fromValue.Value);
            if (toExclusive.HasValue) q = q.Where(p => p.PaidAt < toExclusive.Value);
            return q;
        }

        private IQueryable<ManualRevenue> ManualQuery(int? projectId, DateTime? fromValue, DateTime? toExclusive)
        {
            var q = _context.ManualRevenues.AsNoTracking().AsQueryable();
            if (projectId.HasValue) q = q.Where(r => r.ProjectId == projectId.Value);
            if (fromValue.HasValue) q = q.Where(r => r.Date >= fromValue.Value);
            if (toExclusive.HasValue) q = q.Where(r => r.Date < toExclusive.Value);
            return q;
        }

        private IQueryable<Expense> ExpenseQuery(int? projectId, DateTime? fromValue, DateTime? toExclusive)
        {
            var q = _context.Expenses.AsNoTracking().AsQueryable();
            if (projectId.HasValue) q = q.Where(e => e.ProjectId == projectId.Value);
            if (fromValue.HasValue) q = q.Where(e => e.Date >= fromValue.Value);
            if (toExclusive.HasValue) q = q.Where(e => e.Date < toExclusive.Value);
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
            var today = DateTime.UtcNow.Date;
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

        public async Task<ManualRevenueResponseDto> CreateManualRevenueAsync(CreateManualRevenueDto dto, int? adminUserId)
        {
            if (dto.Amount <= 0)
                throw new InvalidOperationException("Amount must be greater than zero.");
            if (string.IsNullOrWhiteSpace(dto.RevenueType))
                throw new InvalidOperationException("Revenue type is required.");

            await EnsureProjectExistsAsync(dto.ProjectId);

            var revenue = new ManualRevenue
            {
                ProjectId = dto.ProjectId,
                Amount = dto.Amount,
                RevenueType = dto.RevenueType.Trim(),
                Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
                Reference = string.IsNullOrWhiteSpace(dto.Reference) ? null : dto.Reference.Trim(),
                Date = dto.Date?.Date ?? DateTime.UtcNow,
                CreatedByUserId = adminUserId,
                CreatedAt = DateTime.UtcNow
            };

            _context.ManualRevenues.Add(revenue);
            await _context.SaveChangesAsync();

            return await MapManualRevenueAsync(revenue);
        }

        public async Task<ManualRevenueResponseDto> UpdateManualRevenueAsync(int id, UpdateManualRevenueDto dto)
        {
            var revenue = await _context.ManualRevenues.FirstOrDefaultAsync(r => r.Id == id);
            if (revenue == null)
                throw new InvalidOperationException("Manual revenue entry not found.");
            if (dto.Amount <= 0)
                throw new InvalidOperationException("Amount must be greater than zero.");
            if (string.IsNullOrWhiteSpace(dto.RevenueType))
                throw new InvalidOperationException("Revenue type is required.");

            await EnsureProjectExistsAsync(dto.ProjectId);

            revenue.ProjectId = dto.ProjectId;
            revenue.Amount = dto.Amount;
            revenue.RevenueType = dto.RevenueType.Trim();
            revenue.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
            revenue.Reference = string.IsNullOrWhiteSpace(dto.Reference) ? null : dto.Reference.Trim();
            if (dto.Date.HasValue)
                revenue.Date = dto.Date.Value.Date;

            await _context.SaveChangesAsync();

            return await MapManualRevenueAsync(revenue);
        }

        public async Task DeleteManualRevenueAsync(int id)
        {
            var revenue = await _context.ManualRevenues.FirstOrDefaultAsync(r => r.Id == id);
            if (revenue == null)
                throw new InvalidOperationException("Manual revenue entry not found.");

            _context.ManualRevenues.Remove(revenue);
            await _context.SaveChangesAsync();
        }

        public async Task<ExpenseResponseDto> CreateExpenseAsync(CreateExpenseDto dto, int? adminUserId)
        {
            if (dto.Amount <= 0)
                throw new InvalidOperationException("Amount must be greater than zero.");
            if (string.IsNullOrWhiteSpace(dto.Category))
                throw new InvalidOperationException("Category is required.");

            await EnsureProjectExistsAsync(dto.ProjectId);

            var expense = new Expense
            {
                ProjectId = dto.ProjectId,
                Amount = dto.Amount,
                Category = dto.Category.Trim(),
                Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
                Vendor = string.IsNullOrWhiteSpace(dto.Vendor) ? null : dto.Vendor.Trim(),
                Date = dto.Date?.Date ?? DateTime.UtcNow,
                CreatedByUserId = adminUserId,
                CreatedAt = DateTime.UtcNow
            };

            _context.Expenses.Add(expense);
            await _context.SaveChangesAsync();

            return await MapExpenseAsync(expense);
        }

        public async Task<ExpenseResponseDto> UpdateExpenseAsync(int id, UpdateExpenseDto dto)
        {
            var expense = await _context.Expenses.FirstOrDefaultAsync(e => e.Id == id);
            if (expense == null)
                throw new InvalidOperationException("Expense not found.");
            if (dto.Amount <= 0)
                throw new InvalidOperationException("Amount must be greater than zero.");
            if (string.IsNullOrWhiteSpace(dto.Category))
                throw new InvalidOperationException("Category is required.");

            await EnsureProjectExistsAsync(dto.ProjectId);

            expense.ProjectId = dto.ProjectId;
            expense.Amount = dto.Amount;
            expense.Category = dto.Category.Trim();
            expense.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
            expense.Vendor = string.IsNullOrWhiteSpace(dto.Vendor) ? null : dto.Vendor.Trim();
            if (dto.Date.HasValue)
                expense.Date = dto.Date.Value.Date;

            await _context.SaveChangesAsync();

            return await MapExpenseAsync(expense);
        }

        public async Task DeleteExpenseAsync(int id)
        {
            var expense = await _context.Expenses.FirstOrDefaultAsync(e => e.Id == id);
            if (expense == null)
                throw new InvalidOperationException("Expense not found.");

            _context.Expenses.Remove(expense);
            await _context.SaveChangesAsync();
        }

        private async Task EnsureProjectExistsAsync(int? projectId)
        {
            if (!projectId.HasValue)
                return;
            var exists = await _context.Projects.AnyAsync(p => p.Id == projectId.Value);
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
            return new ManualRevenueResponseDto
            {
                Id = r.Id,
                ProjectId = r.ProjectId,
                ProjectName = await GetProjectNameAsync(r.ProjectId),
                Amount = r.Amount,
                RevenueType = r.RevenueType,
                Description = r.Description,
                Reference = r.Reference,
                Date = r.Date,
                CreatedAt = r.CreatedAt
            };
        }

        private async Task<ExpenseResponseDto> MapExpenseAsync(Expense e)
        {
            return new ExpenseResponseDto
            {
                Id = e.Id,
                ProjectId = e.ProjectId,
                ProjectName = await GetProjectNameAsync(e.ProjectId),
                Amount = e.Amount,
                Category = e.Category,
                Description = e.Description,
                Vendor = e.Vendor,
                Date = e.Date,
                CreatedAt = e.CreatedAt
            };
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
