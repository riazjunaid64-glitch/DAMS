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
        public static string When(DateTime utc) =>
            PakistanTime.ToLocal(utc).ToString("ddd d MMM, h:mm tt", CultureInfo.GetCultureInfo("en-US"));

        /// <summary>Example: Sep 2 — the Pakistan date a UTC instant falls on.</summary>
        public static string Day(DateTime utc) =>
            PakistanTime.ToLocal(utc).ToString("MMM d", CultureInfo.GetCultureInfo("en-US"));
    }
}
