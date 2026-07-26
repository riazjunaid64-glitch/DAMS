using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>
    /// Configurable origin of a lead (Walk-in, Facebook, Broker, ...). Rows are data, not
    /// code, so the business can add channels without a deployment. The seeded rows are
    /// marked <see cref="IsSystem"/> and cannot be deleted, only deactivated.
    /// </summary>
    public class LeadSource
    {
        public int Id { get; set; }

        // Stable machine key used by ingestion payloads (e.g. "facebook").
        public string Code { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public bool IsSystem { get; set; }

        public int DisplayOrder { get; set; }

        // Keeps source attribution intact when a lead converts into a Customer/Booking,
        // which use the older and coarser CustomerSource enum.
        public CustomerSource CustomerSource { get; set; } = CustomerSource.Other;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }
    }
}
