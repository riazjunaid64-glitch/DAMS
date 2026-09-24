namespace DAMS.Application.Common
{
    /// <summary>
    /// Forms send every field, so an optional value the user left empty arrives as "" rather than
    /// null. Validators such as [EmailAddress] accept null but reject "", which turns "not given"
    /// into "invalid". Request DTOs pass optional validated fields through this in their setter so
    /// validation sees what the user meant.
    /// </summary>
    public static class OptionalInput
    {
        public static string? BlankAsNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
