namespace DAMS.Domain.Entities
{
    /// <summary>
    /// What a correctable financial record looked like before somebody changed it, and who changed it.
    /// <para>
    /// Expenses, revenue entries, asset purchases and FBR deposits are editable and deletable on
    /// purpose — real corrections happen, and the alternative (a reversal workflow for every typo)
    /// is not what this client works with. But editing an expense dated last month moves last
    /// month's profit, last month's bank balance and possibly the tax withheld from a supplier, and a
    /// <c>RowVersion</c> does nothing about that: it stops two admins overwriting each other, not one
    /// admin quietly restating history. Without a trail, an expense of 5,000,000 becoming 500,000 is
    /// indistinguishable from an expense that was always 500,000.
    /// </para>
    /// <para>
    /// Written by <c>AppDbContext</c> in the same SaveChanges as the change itself, so the record and
    /// its evidence commit or roll back together and no service can forget to write one. Append-only:
    /// the context refuses to modify or delete these rows.
    /// </para>
    /// </summary>
    public class FinanceRecordAudit
    {
        public int Id { get; set; }

        /// <summary>Entity name — <c>Expense</c>, <c>ManualRevenue</c>, <c>AssetPurchase</c>,
        /// <c>WhtDeposit</c>. Not a foreign key: the row it describes may no longer exist, which is
        /// exactly the case the trail is for.</summary>
        public string RecordType { get; set; } = string.Empty;

        public int RecordId { get; set; }

        /// <summary><c>Updated</c> or <c>Deleted</c>. Creations are not recorded here — the record
        /// itself already carries who created it and when.</summary>
        public string Action { get; set; } = string.Empty;

        /// <summary>JSON. For an update, the fields that moved with their before and after values;
        /// for a delete, the whole row as it last stood.</summary>
        public string Changes { get; set; } = string.Empty;

        public int? ActorUserId { get; set; }

        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    }
}
