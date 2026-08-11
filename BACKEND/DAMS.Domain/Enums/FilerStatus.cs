namespace DAMS.Domain.Enums
{
    /// <summary>
    /// Whether a vendor appears on the FBR Active Taxpayer List. Non-filers are withheld at
    /// (typically) double the filer rate, so <see cref="Unknown"/> deliberately falls back to
    /// the non-filer rate: under-deducting is the penalised direction, while over-deducting is
    /// recoverable by the vendor when they file.
    /// </summary>
    public enum FilerStatus
    {
        Unknown = 0,
        Filer = 1,
        NonFiler = 2
    }
}
