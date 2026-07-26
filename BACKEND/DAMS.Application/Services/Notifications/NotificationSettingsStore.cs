using DAMS.Application.Common;
using DAMS.Domain.Entities;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services.Notifications
{
    /// <summary>
    /// Reads and writes the admin settings store. Values are cached for the lifetime of one
    /// request (the service is scoped) so a single email render does not make a dozen
    /// round-trips, while a change made in one request is visible to the next.
    /// </summary>
    public sealed class NotificationSettingsStore
    {
        private readonly AppDbContext _context;
        private Dictionary<string, string?>? _cache;

        public NotificationSettingsStore(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyDictionary<string, string?>> AllAsync(CancellationToken cancellationToken = default)
        {
            _cache ??= await _context.NotificationSettings
                .AsNoTracking()
                .ToDictionaryAsync(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase, cancellationToken);

            return _cache;
        }

        public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            var all = await AllAsync(cancellationToken);
            return all.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
        }

        public async Task<string> GetOrDefaultAsync(string key, string fallback, CancellationToken cancellationToken = default) =>
            await GetAsync(key, cancellationToken) ?? fallback;

        public async Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken cancellationToken = default)
        {
            var value = await GetAsync(key, cancellationToken);
            return value == null ? fallback : bool.TryParse(value, out var parsed) ? parsed : fallback;
        }

        public async Task<int> GetIntAsync(string key, int fallback, CancellationToken cancellationToken = default)
        {
            var value = await GetAsync(key, cancellationToken);
            return value != null && int.TryParse(value, out var parsed) ? parsed : fallback;
        }

        /// <summary>Stages a write. The caller saves, so a settings update stays atomic with
        /// its audit entry.</summary>
        public async Task SetAsync(string key, string? value, int? userId, CancellationToken cancellationToken = default)
        {
            var existing = await _context.NotificationSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
            var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();

            if (existing == null)
            {
                _context.NotificationSettings.Add(new NotificationSetting
                {
                    Key = key,
                    Value = trimmed,
                    IsSecret = NotificationSettingKeys.IsSecret(key),
                    UpdatedAt = DateTime.UtcNow,
                    UpdatedByUserId = userId
                });
            }
            else
            {
                existing.Value = trimmed;
                existing.IsSecret = NotificationSettingKeys.IsSecret(key);
                existing.UpdatedAt = DateTime.UtcNow;
                existing.UpdatedByUserId = userId;
            }

            _cache = null;
        }

        /// <summary>Everything an email or push render needs about the company, in one object.</summary>
        public async Task<NotificationBranding> GetBrandingAsync(CancellationToken cancellationToken = default)
        {
            var all = await AllAsync(cancellationToken);

            string Value(string key, string fallback) =>
                all.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v! : fallback;

            string? Optional(string key) =>
                all.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

            return new NotificationBranding
            {
                AppName = Value(NotificationSettingKeys.AppName, "DAMS"),
                CompanyName = Value(NotificationSettingKeys.CompanyName, "DAMS"),
                LogoUrl = Optional(NotificationSettingKeys.CompanyLogoUrl),
                SupportEmail = Optional(NotificationSettingKeys.SupportEmail),
                SupportPhone = Optional(NotificationSettingKeys.SupportPhone),
                Address = Optional(NotificationSettingKeys.CompanyAddress),
                EmailHeader = Optional(NotificationSettingKeys.EmailHeader),
                EmailFooter = Optional(NotificationSettingKeys.EmailFooter),
                PrimaryColor = SafeColor(Optional(NotificationSettingKeys.BrandPrimaryColor), "#1f3a5f"),
                AccentColor = SafeColor(Optional(NotificationSettingKeys.BrandAccentColor), "#c9a227"),
                CopyrightText = Optional(NotificationSettingKeys.CopyrightText),
                SocialLinks = Optional(NotificationSettingKeys.SocialLinks),
                DateFormat = Value(NotificationSettingKeys.DateFormat, "dd MMM yyyy"),
                CurrencySymbol = Value(NotificationSettingKeys.CurrencySymbol, "PKR"),
                PublicBaseUrl = Optional(NotificationSettingKeys.PublicBaseUrl)
            };
        }

        /// <summary>A colour goes straight into an inline style, so only a literal hex value
        /// is accepted — anything else falls back to the DAMS default.</summary>
        private static string SafeColor(string? value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            var trimmed = value.Trim();
            if (trimmed.Length is not (4 or 7) || trimmed[0] != '#')
                return fallback;

            return trimmed[1..].All(Uri.IsHexDigit) ? trimmed : fallback;
        }
    }

    public sealed class NotificationBranding
    {
        public required string AppName { get; init; }

        public required string CompanyName { get; init; }

        public string? LogoUrl { get; init; }

        public string? SupportEmail { get; init; }

        public string? SupportPhone { get; init; }

        public string? Address { get; init; }

        public string? EmailHeader { get; init; }

        public string? EmailFooter { get; init; }

        public required string PrimaryColor { get; init; }

        public required string AccentColor { get; init; }

        public string? CopyrightText { get; init; }

        public string? SocialLinks { get; init; }

        public required string DateFormat { get; init; }

        public required string CurrencySymbol { get; init; }

        public string? PublicBaseUrl { get; init; }
    }
}
