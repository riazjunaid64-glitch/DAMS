using System.Text.Json;
using DAMS.Application.Common;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services.Notifications
{
    /// <summary>
    /// The in-app channel. The notification row itself is the delivery, so this only has to
    /// announce it to any stream the recipient currently has open. A closed browser is not a
    /// failure — the message is already permanently in their inbox.
    /// </summary>
    public sealed class InAppChannelSender : INotificationChannelSender
    {
        private readonly INotificationRealtimeBroker _realtime;

        public InAppChannelSender(INotificationRealtimeBroker realtime)
        {
            _realtime = realtime;
        }

        public NotificationChannel Channel => NotificationChannel.InApp;

        public async Task<ChannelSendResult> SendAsync(
            NotificationDelivery delivery, Notification notification, CancellationToken cancellationToken = default)
        {
            if (notification.RecipientUserId is not > 0)
                return ChannelSendResult.Unavailable("This recipient has no DAMS account, so there is no inbox to deliver to.");

            await _realtime.PublishAsync(notification.RecipientUserId.Value, "notification", new { refresh = true });
            return ChannelSendResult.Sent($"user:{notification.RecipientUserId}");
        }
    }

    /// <summary>
    /// The email channel: render, check the address is usable, hand to the transport, record
    /// what the transport said. Acceptance is recorded as <c>Sent</c>; only a verified
    /// provider signal may ever mark a message <c>Delivered</c>.
    /// </summary>
    public sealed class EmailChannelSender : INotificationChannelSender
    {
        private readonly AppDbContext _context;
        private readonly NotificationSettingsStore _settings;
        private readonly NotificationRenderer _renderer;
        private readonly IEmailSender _sender;
        private readonly NotificationReceiptAttachmentBuilder _attachments;

        public EmailChannelSender(
            AppDbContext context,
            NotificationSettingsStore settings,
            NotificationRenderer renderer,
            IEmailSender sender,
            NotificationReceiptAttachmentBuilder attachments)
        {
            _context = context;
            _settings = settings;
            _renderer = renderer;
            _sender = sender;
            _attachments = attachments;
        }

        public NotificationChannel Channel => NotificationChannel.Email;

        public async Task<ChannelSendResult> SendAsync(
            NotificationDelivery delivery, Notification notification, CancellationToken cancellationToken = default)
        {
            if (!await _settings.GetBoolAsync(NotificationSettingKeys.EmailEnabled, false, cancellationToken))
                return ChannelSendResult.Skipped("Email is switched off for the whole application.");

            var template = await _renderer.GetTemplateAsync(notification.Type, NotificationChannel.Email, cancellationToken);
            if (template is { IsEnabled: false })
                return ChannelSendResult.Skipped("The email template for this notification is disabled.");

            string? address;
            string recipientName;

            if (notification.RecipientUserId is > 0)
            {
                var recipient = await _context.Users
                    .AsNoTracking()
                    .Where(u => u.UserId == notification.RecipientUserId)
                    .Select(u => new { u.Email, u.FullName })
                    .FirstOrDefaultAsync(cancellationToken);

                if (recipient == null)
                    return ChannelSendResult.Unavailable("The recipient account no longer exists.");

                recipientName = recipient.FullName;
                // Where a customer record carries a better address than the login, prefer it —
                // the login may be a shared or historic address. Resolving now, rather than at
                // creation, means a corrected address is picked up by a retry.
                address = await ResolvePreferredAddressAsync(notification.RecipientUserId.Value, recipient.Email, cancellationToken);
            }
            else
            {
                address = notification.RecipientEmail;
                recipientName = notification.RecipientName ?? address ?? string.Empty;
            }

            if (!SmtpEmailSender.IsValidAddress(address))
                return ChannelSendResult.Unavailable("This recipient has no valid email address on file.");

            var normalized = address!.Trim().ToLowerInvariant();
            // Suppression is a deliverability fact, not a preference. Essential messages may
            // bypass an opt-out, but they must never keep hammering an address already proven
            // invalid and damage the sender's reputation.
            if (await _context.EmailSuppressions
                    .AsNoTracking()
                    .AnyAsync(s => s.Email == normalized && s.ClearedAt == null, cancellationToken))
            {
                return ChannelSendResult.Skipped("This address is suppressed after a hard bounce.");
            }

            var rendered = await _renderer.RenderEmailAsync(notification, recipientName, cancellationToken);
            var attachments = await _attachments.BuildAsync(notification, cancellationToken);

            var result = await _sender.SendAsync(new EmailMessage
            {
                To = address,
                ToName = recipientName,
                Subject = rendered.Subject,
                HtmlBody = rendered.Html,
                TextBody = rendered.Text,
                Attachments = attachments
            }, cancellationToken);

            var masked = Mask(address);

            if (result.Success)
                return ChannelSendResult.Sent(masked, result.ProviderReference);

            if (result.IsHardBounce)
            {
                await SuppressAsync(normalized, result.Error ?? "Hard bounce.", cancellationToken);
                return ChannelSendResult.Bounced(result.Error ?? "The address was rejected.", masked);
            }

            return result.IsPermanent
                ? ChannelSendResult.PermanentFailure(result.Error ?? "The message was rejected.", masked)
                : ChannelSendResult.TransientFailure(result.Error ?? "The message could not be sent.", masked);
        }

        internal async Task<string?> ResolvePreferredAddressAsync(int userId, string? loginEmail, CancellationToken cancellationToken)
        {
            var customerEmail = await _context.Customers
                .AsNoTracking()
                .Where(c => c.UserId == userId && c.Email != null && c.Email != "")
                .OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt)
                .Select(c => c.Email)
                .FirstOrDefaultAsync(cancellationToken);

            return SmtpEmailSender.IsValidAddress(customerEmail) ? customerEmail : loginEmail;
        }

        private async Task SuppressAsync(string email, string reason, CancellationToken cancellationToken)
        {
            var existing = await _context.EmailSuppressions.FirstOrDefaultAsync(s => s.Email == email, cancellationToken);
            if (existing != null)
            {
                existing.ClearedAt = null;
                existing.Reason = LeadContactNormalizer.Limit(reason, 300);
                return;
            }

            _context.EmailSuppressions.Add(new EmailSuppression
            {
                Email = email,
                Reason = LeadContactNormalizer.Limit(reason, 300),
                CreatedAt = DateTime.UtcNow
            });
        }

        /// <summary>Delivery history shows enough to identify a mailbox, not enough to harvest one.</summary>
        public static string Mask(string address)
        {
            var at = address.IndexOf('@');
            if (at <= 0)
                return "***";

            var name = address[..at];
            var domain = address[at..];
            var visible = name.Length <= 2 ? name[..1] : name[..2];
            return $"{visible}{new string('*', Math.Min(6, Math.Max(1, name.Length - visible.Length)))}{domain}";
        }
    }

    /// <summary>
    /// The browser push channel. One notification fans out to every live subscription the
    /// recipient has, and a subscription the browser has thrown away is deactivated instead
    /// of being retried forever.
    /// </summary>
    public sealed class WebPushChannelSender : INotificationChannelSender
    {
        private readonly AppDbContext _context;
        private readonly NotificationSettingsStore _settings;
        private readonly NotificationRenderer _renderer;
        private readonly IWebPushSender _push;
        private readonly PushSubscriptionService _subscriptions;
        private readonly NotificationOptions _options;

        public WebPushChannelSender(
            AppDbContext context,
            NotificationSettingsStore settings,
            NotificationRenderer renderer,
            IWebPushSender push,
            PushSubscriptionService subscriptions,
            NotificationOptions options)
        {
            _context = context;
            _settings = settings;
            _renderer = renderer;
            _push = push;
            _subscriptions = subscriptions;
            _options = options;
        }

        public NotificationChannel Channel => NotificationChannel.WebPush;

        public async Task<ChannelSendResult> SendAsync(
            NotificationDelivery delivery, Notification notification, CancellationToken cancellationToken = default)
        {
            if (!await _settings.GetBoolAsync(NotificationSettingKeys.PushEnabled, false, cancellationToken))
                return ChannelSendResult.Skipped("Browser push is switched off for the whole application.");

            var template = await _renderer.GetTemplateAsync(notification.Type, NotificationChannel.WebPush, cancellationToken);
            if (template is { IsEnabled: false })
                return ChannelSendResult.Skipped("The push template for this notification is disabled.");

            if (notification.RecipientUserId is not > 0)
                return ChannelSendResult.Unavailable("This recipient has no DAMS account, so no browser can be subscribed.");

            var credentials = await _subscriptions.GetCredentialsAsync(cancellationToken);
            if (credentials == null)
                return ChannelSendResult.Skipped("Browser push keys are not configured.");

            var subscriptions = await _context.PushSubscriptions
                .Where(s => s.UserId == notification.RecipientUserId && s.IsActive)
                .ToListAsync(cancellationToken);

            if (subscriptions.Count == 0)
                return ChannelSendResult.Unavailable("This recipient has no browser with notifications switched on.");

            var rendered = await _renderer.RenderPushAsync(notification, cancellationToken);
            var payload = JsonSerializer.Serialize(new
            {
                title = rendered.Title,
                body = rendered.Body,
                url = rendered.Url,
                icon = rendered.Icon,
                badge = rendered.Badge,
                tag = rendered.Tag,
                notificationId = notification.Id
            });

            var delivered = 0;
            var lastError = string.Empty;

            foreach (var subscription in subscriptions)
            {
                if (!_subscriptions.IsAllowedEndpoint(subscription.Endpoint))
                {
                    subscription.IsActive = false;
                    subscription.DeactivatedAt = DateTime.UtcNow;
                    subscription.DeactivationReason = "The stored endpoint is not an approved browser push service.";
                    lastError = subscription.DeactivationReason;
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
                    continue;
                }

                lastError = result.Error ?? "The push service rejected the message.";

                if (result.SubscriptionGone)
                {
                    subscription.IsActive = false;
                    subscription.DeactivatedAt = DateTime.UtcNow;
                    subscription.DeactivationReason = "The browser discarded this subscription.";
                    continue;
                }

                subscription.ConsecutiveFailures++;
                if (subscription.ConsecutiveFailures >= _options.PushFailureThreshold)
                {
                    subscription.IsActive = false;
                    subscription.DeactivatedAt = DateTime.UtcNow;
                    subscription.DeactivationReason = $"Deactivated after {subscription.ConsecutiveFailures} consecutive failures.";
                }
            }

            await _context.SaveChangesAsync(cancellationToken);

            var target = $"{subscriptions.Count} device(s)";

            if (delivered > 0)
                return ChannelSendResult.Sent(target);

            // Every device is gone: another attempt cannot help, and the recipient will be
            // offered the opt-in again next time they open DAMS.
            return subscriptions.All(s => !s.IsActive)
                ? ChannelSendResult.Unavailable("Every browser subscription for this recipient has expired.")
                : ChannelSendResult.TransientFailure(lastError, target);
        }
    }
}
