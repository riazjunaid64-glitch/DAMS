using System.Security.Cryptography;
using System.Text;

namespace DAMS.Application.Services.Integrations
{
    /// <summary>
    /// Verifies that a webhook body really came from Meta.
    ///
    /// This is the only thing standing between a public endpoint and the lead pipeline, so it
    /// works on the exact bytes received — re-serializing parsed JSON would change them and
    /// break the signature — and compares in fixed time so the comparison itself leaks nothing.
    /// </summary>
    public static class MetaSignature
    {
        private const string Prefix = "sha256=";

        public static bool IsValid(ReadOnlySpan<byte> rawBody, string? headerValue, string? appSecret)
        {
            if (string.IsNullOrWhiteSpace(headerValue) || string.IsNullOrWhiteSpace(appSecret))
                return false;

            if (!headerValue.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            var supplied = headerValue[Prefix.Length..].Trim();
            if (supplied.Length == 0)
                return false;

            byte[] suppliedBytes;
            try
            {
                suppliedBytes = Convert.FromHexString(supplied);
            }
            catch (FormatException)
            {
                return false;
            }

            var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), rawBody);

            return CryptographicOperations.FixedTimeEquals(expected, suppliedBytes);
        }

        /// <summary>Constant-time comparison for the webhook verification token.</summary>
        public static bool TokensMatch(string? configured, string? supplied)
        {
            if (string.IsNullOrWhiteSpace(configured) || string.IsNullOrWhiteSpace(supplied))
                return false;

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(configured),
                Encoding.UTF8.GetBytes(supplied));
        }
    }
}
