using DAMS.Domain.Enums;

namespace DAMS.Application.Common;

/// <summary>
/// A create/update for a customer was refused because the mobile or CNIC already belongs to
/// another customer. Surfaced as 409 so the screen can show "Already used by … Open customer".
/// </summary>
public sealed class CustomerConflictException : InvalidOperationException
{
    public CustomerConflictException(
        string field,
        int existingCustomerId,
        string existingCustomerName,
        string message)
        : base(message)
    {
        Field = field;
        ExistingCustomerId = existingCustomerId;
        ExistingCustomerName = existingCustomerName;
    }

    /// <summary>"phone" or "cnic" — which field the screen should underline.</summary>
    public string Field { get; }

    public int ExistingCustomerId { get; }

    public string ExistingCustomerName { get; }

    public static CustomerConflictException Phone(int id, string name) =>
        new("phone", id, name, $"Already used by {name}.");

    public static CustomerConflictException Cnic(int id, string name) =>
        new("cnic", id, name, $"Already used by {name}.");
}

/// <summary>Digits-only form of a CNIC / NICOP so "37405-1234567-1" and "3740512345671" match.</summary>
public static class CustomerIdentityNormalizer
{
    public static string DigitsOnly(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : new string(value.Where(char.IsDigit).ToArray());

    public static string? DigitsOnlyOrNull(string? value)
    {
        var digits = DigitsOnly(value);
        return digits.Length == 0 ? null : digits;
    }
}

/// <summary>
/// "Documents needed" until KAN-82: required docs whose status is still Missing, Requested,
/// Rejected, ReplacementRequired, Expired or Postponed.
/// </summary>
public static class CustomerDocumentsNeeded
{
    public static bool IsNeeded(CustomerDocumentStatus status) =>
        status is CustomerDocumentStatus.Missing
            or CustomerDocumentStatus.Requested
            or CustomerDocumentStatus.Rejected
            or CustomerDocumentStatus.ReplacementRequired
            or CustomerDocumentStatus.Expired
            or CustomerDocumentStatus.Postponed;

    public static int Count(IEnumerable<Domain.Entities.CustomerDocumentRequirement> requirements) =>
        requirements.Count(r => r.IsRequired && IsNeeded(r.Status));

    public static bool Any(IEnumerable<Domain.Entities.CustomerDocumentRequirement> requirements) =>
        requirements.Any(r => r.IsRequired && IsNeeded(r.Status));
}
