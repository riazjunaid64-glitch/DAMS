namespace DAMS.Domain.Entities
{
    public class OpeningBalanceSet
    {
        public int Id { get; set; }
        public DateTime AsAtDate { get; set; }
        public bool IsCommitted { get; set; }
        public DateTime? CommittedAt { get; set; }
        public int? CommittedByUserId { get; set; }
        public byte[] RowVersion { get; set; } = [];

        public ICollection<OpeningBalanceEntry> Entries { get; set; } = new List<OpeningBalanceEntry>();
        public ICollection<OpeningBalanceAuditEntry> AuditEntries { get; set; } = new List<OpeningBalanceAuditEntry>();
    }

    public class OpeningBalanceEntry
    {
        public int Id { get; set; }
        public int OpeningBalanceSetId { get; set; }
        public int FinanceAccountId { get; set; }
        public decimal DebitAmount { get; set; }
        public decimal CreditAmount { get; set; }
        public string? Note { get; set; }

        public OpeningBalanceSet OpeningBalanceSet { get; set; } = null!;
        public FinanceAccount FinanceAccount { get; set; } = null!;
    }

    public class OpeningBalanceAuditEntry
    {
        public int Id { get; set; }
        public int OpeningBalanceSetId { get; set; }
        public string Action { get; set; } = string.Empty;
        public int? UserId { get; set; }
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
        public string? Note { get; set; }

        public OpeningBalanceSet OpeningBalanceSet { get; set; } = null!;
    }
}
