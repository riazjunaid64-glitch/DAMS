namespace DAMS.Domain.Enums
{
    /// <summary>
    /// Classifies an installment obligation. Regular schedule rows use <see cref="Regular"/>.
    /// Possession charges are a separate obligation, not part of the regular schedule math.
    /// Additional types (e.g. Custom, Penalty) can be added in future phases.
    /// </summary>
    public enum InstallmentType
    {
        Regular = 0,
        Possession = 1
    }
}
