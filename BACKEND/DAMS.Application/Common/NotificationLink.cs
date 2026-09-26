using DAMS.Domain.Enums;

namespace DAMS.Application.Common
{
    /// <summary>
    /// Deep links. A notification destination is always a site-relative path, so a stored
    /// notification, a template or an admin broadcast can never be turned into an open
    /// redirect to somebody else's host. The absolute form is only ever produced at send
    /// time, from the configured public base URL.
    /// </summary>
    public static class NotificationLink
    {
        /// <summary>
        /// Accepts a path only. Returns null for anything that could leave the site:
        /// absolute URLs, scheme-relative "//host", backslash tricks, control characters.
        /// </summary>
        public static string? Sanitize(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            var value = path.Trim();

            // Strip anything that could smuggle a second line or a control byte into a header.
            if (value.Any(c => char.IsControl(c)))
                return null;

            // "\\evil.com" and "/\evil.com" are treated as network paths by browsers.
            if (value.Contains('\\'))
                return null;

            if (!value.StartsWith('/') || value.StartsWith("//", StringComparison.Ordinal))
                return null;

            // A colon before the first slash after the leading one would make "/x:y" ambiguous
            // in some parsers; rejecting schemes outright is simpler and loses nothing.
            if (value.Contains("://", StringComparison.Ordinal))
                return null;

            return LeadContactNormalizer.Limit(value, 500);
        }

        /// <summary>The canonical in-app destination for a related record.</summary>
        public static string? ForEntity(NotificationEntityType type, int? id, int? secondaryId = null) => type switch
        {
            NotificationEntityType.Lead when id > 0 => $"/crm/leads/{id}",
            NotificationEntityType.LeadIntakeHold when id > 0 => "/crm",
            NotificationEntityType.IntegrationConnection when id > 0 => "/crm/settings?tab=integrations",
            NotificationEntityType.LeadFollowUp when secondaryId > 0 => $"/crm/leads/{secondaryId}?followUp={id}",
            NotificationEntityType.LeadSiteVisit when secondaryId > 0 => $"/crm/leads/{secondaryId}?visit={id}",
            NotificationEntityType.LeadComment when secondaryId > 0 => $"/crm/leads/{secondaryId}?comment={id}",
            NotificationEntityType.Customer when id > 0 => $"/customers/{id}",
            NotificationEntityType.BookingRequest when id > 0 => $"/bookings?request={id}",
            NotificationEntityType.Booking when id > 0 => $"/confirmed-bookings/{id}",
            NotificationEntityType.Payment when id > 0 && secondaryId > 0 => $"/receipt/{secondaryId}/{id}",
            NotificationEntityType.Installment when secondaryId > 0 => $"/confirmed-bookings/{secondaryId}?installment={id}",
            NotificationEntityType.Project when id > 0 => $"/projects/{id}",
            NotificationEntityType.Unit when id > 0 => $"/units/{id}",
            NotificationEntityType.EmployeeTask when secondaryId > 0 => $"/employees/{secondaryId}?task={id}",
            _ => "/notifications"
        };

        /// <summary>
        /// The customer-facing route for a booking a customer owns. Staff routes are not
        /// visible to clients, so their links must not point there.
        /// </summary>
        public static string ForCustomerBooking(int bookingId) => $"/my-projects/{bookingId}";

        /// <summary>
        /// The customer's project list, not a specific booking. Customer booking routes
        /// deliberately exclude cancelled bookings, so a cancellation notification must not
        /// deep-link to a record the customer can no longer open.
        /// </summary>
        public static string ForCustomerBookingsList() => "/my-projects";

        public static string ForCustomerReceipt(int bookingId, int paymentId) => $"/receipt/{bookingId}/{paymentId}";

        /// <summary>
        /// Joins a validated relative path onto the configured public origin. Returns the
        /// relative path unchanged when no origin is configured — a mail client shows it as
        /// plain text rather than DAMS inventing a host.
        /// </summary>
        public static string Absolute(string? baseUrl, string? relative)
        {
            var path = Sanitize(relative) ?? "/notifications";
            if (string.IsNullOrWhiteSpace(baseUrl))
                return path;

            if (!Uri.TryCreate(baseUrl.Trim().TrimEnd('/'), UriKind.Absolute, out var origin)
                || (origin.Scheme != Uri.UriSchemeHttp && origin.Scheme != Uri.UriSchemeHttps))
                return path;

            return $"{origin.GetLeftPart(UriPartial.Authority)}{path}";
        }
    }
}
