using System.Text.RegularExpressions;

namespace DAMS.Application.Services.Integrations
{
    /// <summary>
    /// Removes anything credential-shaped from text on its way to a log, a database column or
    /// an API response.
    ///
    /// Graph error messages quote the request that failed, and that request carries the access
    /// token in its query string. Without this, a single failed call would write a live token
    /// into ExternalIntegrationEvents.LastError and the application log.
    /// </summary>
    public static partial class MetaCredentialScrubber
    {
        private const string Replacement = "$1=***";

        public static string? Scrub(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            return SensitiveQueryValue().Replace(value, Replacement);
        }

        /// <summary>Scrubs, then clamps to a column length.</summary>
        public static string? ScrubAndLimit(string? value, int maxLength)
        {
            var scrubbed = Scrub(value);
            return scrubbed is null || scrubbed.Length <= maxLength ? scrubbed : scrubbed[..maxLength];
        }

        [GeneratedRegex(
            @"\b(access_token|appsecret_proof|client_secret|code|fb_exchange_token)\b=[^&\s""']+",
            RegexOptions.IgnoreCase)]
        private static partial Regex SensitiveQueryValue();
    }
}
