using DAMS.Application.Common;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DAMS.Application.Services.Notifications
{
    /// <summary>
    /// Attaches the official receipt to a payment receipt email.
    ///
    /// The document is built from <see cref="IBookingService.GetPaymentReceiptAsync"/> — the
    /// same projection the on-screen receipt uses — so the attachment can never contain a
    /// figure the notification system worked out for itself.
    /// </summary>
    public sealed class NotificationReceiptAttachmentBuilder
    {
        private readonly IBookingService _bookings;
        private readonly NotificationSettingsStore _settings;
        private readonly ILogger<NotificationReceiptAttachmentBuilder> _logger;

        public NotificationReceiptAttachmentBuilder(
            IBookingService bookings,
            NotificationSettingsStore settings,
            ILogger<NotificationReceiptAttachmentBuilder> logger)
        {
            _bookings = bookings;
            _settings = settings;
            _logger = logger;
        }

        public async Task<IReadOnlyList<EmailAttachment>> BuildAsync(
            Notification notification, CancellationToken cancellationToken = default)
        {
            if (notification.Type != NotificationType.PaymentReceipt
                || notification.EntityType != NotificationEntityType.Payment
                || notification.EntityId is null or <= 0)
                return Array.Empty<EmailAttachment>();

            if (!await _settings.GetBoolAsync(NotificationSettingKeys.EmailAttachReceipt, true, cancellationToken))
                return Array.Empty<EmailAttachment>();

            var bookingId = BookingIdFromLink(notification.DeepLink);
            if (bookingId == null)
                return Array.Empty<EmailAttachment>();

            try
            {
                var receipt = await _bookings.GetPaymentReceiptAsync(bookingId.Value, notification.EntityId.Value);
                var branding = await _settings.GetBrandingAsync(cancellationToken);

                var pdf = ReceiptPdfWriter.Build(
                    receipt,
                    branding.CompanyName,
                    branding.Address,
                    branding.SupportEmail,
                    branding.SupportPhone,
                    branding.CurrencySymbol);

                var name = string.IsNullOrWhiteSpace(receipt.ReceiptNumber)
                    ? $"Receipt-{receipt.PaymentId}.pdf"
                    : $"{Safe(receipt.ReceiptNumber)}.pdf";

                return new[] { new EmailAttachment(name, "application/pdf", pdf) };
            }
            catch (Exception ex)
            {
                // An email with the figures in its body is far better than no email at all;
                // the attachment is a convenience, not the record.
                _logger.LogWarning(ex,
                    "The receipt attachment for payment {PaymentId} could not be built. The email is sent without it.",
                    notification.EntityId);
                return Array.Empty<EmailAttachment>();
            }
        }

        private static int? BookingIdFromLink(string? deepLink)
        {
            if (string.IsNullOrWhiteSpace(deepLink))
                return null;

            var segments = deepLink.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return segments.Length >= 2
                   && segments[0].Equals("receipt", StringComparison.OrdinalIgnoreCase)
                   && int.TryParse(segments[1], out var bookingId)
                ? bookingId
                : null;
        }

        /// <summary>A file name reaches a mail client; only characters that cannot be abused survive.</summary>
        private static string Safe(string value) =>
            new(value.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').Take(40).ToArray());
    }
}
