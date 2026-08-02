using DAMS.Application.Common;
using DAMS.Application.DTOs.NotificationDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services.Notifications
{
    /// <summary>
    /// Per-category channel preferences. The list is always complete — a user with no stored
    /// rows still sees every category with its default — and mandatory categories are
    /// returned flagged so the interface can show them without offering a switch.
    /// </summary>
    public sealed class NotificationPreferenceService : INotificationPreferenceService
    {
        private readonly AppDbContext _context;
        private readonly NotificationSettingsStore _settings;
        private readonly NotificationEligibilityPolicy _eligibility;
        private bool? _schemaAvailable;

        public NotificationPreferenceService(
            AppDbContext context,
            NotificationSettingsStore settings,
            NotificationEligibilityPolicy eligibility)
        {
            _context = context;
            _settings = settings;
            _eligibility = eligibility;
        }

        public async Task<NotificationCapabilitiesDto> GetCapabilitiesAsync(
            NotificationUserContext ctx, CancellationToken cancellationToken = default)
        {
            var schemaExists = await SchemaExistsAsync(cancellationToken);
            var emailOn = schemaExists && await _settings.GetBoolAsync(
                NotificationSettingKeys.EmailEnabled, false, cancellationToken);
            var pushOn = schemaExists && await _settings.GetBoolAsync(
                NotificationSettingKeys.PushEnabled, false, cancellationToken);
            var emailCategories = CategoriesSupporting(ctx.Role, NotificationChannel.Email);
            var pushCategories = CategoriesSupporting(ctx.Role, NotificationChannel.WebPush);

            return new NotificationCapabilitiesDto
            {
                Role = ctx.Role,
                EmptyStateMessage = _eligibility.EmptyStateForRole(ctx.Role),
                Categories = _eligibility.CategoriesForRole(ctx.Role)
                    .Select(category => new NotificationCategoryCapabilityDto
                    {
                        Category = category.Category,
                        Label = category.Label,
                        Description = category.Description,
                        IsMandatory = NotificationCatalog.IsMandatoryCategory(category.Category),
                        EmailAvailable = emailOn && emailCategories.Contains(category.Category),
                        PushAvailable = pushOn && pushCategories.Contains(category.Category)
                    })
                    .ToList()
            };
        }

        public async Task<List<NotificationPreferenceDto>> GetAsync(
            NotificationUserContext ctx, CancellationToken cancellationToken = default)
        {
            var schemaExists = await SchemaExistsAsync(cancellationToken);
            var stored = schemaExists
                ? await _context.NotificationPreferences
                    .AsNoTracking()
                    .Where(p => p.UserId == ctx.UserId)
                    .ToDictionaryAsync(p => p.Category, cancellationToken)
                : new Dictionary<NotificationCategory, NotificationPreference>();

            var capabilities = await GetCapabilitiesAsync(ctx, cancellationToken);

            return capabilities.Categories
                .Select(d =>
                {
                    var mandatory = NotificationCatalog.IsMandatoryCategory(d.Category);
                    stored.TryGetValue(d.Category, out var row);

                    return new NotificationPreferenceDto
                    {
                        Category = d.Category,
                        Label = d.Label,
                        Description = d.Description,
                        IsMandatory = mandatory,
                        EmailEnabled = mandatory || (row?.EmailEnabled ?? true),
                        PushEnabled = mandatory || (row?.PushEnabled ?? true),
                        EmailAvailable = d.EmailAvailable,
                        PushAvailable = d.PushAvailable
                    };
                })
                .ToList();
        }

        public async Task<List<NotificationPreferenceDto>> UpdateAsync(
            NotificationUserContext ctx, UpdateNotificationPreferencesDto dto, CancellationToken cancellationToken = default)
        {
            if (!await SchemaExistsAsync(cancellationToken))
                return await GetAsync(ctx, cancellationToken);

            var existing = await _context.NotificationPreferences
                .Where(p => p.UserId == ctx.UserId)
                .ToDictionaryAsync(p => p.Category, cancellationToken);

            foreach (var item in dto.Items.DistinctBy(i => i.Category))
            {
                if (!_eligibility.CanRoleUseCategory(ctx.Role, item.Category))
                    throw new LeadAuthorizationException("That notification category is not available for your account.");

                // Silently ignoring a mandatory category is the right answer: the interface
                // never offers it, and a crafted request must not be able to switch off a
                // receipt or a security message.
                if (NotificationCatalog.IsMandatoryCategory(item.Category))
                    continue;

                if (existing.TryGetValue(item.Category, out var row))
                {
                    row.EmailEnabled = item.EmailEnabled;
                    row.PushEnabled = item.PushEnabled;
                    row.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    _context.NotificationPreferences.Add(new NotificationPreference
                    {
                        UserId = ctx.UserId,
                        Category = item.Category,
                        EmailEnabled = item.EmailEnabled,
                        PushEnabled = item.PushEnabled,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
            return await GetAsync(ctx, cancellationToken);
        }

        private HashSet<NotificationCategory> CategoriesSupporting(string role, NotificationChannel channel) =>
            NotificationCatalog.All
                .Where(d => _eligibility.CanRoleReceive(role, d.Type) && d.DefaultChannels.HasFlag(channel))
                .Select(d => d.Category)
                .ToHashSet();

        private async Task<bool> SchemaExistsAsync(CancellationToken cancellationToken)
        {
            if (_schemaAvailable.HasValue)
                return _schemaAvailable.Value;

            _schemaAvailable = await NotificationSchemaProbe.ExistsAsync(_context, cancellationToken);
            return _schemaAvailable.Value;
        }
    }
}
