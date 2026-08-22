namespace DAMS.Domain.Enums
{
    /// <summary>
    /// Lifecycle of a connected provider account. Numbering starts at 1 so an unset value
    /// can never be mistaken for a live, usable connection.
    /// </summary>
    public enum ExternalIntegrationConnectionStatus
    {
        Connected = 1,
        NeedsReauthorization = 2,
        Disconnected = 3,
        Error = 4
    }

    /// <summary>
    /// Lifecycle of one durable webhook event in the inbox.
    /// </summary>
    public enum ExternalIntegrationEventStatus
    {
        Pending = 0,
        Processing = 1,
        Processed = 2,
        Retry = 3,
        Failed = 4,
        /// <summary>Deliberately not turned into a lead — a disabled or unrecognised resource.</summary>
        Ignored = 5
    }
}
