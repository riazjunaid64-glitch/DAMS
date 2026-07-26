namespace DAMS.Application.DTOs.CustomerDtos
{
    /// <summary>
    /// Outcome of matching an enquiry against the customer book: which customer it belongs
    /// to, and whether that customer had to be created.
    /// </summary>
    public sealed record CustomerResolution(int CustomerId, bool WasCreated);
}
