using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public sealed class RevenueCategoryService : IRevenueCategoryService
    {
        private readonly AppDbContext _context;
        public RevenueCategoryService(AppDbContext context) => _context = context;

        public Task<List<RevenueCategoryDto>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken = default) =>
            Project(_context.RevenueCategories.AsNoTracking().Where(c => includeInactive || c.IsActive))
                .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name).ToListAsync(cancellationToken);

        public async Task<RevenueCategoryDto> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            await Project(_context.RevenueCategories.AsNoTracking().Where(c => c.Id == id))
                .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Revenue category not found.");

        public async Task<RevenueCategoryDto> CreateAsync(SaveRevenueCategoryDto dto, int? userId, CancellationToken cancellationToken = default)
        {
            Validate(dto);
            var code = NormaliseCode(dto.Code, dto.Name);
            await EnsureUniqueAsync(dto.Name, code, null, cancellationToken);
            var category = new RevenueCategory
            {
                Name = dto.Name.Trim(), Code = code, Description = Clean(dto.Description),
                DisplayOrder = dto.DisplayOrder, IsActive = dto.IsActive,
                CreatedByUserId = userId, CreatedAt = DateTime.UtcNow
            };
            _context.RevenueCategories.Add(category);
            await _context.SaveChangesAsync(cancellationToken);
            return await GetByIdAsync(category.Id, cancellationToken);
        }

        public async Task<RevenueCategoryDto> UpdateAsync(int id, SaveRevenueCategoryDto dto, CancellationToken cancellationToken = default)
        {
            Validate(dto);
            var category = await _context.RevenueCategories.SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("Revenue category not found.");
            ApplyToken(category, dto.ConcurrencyToken);
            await EnsureUniqueAsync(dto.Name, category.Code, id, cancellationToken);
            category.Name = dto.Name.Trim();
            category.Description = Clean(dto.Description);
            category.DisplayOrder = dto.DisplayOrder;
            category.IsActive = dto.IsActive;
            category.UpdatedAt = DateTime.UtcNow;
            // ManualRevenue.RevenueTypeName is a snapshot and is deliberately not updated.
            await _context.SaveChangesAsync(cancellationToken);
            return await GetByIdAsync(id, cancellationToken);
        }

        public async Task<RevenueCategoryDto?> DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            var category = await _context.RevenueCategories.SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("Revenue category not found.");
            // Configuration is historical data, so DELETE always means retire. A physical delete
            // could race with a revenue entry and makes accidental removal unrecoverable.
            category.IsActive = false;
            category.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
            return await GetByIdAsync(id, cancellationToken);
        }

        private static IQueryable<RevenueCategoryDto> Project(IQueryable<RevenueCategory> query) => query.Select(c => new RevenueCategoryDto
        {
            Id = c.Id, Name = c.Name, Code = c.Code, Description = c.Description,
            DisplayOrder = c.DisplayOrder, IsActive = c.IsActive, RevenueCount = c.ManualRevenues.Count,
            CreatedAt = c.CreatedAt, UpdatedAt = c.UpdatedAt,
            ConcurrencyToken = Convert.ToBase64String(c.RowVersion)
        });

        private async Task EnsureUniqueAsync(string name, string code, int? except, CancellationToken cancellationToken)
        {
            var cleanName = name.Trim();
            if (await _context.RevenueCategories.AnyAsync(c => (!except.HasValue || c.Id != except) && (c.Name == cleanName || c.Code == code), cancellationToken))
                throw new InvalidOperationException("A revenue category with this name or code already exists.");
        }

        private void ApplyToken(RevenueCategory category, string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                if (category.RowVersion.Length == 0) return;
                throw new InvalidOperationException("The category version is missing. Refresh and try again.");
            }
            try { _context.Entry(category).Property(c => c.RowVersion).OriginalValue = Convert.FromBase64String(token); }
            catch (FormatException) { throw new InvalidOperationException("The category version is invalid. Refresh and try again."); }
        }

        private static void Validate(SaveRevenueCategoryDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) throw new InvalidOperationException("Category name is required.");
            if (dto.Name.Trim().Length > 150) throw new InvalidOperationException("Category name cannot exceed 150 characters.");
            if (dto.Description?.Trim().Length > 1000) throw new InvalidOperationException("Description cannot exceed 1000 characters.");
        }

        private static string NormaliseCode(string code, string name)
        {
            var value = string.IsNullOrWhiteSpace(code) ? name : code;
            var slug = new string(value.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
            while (slug.Contains("__")) slug = slug.Replace("__", "_");
            slug = slug.Trim('_');
            if (slug.Length == 0) throw new InvalidOperationException("Category code must contain a letter or digit.");
            return slug.Length <= 80 ? slug : slug[..80];
        }

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
