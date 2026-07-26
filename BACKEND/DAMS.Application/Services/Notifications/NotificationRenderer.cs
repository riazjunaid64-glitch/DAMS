using System.Net;
using System.Text;
using System.Text.Json;
using DAMS.Application.Common;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services.Notifications
{
    public sealed class RenderedEmail
    {
        public required string Subject { get; init; }

        public required string Html { get; init; }

        public required string Text { get; init; }

        public required string ActionUrl { get; init; }
    }

    public sealed class RenderedPush
    {
        public required string Title { get; init; }

        public required string Body { get; init; }

        public required string Url { get; init; }

        public string? Icon { get; init; }

        public string? Badge { get; init; }

        public required string Tag { get; init; }
    }

    /// <summary>
    /// Turns a stored notification into the exact bytes a channel sends. Templates come from
    /// the database when an admin has edited one and from the catalog otherwise, so the
    /// system is never dependent on seed data having been applied.
    ///
    /// Push copy is rendered from a deliberately short template and is never allowed to carry
    /// the detail an email carries: a push banner can appear on a lock screen or a shared
    /// desktop, so it says what happened and where to look, not how much or for whom.
    /// </summary>
    public sealed class NotificationRenderer
    {
        private readonly AppDbContext _context;
        private readonly NotificationSettingsStore _settings;
        private Dictionary<(NotificationType, NotificationChannel), NotificationTemplate>? _templates;

        public NotificationRenderer(AppDbContext context, NotificationSettingsStore settings)
        {
            _context = context;
            _settings = settings;
        }

        public async Task<NotificationTemplate?> GetTemplateAsync(
            NotificationType type, NotificationChannel channel, CancellationToken cancellationToken)
        {
            _templates ??= await _context.NotificationTemplates
                .AsNoTracking()
                .ToDictionaryAsync(t => (t.Type, t.Channel), cancellationToken);

            return _templates.TryGetValue((type, channel), out var template) ? template : null;
        }

        public async Task<RenderedEmail> RenderEmailAsync(
            Notification notification,
            string recipientName,
            CancellationToken cancellationToken = default,
            NotificationTemplate? templateOverride = null)
        {
            var branding = await _settings.GetBrandingAsync(cancellationToken);
            var definition = NotificationCatalog.Get(notification.Type);
            var template = templateOverride
                           ?? await GetTemplateAsync(notification.Type, NotificationChannel.Email, cancellationToken);

            var actionUrl = NotificationLink.Absolute(branding.PublicBaseUrl,
                NotificationLink.Sanitize(template?.ActionUrl) ?? notification.DeepLink);

            var values = BuildValues(notification, recipientName, branding, actionUrl);

            var subject = NotificationTemplateRenderer.RenderText(
                template is { IsEnabled: true } ? template.Subject : definition.DefaultSubject, values);
            if (string.IsNullOrWhiteSpace(subject))
                subject = notification.Title;

            var heading = NotificationTemplateRenderer.RenderText(
                template is { IsEnabled: true } ? template.Heading ?? notification.Title : notification.Title, values);

            var bodyHtml = NotificationTemplateRenderer.RenderHtml(
                template is { IsEnabled: true } ? template.Body : definition.DefaultBody, values);
            if (string.IsNullOrWhiteSpace(bodyHtml))
                bodyHtml = WebUtility.HtmlEncode(notification.Message);

            var actionText = NotificationTemplateRenderer.RenderText(
                template is { IsEnabled: true } ? template.ActionText ?? definition.DefaultActionText : definition.DefaultActionText,
                values);

            var footer = NotificationTemplateRenderer.RenderHtml(
                template is { IsEnabled: true } ? template.Footer : null, values);

            var html = BuildLayout(branding, heading, bodyHtml, actionText, actionUrl, footer);
            var text = BuildPlainText(branding, heading, bodyHtml, actionText, actionUrl, footer);

            return new RenderedEmail { Subject = subject, Html = html, Text = text, ActionUrl = actionUrl };
        }

        public async Task<RenderedPush> RenderPushAsync(
            Notification notification,
            CancellationToken cancellationToken = default,
            NotificationTemplate? templateOverride = null)
        {
            var branding = await _settings.GetBrandingAsync(cancellationToken);
            var template = templateOverride
                           ?? await GetTemplateAsync(notification.Type, NotificationChannel.WebPush, cancellationToken);
            var values = BuildValues(notification, string.Empty, branding, notification.DeepLink ?? "/notifications");
            var useTemplate = template is { IsEnabled: true } && IsSafePushTemplate(notification.Type, template);

            var title = NotificationTemplateRenderer.RenderText(
                useTemplate ? template!.Subject : SafePushTitle(notification), values);
            if (string.IsNullOrWhiteSpace(title))
                title = branding.AppName;

            var body = NotificationTemplateRenderer.RenderText(
                useTemplate ? template!.Body : SafePushBody(notification), values);
            if (string.IsNullOrWhiteSpace(body))
                body = "Open DAMS to see the details.";

            return new RenderedPush
            {
                Title = Truncate(title, 80),
                Body = Truncate(body, 160),
                Url = NotificationLink.Sanitize(useTemplate ? template!.ActionUrl : null)
                      ?? notification.DeepLink
                      ?? await _settings.GetOrDefaultAsync(NotificationSettingKeys.PushDefaultUrl, "/notifications", cancellationToken),
                Icon = (useTemplate ? template!.IconUrl : null)
                       ?? await _settings.GetAsync(NotificationSettingKeys.PushIconUrl, cancellationToken),
                Badge = (useTemplate ? template!.BadgeUrl : null)
                        ?? await _settings.GetAsync(NotificationSettingKeys.PushBadgeUrl, cancellationToken),
                // Same tag collapses repeats of the same event on the device instead of
                // stacking a banner per retry.
                Tag = $"dams-{notification.Type}-{notification.Id}"
            };
        }

        private static bool IsSafePushTemplate(NotificationType type, NotificationTemplate template)
        {
            try
            {
                var allowed = NotificationCatalog.PushVariablesFor(type);
                NotificationTemplateRenderer.Validate(template.Subject, allowed, "Push title");
                NotificationTemplateRenderer.Validate(template.Body, allowed, "Push body");
                return true;
            }
            catch (NotificationTemplateException)
            {
                // A database restored from an older release may contain a push template that
                // predates the lock-screen privacy rules. Fall back to safe built-in wording
                // at send time instead of exposing those values.
                return false;
            }
        }

        /// <summary>
        /// The safe default push headline. It names the event, never the customer, the amount
        /// or any other detail that should not be readable over somebody's shoulder.
        /// </summary>
        internal static string SafePushTitle(Notification notification) => notification.Type switch
        {
            NotificationType.PaymentReceipt => "Payment received",
            NotificationType.BookingApproved => "Booking approved",
            NotificationType.BookingRejected => "Booking update",
            NotificationType.BookingCancelled => "Booking cancelled",
            NotificationType.InstallmentDue => "Installment due soon",
            NotificationType.InstallmentOverdue => "Installment overdue",
            NotificationType.UserMentioned => "You were mentioned",
            NotificationType.AccountSecurity => "Account notice",
            _ => NotificationCatalog.Get(notification.Type).Name
        };

        internal static string SafePushBody(Notification notification) => notification.Type switch
        {
            NotificationType.PaymentReceipt => "Open DAMS to view your receipt.",
            NotificationType.BookingApproved => "Open DAMS to view your booking.",
            NotificationType.BookingRejected => "Open DAMS to see the details.",
            NotificationType.BookingCancelled => "Open DAMS to see the details.",
            NotificationType.InstallmentDue => "Open DAMS to review your payment plan.",
            NotificationType.InstallmentOverdue => "Open DAMS to review your payment plan.",
            NotificationType.AccountSecurity => "Open DAMS to review your account.",
            NotificationType.AdminAnnouncement => Truncate(notification.Message, 160),
            // Staff browsers can still mirror notifications onto a shared lock screen.
            // CRM names, record identifiers, and free-form notes therefore stay in DAMS.
            _ => "Open DAMS to see the details."
        };

        private static Dictionary<string, string?> BuildValues(
            Notification notification, string recipientName, NotificationBranding branding, string actionUrl)
        {
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["companyName"] = branding.CompanyName,
                ["appName"] = branding.AppName,
                ["supportEmail"] = branding.SupportEmail,
                ["supportPhone"] = branding.SupportPhone,
                ["recipientName"] = recipientName,
                ["actionUrl"] = actionUrl,
                ["year"] = DateTime.UtcNow.Year.ToString(),
                ["title"] = notification.Title,
                ["message"] = notification.Message
            };

            if (!string.IsNullOrWhiteSpace(notification.DataJson))
            {
                try
                {
                    var stored = JsonSerializer.Deserialize<Dictionary<string, string?>>(notification.DataJson);
                    if (stored != null)
                    {
                        // Event data wins over the generic defaults, but never over branding.
                        foreach (var pair in stored)
                            values[pair.Key] = pair.Value;

                        values["companyName"] = branding.CompanyName;
                        values["appName"] = branding.AppName;
                        values["actionUrl"] = actionUrl;
                        values["recipientName"] = string.IsNullOrWhiteSpace(recipientName)
                            ? values.GetValueOrDefault("recipientName")
                            : recipientName;
                    }
                }
                catch (JsonException)
                {
                    // Unreadable stored data must not stop the notification going out.
                }
            }

            return values;
        }

        private static string BuildLayout(
            NotificationBranding branding, string heading, string bodyHtml, string? actionText, string actionUrl, string? footerHtml)
        {
            var safeAction = WebUtility.HtmlEncode(actionUrl);
            var builder = new StringBuilder();

            builder.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\">")
                   .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
                   .Append("<title>").Append(WebUtility.HtmlEncode(heading)).Append("</title></head>")
                   .Append("<body style=\"margin:0;padding:0;background:#f4f5f7;font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:#1f2933;\">")
                   .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#f4f5f7;padding:24px 12px;\"><tr><td align=\"center\">")
                   .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:600px;background:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 1px 3px rgba(0,0,0,0.08);\">");

            builder.Append("<tr><td style=\"background:").Append(branding.PrimaryColor).Append(";padding:20px 24px;\">");
            if (!string.IsNullOrWhiteSpace(branding.LogoUrl))
                builder.Append("<img src=\"").Append(WebUtility.HtmlEncode(branding.LogoUrl))
                       .Append("\" alt=\"").Append(WebUtility.HtmlEncode(branding.CompanyName))
                       .Append("\" height=\"36\" style=\"display:block;border:0;max-height:36px;\">");
            else
                builder.Append("<span style=\"color:#ffffff;font-size:18px;font-weight:600;\">")
                       .Append(WebUtility.HtmlEncode(branding.CompanyName)).Append("</span>");
            builder.Append("</td></tr>");

            if (!string.IsNullOrWhiteSpace(branding.EmailHeader))
                builder.Append("<tr><td style=\"padding:16px 24px 0;font-size:14px;color:#52606d;\">")
                       .Append(NotificationTemplateRenderer.Sanitize(branding.EmailHeader)).Append("</td></tr>");

            builder.Append("<tr><td style=\"padding:24px;\">")
                   .Append("<h1 style=\"margin:0 0 12px;font-size:20px;line-height:1.3;color:#102a43;\">")
                   .Append(WebUtility.HtmlEncode(heading)).Append("</h1>")
                   .Append("<div style=\"font-size:15px;line-height:1.6;color:#334e68;\">").Append(bodyHtml).Append("</div>");

            if (!string.IsNullOrWhiteSpace(actionText))
                builder.Append("<div style=\"margin:24px 0 4px;\"><a href=\"").Append(safeAction)
                       .Append("\" style=\"display:inline-block;background:").Append(branding.AccentColor)
                       .Append(";color:#1a1a1a;text-decoration:none;font-weight:600;padding:12px 22px;border-radius:8px;font-size:15px;\">")
                       .Append(WebUtility.HtmlEncode(actionText)).Append("</a></div>")
                       .Append("<p style=\"font-size:12px;color:#829ab1;margin:12px 0 0;word-break:break-all;\">")
                       .Append("If the button does not work, open: ").Append(safeAction).Append("</p>");

            builder.Append("</td></tr>");

            if (!string.IsNullOrWhiteSpace(footerHtml))
                builder.Append("<tr><td style=\"padding:0 24px 16px;font-size:13px;color:#52606d;\">").Append(footerHtml).Append("</td></tr>");

            builder.Append("<tr><td style=\"background:#f8f9fb;padding:18px 24px;font-size:12px;color:#627d98;border-top:1px solid #e4e7eb;\">");

            if (!string.IsNullOrWhiteSpace(branding.EmailFooter))
                builder.Append("<div style=\"margin-bottom:8px;\">")
                       .Append(NotificationTemplateRenderer.Sanitize(branding.EmailFooter)).Append("</div>");

            builder.Append("<div>").Append(WebUtility.HtmlEncode(branding.CompanyName));
            if (!string.IsNullOrWhiteSpace(branding.Address))
                builder.Append(" · ").Append(WebUtility.HtmlEncode(branding.Address));
            builder.Append("</div>");

            if (!string.IsNullOrWhiteSpace(branding.SupportEmail) || !string.IsNullOrWhiteSpace(branding.SupportPhone))
            {
                builder.Append("<div style=\"margin-top:4px;\">");
                if (!string.IsNullOrWhiteSpace(branding.SupportEmail))
                    builder.Append("<a href=\"mailto:").Append(WebUtility.HtmlEncode(branding.SupportEmail))
                           .Append("\" style=\"color:#334e68;\">").Append(WebUtility.HtmlEncode(branding.SupportEmail)).Append("</a>");
                if (!string.IsNullOrWhiteSpace(branding.SupportPhone))
                    builder.Append(" · ").Append(WebUtility.HtmlEncode(branding.SupportPhone));
                builder.Append("</div>");
            }

            builder.Append("<div style=\"margin-top:8px;color:#829ab1;\">")
                   .Append(WebUtility.HtmlEncode(branding.CopyrightText ?? $"© {DateTime.UtcNow.Year} {branding.CompanyName}"))
                   .Append("</div>");

            builder.Append("</td></tr></table></td></tr></table></body></html>");
            return builder.ToString();
        }

        private static string BuildPlainText(
            NotificationBranding branding, string heading, string bodyHtml, string? actionText, string actionUrl, string? footerHtml)
        {
            var builder = new StringBuilder();
            builder.AppendLine(heading).AppendLine();
            builder.AppendLine(NotificationTemplateRenderer.ToPlainText(bodyHtml)).AppendLine();

            if (!string.IsNullOrWhiteSpace(actionText))
                builder.AppendLine($"{actionText}: {actionUrl}").AppendLine();

            var footer = NotificationTemplateRenderer.ToPlainText(footerHtml);
            if (!string.IsNullOrWhiteSpace(footer))
                builder.AppendLine(footer).AppendLine();

            builder.AppendLine("--");
            builder.AppendLine(branding.CompanyName);
            if (!string.IsNullOrWhiteSpace(branding.Address))
                builder.AppendLine(branding.Address);
            if (!string.IsNullOrWhiteSpace(branding.SupportEmail))
                builder.AppendLine(branding.SupportEmail);
            if (!string.IsNullOrWhiteSpace(branding.SupportPhone))
                builder.AppendLine(branding.SupportPhone);

            return builder.ToString().TrimEnd();
        }

        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : value[..(max - 1)].TrimEnd() + "…";
    }
}
