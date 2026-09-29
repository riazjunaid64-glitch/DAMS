using DAMS.Application.Common;
using DAMS.Domain.Common;

namespace DAMS.Application.Common
{
    /// <summary>
    /// Turns however a phone number or email was typed into the single canonical form used
    /// for duplicate detection. "0300-1234567", "0300 1234567" and "+92 300 1234567" must
    /// all resolve to the same person.
    /// </summary>
    public static class LeadContactNormalizer
    {
        /// <summary>Below this many digits, a number could never be a real, dialable
        /// subscriber — too short to mean anything, too short to safely match against.</summary>
        public const int MinUsablePhoneDigits = ContactNormalization.MinUsablePhoneDigits;

        public static string NormalizePhone(string? phone) => ContactNormalization.NormalizePhone(phone);

        public static string? NormalizePhoneOrNull(string? phone) => ContactNormalization.NormalizePhoneOrNull(phone);

        public static string? NormalizeUsablePhoneOrNull(string? phone) =>
            ContactNormalization.NormalizeUsablePhoneOrNull(phone);

        public static string? NormalizeEmail(string? email) =>
            string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

        public static string? Clean(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        /// <summary>Clamps text to a column's length so a long note can never fail a save.</summary>
        public static string Limit(string value, int max) =>
            value.Length <= max ? value : value[..max];

        public static string? LimitOrNull(string? value, int max) =>
            value is null || value.Length <= max ? value : value[..max];
    }
}
