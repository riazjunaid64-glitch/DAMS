namespace DAMS.Domain.Entities
{
    /// <summary>
    /// A single admin-configurable setting. A flat key/value store rather than a wide table
    /// so a new provider or a new branding field never needs a migration.
    /// <see cref="IsSecret"/> values are only ever returned masked.
    /// </summary>
    public class NotificationSetting
    {
        public int Id { get; set; }

        public string Key { get; set; } = string.Empty;

        public string? Value { get; set; }

        public bool IsSecret { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public int? UpdatedByUserId { get; set; }
    }
}
