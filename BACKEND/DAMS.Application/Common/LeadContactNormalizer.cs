namespace DAMS.Application.Common
{
    /// <summary>
    /// Turns however a phone number or email was typed into the single canonical form used
    /// for duplicate detection. "0300-1234567", "0300 1234567" and "+92 300 1234567" must
    /// all resolve to the same person.
    /// </summary>
    public static class LeadContactNormalizer
    {
        /// <summary>Local trunk prefix and country code for Pakistani numbers.</summary>
        private const string CountryCode = "92";

        /// <summary>Below this many digits, a number could never be a real, dialable
        /// subscriber — too short to mean anything, too short to safely match against.</summary>
        public const int MinUsablePhoneDigits = 7;

        public static string NormalizePhone(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
                return string.Empty;

            var digits = new string(phone.Where(char.IsDigit).ToArray());
            if (digits.Length == 0)
                return string.Empty;

            // 0300xxxxxxx and 92300xxxxxxx are the same subscriber; compare on the national
            // number so a lead entered either way is recognised as a duplicate.
            if (digits.StartsWith(CountryCode, StringComparison.Ordinal) && digits.Length > CountryCode.Length)
                digits = digits[CountryCode.Length..];
            digits = digits.TrimStart('0');

            return digits;
        }

        public static string? NormalizePhoneOrNull(string? phone)
        {
            var normalized = NormalizePhone(phone);
            return normalized.Length == 0 ? null : normalized;
        }

        /// <summary>Same as <see cref="NormalizePhoneOrNull"/>, but also null when the result
        /// is too short to be a usable phone or WhatsApp number — so both channels are held to
        /// the same "would this ever match anything" bar rather than only phone getting it.</summary>
        public static string? NormalizeUsablePhoneOrNull(string? phone)
        {
            var normalized = NormalizePhoneOrNull(phone);
            return normalized is { Length: < MinUsablePhoneDigits } ? null : normalized;
        }

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
