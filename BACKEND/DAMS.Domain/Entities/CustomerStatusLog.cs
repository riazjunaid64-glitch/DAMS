using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>
    /// One block or unblock of a customer: who, when and why. Written in the same transaction as the
    /// status change. It is an audit trail only — nothing reads it back into a customer, and rows are
    /// never edited or deleted.
    /// </summary>
    public class CustomerStatusLog
    {
        public int Id { get; set; }

        public int CustomerId { get; set; }

        public CustomerStatusAction Action { get; set; }

        /// <summary>The reason given when blocking; null on an unblock.</summary>
        public string? Reason { get; set; }

        public int? ByUserId { get; set; }

        /// <summary>The audit instant, in UTC.</summary>
        public DateTime At { get; set; } = DateTime.UtcNow;

        public Customer Customer { get; set; } = null!;
    }
}
