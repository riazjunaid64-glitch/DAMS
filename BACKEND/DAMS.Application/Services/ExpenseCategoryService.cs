using DAMS.Application.DTOs.WhtDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public sealed class ExpenseCategoryService : IExpenseCategoryService
    {
        private readonly AppDbContext _context;

        public ExpenseCategoryService(AppDbContext context) => _context = context;

        public Task<List<ExpenseCategoryDto>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken = default) =>
            Project(_context.ExpenseCategories.AsNoTracking().Where(c => includeInactive || c.IsActive))
                .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
                .ToListAsync(cancellationToken);

        public async Task<ExpenseCategoryDto> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            await Project(_context.ExpenseCategories.AsNoTracking().Where(c => c.Id == id))
                .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Expense category not found.");

        public async Task<ExpenseCategoryDto> CreateAsync(SaveExpenseCategoryDto dto, int? adminUserId, CancellationToken cancellationToken = default)
        {
            Validate(dto);
            var code = NormaliseCode(dto.Code, dto.Name);
            if (await _context.ExpenseCategories.AnyAsync(c => c.Code == code, cancellationToken))
                throw new InvalidOperationException("An expense category with this code already exists.");
            await EnsureUniqueNameAsync(dto.Name, null, cancellationToken);

            var category = new ExpenseCategory
            {
                Name = dto.Name.Trim(),
                Code = code,
                Description = Clean(dto.Description),
                IsWhtApplicable = dto.IsWhtApplicable,
                FilerRate = dto.IsWhtApplicable ? dto.FilerRate : 0m,
                NonFilerRate = dto.IsWhtApplicable ? dto.NonFilerRate : 0m,
                AnnualThreshold = dto.IsWhtApplicable ? dto.AnnualThreshold : 0m,
                TaxSection = Clean(dto.TaxSection),
                DisplayOrder = dto.DisplayOrder,
                IsActive = dto.IsActive,
                CreatedByUserId = adminUserId,
                CreatedAt = DateTime.UtcNow
            };
            _context.ExpenseCategories.Add(category);
            await _context.SaveChangesAsync(cancellationToken);
            return await GetByIdAsync(category.Id, cancellationToken);
        }

        public async Task<ExpenseCategoryDto> UpdateAsync(int id, SaveExpenseCategoryDto dto, CancellationToken cancellationToken = default)
        {
            Validate(dto);
            var category = await _context.ExpenseCategories.SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("Expense category not found.");
            ApplyConcurrencyToken(category, dto.ConcurrencyToken);
            await EnsureUniqueNameAsync(dto.Name, id, cancellationToken);

            // Code is deliberately immutable: it is the stable key reports and imports join on.
            category.Name = dto.Name.Trim();
            category.Description = Clean(dto.Description);
            category.IsWhtApplicable = dto.IsWhtApplicable;
            category.FilerRate = dto.IsWhtApplicable ? dto.FilerRate : 0m;
            category.NonFilerRate = dto.IsWhtApplicable ? dto.NonFilerRate : 0m;
            category.AnnualThreshold = dto.IsWhtApplicable ? dto.AnnualThreshold : 0m;
            category.TaxSection = Clean(dto.TaxSection);
            category.DisplayOrder = dto.DisplayOrder;
            category.IsActive = dto.IsActive;
            category.UpdatedAt = DateTime.UtcNow;

            // Expenses already recorded keep the rate they were entered at — the snapshot on the
            // expense row is never touched here. That is the whole point of snapshotting: a rate
            // correction must not restate tax that has been withheld, reported or deposited.
            await _context.SaveChangesAsync(cancellationToken);
            return await GetByIdAsync(id, cancellationToken);
        }

        public async Task<ExpenseCategoryDto?> DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            var category = await _context.ExpenseCategories.SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("Expense category not found.");

            if (await _context.Expenses.AnyAsync(e => e.CategoryId == id, cancellationToken))
            {
                if (!category.IsActive)
                    return await GetByIdAsync(id, cancellationToken);
                category.IsActive = false;
                category.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
                return await GetByIdAsync(id, cancellationToken);
            }

            // Never used, so nothing depends on it — remove it outright rather than leaving a
            // permanently inactive row cluttering the rate table.
            _context.ExpenseCategories.Remove(category);
            await _context.SaveChangesAsync(cancellationToken);
            return null;
        }

        private static IQueryable<ExpenseCategoryDto> Project(IQueryable<ExpenseCategory> query) =>
            query.Select(c => new ExpenseCategoryDto
            {
                Id = c.Id,
                Name = c.Name,
                Code = c.Code,
                Description = c.Description,
                IsWhtApplicable = c.IsWhtApplicable,
                FilerRate = c.FilerRate,
                NonFilerRate = c.NonFilerRate,
                AnnualThreshold = c.AnnualThreshold,
                TaxSection = c.TaxSection,
                DisplayOrder = c.DisplayOrder,
                IsActive = c.IsActive,
                ExpenseCount = c.Expenses.Count,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
                ConcurrencyToken = Convert.ToBase64String(c.RowVersion)
            });

        private async Task EnsureUniqueNameAsync(string name, int? excludingId, CancellationToken cancellationToken)
        {
            var normalised = name.Trim();
            if (await _context.ExpenseCategories.AnyAsync(
                    c => c.Name == normalised && (!excludingId.HasValue || c.Id != excludingId.Value), cancellationToken))
                throw new InvalidOperationException("An expense category with this name already exists.");
        }

        private static void Validate(SaveExpenseCategoryDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new InvalidOperationException("Category name is required.");
            if (dto.Name.Trim().Length > 150)
                throw new InvalidOperationException("Category name cannot exceed 150 characters.");
            if (dto.Description?.Trim().Length > 1000)
                throw new InvalidOperationException("Description cannot exceed 1000 characters.");
            if (dto.TaxSection?.Trim().Length > 30)
                throw new InvalidOperationException("Tax section cannot exceed 30 characters.");
            if (!dto.IsWhtApplicable)
                return;

            // 100% is the ceiling: a rate above it would withhold more than the payment itself and
            // drive the net paid negative.
            foreach (var (rate, label) in new[] { (dto.FilerRate, "Filer rate"), (dto.NonFilerRate, "Non-filer rate") })
            {
                if (rate < 0m) throw new InvalidOperationException($"{label} cannot be negative.");
                if (rate > 100m) throw new InvalidOperationException($"{label} cannot exceed 100%.");
            }
            if (dto.AnnualThreshold < 0m)
                throw new InvalidOperationException("Annual threshold cannot be negative.");
            if (dto.AnnualThreshold > 999_999_999.99m)
                throw new InvalidOperationException("Annual threshold is outside the supported range.");
        }

        private static string NormaliseCode(string code, string name)
        {
            var source = string.IsNullOrWhiteSpace(code) ? name : code;
            var slug = new string(source.Trim().ToLowerInvariant()
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
            while (slug.Contains("__")) slug = slug.Replace("__", "_");
            slug = slug.Trim('_');
            if (slug.Length == 0)
                throw new InvalidOperationException("Category code must contain at least one letter or digit.");
            return slug.Length > 80 ? slug[..80] : slug;
        }

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private void ApplyConcurrencyToken(ExpenseCategory category, string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("The category version is missing. Refresh and try again.");
            try { _context.Entry(category).Property(c => c.RowVersion).OriginalValue = Convert.FromBase64String(token); }
            catch (FormatException) { throw new InvalidOperationException("The category version is invalid. Refresh and try again."); }
        }
    }
}
