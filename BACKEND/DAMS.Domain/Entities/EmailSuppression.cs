namespace DAMS.Domain.Entities
{
    /// <summary>
    /// An address DAMS has stopped emailing. A hard bounce lands here and blocks
    /// non-essential mail; essential transactional messages (receipts, security) still go
    /// out and simply record the failure, because withholding them is the bigger harm.
    /// </summary>
    public class EmailSuppression
    {
        public int Id { get; set; }

        /// <summary>Lower-cased address.</summary>
        public string Email { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ClearedAt { get; set; }

        public int? ClearedByUserId { get; set; }
    }
}
