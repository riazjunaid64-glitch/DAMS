using System.Text.Json;
using DAMS.Application.Common;
using DAMS.Application.DTOs.NotificationDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace DAMS.Application.Services.Notifications
{
    /// <summary>
    /// Browser push subscriptions. One row per browser; a user may have many. Nothing here
    /// ever returns endpoint or key material to a client — the only public value is the VAPID
    /// public key, which is public by design.
    /// </summary>
    public sealed class PushSubscriptionService : IPushSubscriptionService
    {
        private readonly AppDbContext _context;
        private readonly NotificationSettingsStore _settings;
        private readonly IWebPushSender _push;
        private readonly NotificationOptions _options;

        public PushSubscriptionService(
            AppDbContext context,
            NotificationSettingsStore settings,
            IWebPushSender push,
            NotificationOptions options)
        {
            _context = context;
            _settings = settings;
            _push = push;
            _options = options;
        }

        public async Task<PushConfigDto> GetConfigAsync(NotificationUserContext ctx, CancellationToken cancellationToken = default)
        {
            var enabled = await _settings.GetBoolAsync(NotificationSettingKeys.PushEnabled, false, cancellationToken);
            var publicKey = await _settings.GetAsync(NotificationSettingKeys.PushVapidPublicKey, cancellationToken);
            var privateKey = await _settings.GetAsync(NotificationSettingKeys.PushVapidPrivateKey, cancellationToken);
            var subject = await _settings.GetAsync(NotificationSettingKeys.PushVapidSubject, cancellationToken);

            var devices = await _context.PushSubscriptions
                .AsNoTracking()
                .CountAsync(s => s.UserId == ctx.UserId && s.IsActive, cancellationToken);

            return new PushConfigDto
            {
                // Without both halves of the key pair nothing can be signed, so the client is
                // told push is unavailable rather than being walked into a dead permission
                // prompt it can never use.
                Enabled = enabled
                          && !string.IsNullOrWhiteSpace(publicKey)
                          && !string.IsNullOrWhiteSpace(privateKey)
                          && IsValidVapidSubject(subject),
                PublicKey = publicKey,
                DisplayName = await _settings.GetOrDefaultAsync(NotificationSettingKeys.PushDisplayName, "DAMS", cancellationToken),
                IconUrl = await _settings.GetAsync(NotificationSettingKeys.PushIconUrl, cancellationToken),
                BadgeUrl = await _settings.GetAsync(NotificationSettingKeys.PushBadgeUrl, cancellationToken),
                HasActiveSubscription = devices > 0,
                DeviceCount = devices
            };
        }

        public async Task RegisterAsync(
            NotificationUserContext ctx, RegisterPushSubscriptionDto dto, CancellationToken cancellationToken = default)
        {
            var endpoint = Validate(dto.Endpoint);
            var p256dh = RequireKey(dto.P256dh, "p256dh");
            var auth = RequireKey(dto.Auth, "auth");

            var existing = await _context.PushSubscriptions
                .FirstOrDefaultAsync(s => s.Endpoint == endpoint, cancellationToken);

            if (existing == null)
            {
                var activeCount = await _context.PushSubscriptions
                    .CountAsync(s => s.UserId == ctx.UserId && s.IsActive, cancellationToken);
                if (activeCount >= Math.Clamp(_options.MaxPushSubscriptionsPerUser, 1, 50))
                    throw new InvalidOperationException(
                        "This account has reached its browser notification device limit. Remove an old device first.");

                _context.PushSubscriptions.Add(new PushSubscription
                {
                    UserId = ctx.UserId,
                    Endpoint = endpoint,
                    P256dh = p256dh,
                    Auth = auth,
                    DeviceLabel = LeadContactNormalizer.LimitOrNull(LeadContactNormalizer.Clean(dto.DeviceLabel), 150),
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    LastSeenAt = DateTime.UtcNow
                });
            }
            else
            {
                if (existing.UserId != ctx.UserId || !existing.IsActive)
                {
                    var activeCount = await _context.PushSubscriptions
                        .CountAsync(s => s.UserId == ctx.UserId && s.IsActive, cancellationToken);
                    if (activeCount >= Math.Clamp(_options.MaxPushSubscriptionsPerUser, 1, 50))
                        throw new InvalidOperationException(
                            "This account has reached its browser notification device limit. Remove an old device first.");
                }

                // A shared computer: the same browser endpoint now belongs to whoever is
                // signed in. Reassigning rather than duplicating is what stops the previous
                // user's notifications from landing on this machine.
                existing.UserId = ctx.UserId;
                existing.P256dh = p256dh;
                existing.Auth = auth;
                existing.DeviceLabel = LeadContactNormalizer.LimitOrNull(LeadContactNormalizer.Clean(dto.DeviceLabel), 150)
                                       ?? existing.DeviceLabel;
                existing.IsActive = true;
                existing.DeactivatedAt = null;
                existing.DeactivationReason = null;
                existing.ConsecutiveFailures = 0;
                existing.LastSeenAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task UnregisterAsync(NotificationUserContext ctx, string endpoint, CancellationToken cancellationToken = default)
        {
            var normalized = LeadContactNormalizer.Clean(endpoint);
            if (normalized == null)
                return;

            // Scoped to the caller: knowing an endpoint must not let anyone unsubscribe
            // somebody else's device.
            var subscription = await _context.PushSubscriptions
                .FirstOrDefaultAsync(s => s.Endpoint == normalized && s.UserId == ctx.UserId, cancellationToken);

            if (subscription == null)
                return;

            _context.PushSubscriptions.Remove(subscription);
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task<int> UnregisterAllAsync(int userId, CancellationToken cancellationToken = default)
        {
            var subscriptions = await _context.PushSubscriptions
                .Where(s => s.UserId == userId)
                .ToListAsync(cancellationToken);

            if (subscriptions.Count == 0)
                return 0;

            _context.PushSubscriptions.RemoveRange(subscriptions);
            await _context.SaveChangesAsync(cancellationToken);
            return subscriptions.Count;
        }

        public async Task<List<PushDeviceDto>> GetMyDevicesAsync(
            NotificationUserContext ctx, CancellationToken cancellationToken = default)
        {
            var rows = await _context.PushSubscriptions
                .AsNoTracking()
                .Where(s => s.UserId == ctx.UserId)
                .OrderByDescending(s => s.LastSeenAt)
                .ToListAsync(cancellationToken);

            return rows.Select(s => new PushDeviceDto
            {
                Id = s.Id,
                DeviceLabel = s.DeviceLabel,
                Fingerprint = Fingerprint(s.Endpoint),
                CreatedAt = s.CreatedAt,
                LastSeenAt = s.LastSeenAt,
                LastSuccessAt = s.LastSuccessAt,
                IsActive = s.IsActive
            }).ToList();
        }

        public async Task<int> SendTestAsync(NotificationUserContext ctx, CancellationToken cancellationToken = default)
        {
            var config = await GetConfigAsync(ctx, cancellationToken);
            if (!config.Enabled)
                throw new InvalidOperationException("Browser push is not enabled or not configured.");

            var credentials = await GetCredentialsAsync(cancellationToken)
                ?? throw new InvalidOperationException("Browser push keys are not configured.");

            var subscriptions = await _context.PushSubscriptions
                .Where(s => s.UserId == ctx.UserId && s.IsActive)
                .ToListAsync(cancellationToken);

            if (subscriptions.Count == 0)
                throw new InvalidOperationException("No browser on this account has notifications switched on yet.");

            var payload = JsonSerializer.Serialize(new
            {
                title = config.DisplayName,
                body = "Browser notifications are working. You will be told about important DAMS activity here.",
                url = "/notifications",
                icon = config.IconUrl,
                badge = config.BadgeUrl,
                tag = "dams-test"
            });

            var delivered = 0;
            foreach (var subscription in subscriptions)
            {
                if (!IsAllowedEndpoint(subscription.Endpoint))
                {
                    subscription.IsActive = false;
                    subscription.DeactivatedAt = DateTime.UtcNow;
                    subscription.DeactivationReason = "The stored endpoint is not an approved browser push service.";
                    continue;
                }

                var result = await _push.SendAsync(
                    new WebPushTarget(subscription.Endpoint, subscription.P256dh, subscription.Auth),
                    payload, credentials, cancellationToken);

                if (result.Success)
                {
                    subscription.LastSuccessAt = DateTime.UtcNow;
                    subscription.ConsecutiveFailures = 0;
                    delivered++;
                }
                else if (result.SubscriptionGone)
                {
                    subscription.IsActive = false;
                    subscription.DeactivatedAt = DateTime.UtcNow;
                    subscription.DeactivationReason = "The browser discarded this subscription.";
                }
            }

            await _context.SaveChangesAsync(cancellationToken);

            if (delivered == 0)
                throw new InvalidOperationException("The test push could not be delivered to any of your devices.");

            return delivered;
        }

        public async Task<WebPushCredentials?> GetCredentialsAsync(CancellationToken cancellationToken = default)
        {
            var publicKey = await _settings.GetAsync(NotificationSettingKeys.PushVapidPublicKey, cancellationToken);
            var privateKey = await _settings.GetAsync(NotificationSettingKeys.PushVapidPrivateKey, cancellationToken);
            var subject = await _settings.GetAsync(NotificationSettingKeys.PushVapidSubject, cancellationToken);

            if (string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(privateKey))
                return null;

            if (!IsValidVapidSubject(subject))
                return null;

            return new WebPushCredentials(publicKey, privateKey,
                subject!);
        }

        /// <summary>Last few characters only — enough to tell two devices apart, useless on its own.</summary>
        public static string Fingerprint(string endpoint) =>
            endpoint.Length <= 8 ? "…" : $"…{endpoint[^8..]}";

        internal bool IsAllowedEndpoint(string? endpoint)
        {
            try
            {
                _ = Validate(endpoint);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        internal static bool IsValidVapidSubject(string? subject)
        {
            var value = LeadContactNormalizer.Clean(subject);
            if (value == null)
                return false;

            if (value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                return SmtpEmailSender.IsValidAddress(value[7..]);

            return Uri.TryCreate(value, UriKind.Absolute, out var uri)
                   && uri.Scheme == Uri.UriSchemeHttps
                   && !string.IsNullOrWhiteSpace(uri.Host);
        }

        private string Validate(string? endpoint)
        {
            var value = LeadContactNormalizer.Clean(endpoint)
                ?? throw new InvalidOperationException("A push endpoint is required.");

            // Bounded so the unique index over it stays comfortably inside SQL Server's index
            // key limit. Real push service URLs are far shorter than this.
            if (value.Length > 400)
                throw new InvalidOperationException("That push endpoint is not valid.");

            // Only a real https push service URL is stored, so a crafted subscription cannot
            // turn the sender into a request forwarder.
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(uri.UserInfo)
                || !uri.IsDefaultPort)
                throw new InvalidOperationException("A push endpoint must be an https address.");

            var host = uri.IdnHost.TrimEnd('.');
            var allowed = _options.AllowedPushEndpointHosts
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Any(pattern =>
                {
                    var normalized = pattern.Trim().TrimEnd('.').ToLowerInvariant();
                    return normalized.StartsWith('.')
                        ? host.EndsWith(normalized, StringComparison.OrdinalIgnoreCase)
                        : host.Equals(normalized, StringComparison.OrdinalIgnoreCase);
                });

            if (!allowed)
                throw new InvalidOperationException("That address is not a recognized browser push service.");

            return uri.AbsoluteUri;
        }

        private static string RequireKey(string? value, string name)
        {
            var cleaned = LeadContactNormalizer.Clean(value)
                ?? throw new InvalidOperationException($"The {name} key is required.");

            if (cleaned.Length > 200 || !cleaned.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '='))
                throw new InvalidOperationException($"The {name} key is not valid.");

            byte[] decoded;
            try
            {
                decoded = Base64Url.Decode(cleaned);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException($"The {name} key is not valid.");
            }

            if (name == "auth" && decoded.Length != 16)
                throw new InvalidOperationException("The auth key is not valid.");

            if (name == "p256dh")
            {
                if (decoded.Length != 65 || decoded[0] != 0x04)
                    throw new InvalidOperationException("The p256dh key is not valid.");

                try
                {
                    using var key = ECDiffieHellman.Create(new ECParameters
                    {
                        Curve = ECCurve.NamedCurves.nistP256,
                        Q = new ECPoint { X = decoded[1..33], Y = decoded[33..65] }
                    });
                }
                catch (CryptographicException)
                {
                    throw new InvalidOperationException("The p256dh key is not valid.");
                }
            }

            return cleaned;
        }
    }
}
