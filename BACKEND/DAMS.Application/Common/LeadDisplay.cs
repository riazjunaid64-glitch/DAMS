using System.Globalization;
using System.Text.RegularExpressions;
using DAMS.Domain.Enums;

namespace DAMS.Application.Common
{
    /// <summary>
    /// Words and Pakistan-time dates for notifications and timeline text.
    /// Stored values stay as they are; only the text a person reads changes.
    /// </summary>
    public static class LeadDisplay
    {
        private static readonly TimeZoneInfo Pakistan = ResolvePakistan();

        /// <summary>The four statuses the redesigned screens show.</summary>
        public static string Status(LeadStage stage) => stage switch
        {
            LeadStage.Won => "Won",
            LeadStage.Lost => "Lost",
            LeadStage.Dormant => "Dormant",
            _ => "In progress"
        };

        /// <summary>A readable label for an enum, such as SiteVisitScheduled → "Site visit scheduled".</summary>
        public static string Words(Enum value)
        {
            var name = value.ToString();
            if (string.Equals(name, nameof(LeadStage.FirstContactPending), StringComparison.Ordinal))
                return "New";
            if (string.Equals(name, "WhatsApp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, nameof(LeadCommunicationChannel.Whatsapp), StringComparison.Ordinal))
                return "WhatsApp";

            var spaced = Regex.Replace(name, "([a-z])([A-Z])", "$1 $2");
            if (spaced.Length == 0)
                return spaced;
            return char.ToUpperInvariant(spaced[0]) + spaced[1..].ToLowerInvariant();
        }

        /// <summary>Example: Sat 27 Sep, 4:00 PM. The instant is stored in UTC.</summary>
        public static string When(DateTime utc)
        {
            var instant = utc.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(utc, DateTimeKind.Utc)
                : utc.ToUniversalTime();
            var local = TimeZoneInfo.ConvertTimeFromUtc(instant, Pakistan);
            return local.ToString("ddd d MMM, h:mm tt", CultureInfo.GetCultureInfo("en-US"));
        }

        private static TimeZoneInfo ResolvePakistan()
        {
            foreach (var id in new[] { "Asia/Karachi", "Pakistan Standard Time" })
            {
                if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
                    return zone;
            }

            return TimeZoneInfo.Utc;
        }
    }
}
