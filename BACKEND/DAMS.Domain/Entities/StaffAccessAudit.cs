namespace DAMS.Domain.Entities
{
    /// <summary>
    /// Who turned a staff login on or off, and when. Rows are only inserted.
    /// The user ids are plain integers so the record survives the account it describes.
    /// </summary>
    public class StaffAccessAudit
    {
        public int Id { get; set; }

        public int EmployeeId { get; set; }

        public int UserId { get; set; }

        public int PerformedByUserId { get; set; }

        /// <summary>True when access was turned on, false when it was turned off.</summary>
        public bool AccessEnabled { get; set; }

        public DateTime OccurredAt { get; set; }
    }
}
