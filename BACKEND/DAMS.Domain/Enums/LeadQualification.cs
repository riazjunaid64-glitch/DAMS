namespace DAMS.Domain.Enums
{
    /// <summary>Lead temperature. Independent of the pipeline stage.</summary>
    public enum LeadQualification
    {
        Unqualified = 0,
        Cold = 1,
        Warm = 2,
        Hot = 3
    }
}
