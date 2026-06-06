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

        public async Task<FinanceDashboardDto> GetDashboardAsync(int? projectId, DateTime? from, DateTime? to)
        {
            // Normalize date range: 'to' is treated as inclusive of the whole day.
            var fromValue = from?.Date;
            var toExclusive = to?.Date.AddDays(1);

            // ── Automatic revenue (booking / installment / possession payments) ──
            var paymentsQuery = _context.Payments.AsNoTracking()
                .Where(p => p.Booking.Status != BookingStatus.Cancelled);

            if (projectId.HasValue)
                paymentsQuery = paymentsQuery.Where(p => p.Booking.Unit.ProjectId == projectId.Value);
            if (fromValue.HasValue)
                paymentsQuery = paymentsQuery.Where(p => p.PaidAt >= fromValue.Value);
            if (toExclusive.HasValue)
                paymentsQuery = paymentsQuery.Where(p => p.PaidAt < toExclusive.Value);

            var paymentRows = await paymentsQuery
                .Select(p => new
                {
                    p.PaidAt,
                    ProjectId = (int?)p.Booking.Unit.ProjectId,
                    ProjectName = p.Booking.Unit.Project != null ? p.Booking.Unit.Project.ProjectName : "General",
                    p.Type,
                    InstallmentType = p.Installment != null ? (InstallmentType?)p.Installment.Type : null,
                    p.Amount,
                    p.ReceiptNumber,
                    BookingReference = p.Booking.BookingReference,
                    CustomerName = p.Booking.Customer.FullName
                })
                .ToListAsync();

            var automaticRevenueRows = paymentRows
                .Select(p => new RevenueLineDto
                {
                    Date = p.PaidAt,
                    ProjectId = p.ProjectId,
                    ProjectName = p.ProjectName,
                    RevenueType = ResolveAutomaticRevenueType(p.Type, p.InstallmentType),
                    Amount = p.Amount,
                    Source = "Payment",
                    Reference = BuildPaymentReference(p.ReceiptNumber, p.BookingReference, p.CustomerName)
                })
                .ToList();

            // ── Manual revenue ──
            var manualQuery = _context.ManualRevenues.AsNoTracking().AsQueryable();
            if (projectId.HasValue)
                manualQuery = manualQuery.Where(r => r.ProjectId == projectId.Value);
            if (fromValue.HasValue)
                manualQuery = manualQuery.Where(r => r.Date >= fromValue.Value);
            if (toExclusive.HasValue)
                manualQuery = manualQuery.Where(r => r.Date < toExclusive.Value);

            var manualRevenueRows = await manualQuery
                .Select(r => new RevenueLineDto
                {
                    ManualRevenueId = r.Id,
                    Date = r.Date,
                    ProjectId = r.ProjectId,
                    ProjectName = r.Project != null ? r.Project.ProjectName : "General",
                    RevenueType = r.RevenueType,
                    Amount = r.Amount,
                    Source = "Manual Revenue",
                    Reference = r.Reference,
                    Description = r.Description
                })
                .ToListAsync();

            var revenue = automaticRevenueRows
                .Concat(manualRevenueRows)
                .OrderByDescending(r => r.Date)
                .ToList();

            // ── Expenses ──
            var expenseQuery = _context.Expenses.AsNoTracking().AsQueryable();
            if (projectId.HasValue)
                expenseQuery = expenseQuery.Where(e => e.ProjectId == projectId.Value);
            if (fromValue.HasValue)
                expenseQuery = expenseQuery.Where(e => e.Date >= fromValue.Value);
            if (toExclusive.HasValue)
                expenseQuery = expenseQuery.Where(e => e.Date < toExclusive.Value);

            var expenses = await expenseQuery
                .OrderByDescending(e => e.Date)
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

            // ── Summary cards ──
            var automaticRevenue = automaticRevenueRows.Sum(r => r.Amount);
            var manualRevenue = manualRevenueRows.Sum(r => r.Amount);
            var totalRevenue = automaticRevenue + manualRevenue;
            var totalExpenses = expenses.Sum(e => e.Amount);

            var summary = new FinancialSummaryDto
            {
                AutomaticRevenue = automaticRevenue,
                ManualRevenue = manualRevenue,
                TotalRevenue = totalRevenue,
                TotalExpenses = totalExpenses,
                NetProfit = totalRevenue - totalExpenses,
                OutstandingAmount = await ComputeOutstandingAsync(projectId),
                OverdueAmount = await ComputeOverdueAsync(projectId)
            };

            return new FinanceDashboardDto
            {
                Summary = summary,
                Revenue = revenue,
                Expenses = expenses
            };
        }

        // Outstanding = Booking Value (AgreedSalePrice) - Received Payments, over active bookings.
        // This is a current balance snapshot, independent of the date-range filter.
        private async Task<decimal> ComputeOutstandingAsync(int? projectId)
        {
            var bookings = _context.Bookings.AsNoTracking()
                .Where(b => b.Status != BookingStatus.Cancelled);
            if (projectId.HasValue)
                bookings = bookings.Where(b => b.Unit.ProjectId == projectId.Value);

            var totalAgreed = await bookings.SumAsync(b => (decimal?)b.AgreedSalePrice) ?? 0m;

            var payments = _context.Payments.AsNoTracking()
                .Where(p => p.Booking.Status != BookingStatus.Cancelled);
            if (projectId.HasValue)
                payments = payments.Where(p => p.Booking.Unit.ProjectId == projectId.Value);

            var totalReceived = await payments.SumAsync(p => (decimal?)p.Amount) ?? 0m;

            var outstanding = totalAgreed - totalReceived;
            return outstanding < 0 ? 0m : outstanding;
        }

        // Overdue = unpaid installment dues whose DueDate is in the past (active bookings only).
        // Project into memory first to avoid EF nested-collection-Sum translation errors.
        private async Task<decimal> ComputeOverdueAsync(int? projectId)
        {
            var today = DateTime.UtcNow.Date;

            var query = _context.Installments.AsNoTracking()
                .Where(i => i.DueDate < today
                    && i.Status != InstallmentStatus.Paid
                    && i.Booking.Status != BookingStatus.Cancelled);

            if (projectId.HasValue)
                query = query.Where(i => i.Booking.Unit.ProjectId == projectId.Value);

            // Pull installment amount + sum of its payments; EF can translate a correlated
            // subquery in a Select projection but not inside SumAsync.
            var rows = await query
                .Select(i => new
                {
                    i.Amount,
                    PaidAmount = (decimal?)i.Payments.Sum(p => (decimal?)p.Amount) ?? 0m
                })
                .ToListAsync();

            var overdue = rows.Sum(r => Math.Max(0m, r.Amount - r.PaidAmount));
            return overdue;
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
