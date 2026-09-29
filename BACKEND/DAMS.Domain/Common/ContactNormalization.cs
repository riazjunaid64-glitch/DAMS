namespace DAMS.Domain.Common;

/// <summary>
/// Canonical contact forms used for duplicate detection across leads and customers.
/// </summary>
public static class ContactNormalization
{
    private const string CountryCode = "92";

    public const int MinUsablePhoneDigits = 7;

    public static string NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return string.Empty;

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
            return string.Empty;

        if (digits.StartsWith("00", StringComparison.Ordinal))
            digits = digits[2..];

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

    public static string? NormalizeUsablePhoneOrNull(string? phone)
    {
        var normalized = NormalizePhoneOrNull(phone);
        return normalized is { Length: < MinUsablePhoneDigits } ? null : normalized;
    }
}
