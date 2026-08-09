namespace DAMS.Domain.Entities;

/// <summary>Immutable, hash-addressed snapshot of every payout-driving commission-rule revision.</summary>
public sealed class CommissionRuleRevision
{
    public int Id { get; set; }
    public int RuleId { get; set; }
    public int RevisionNumber { get; set; }
    public string SnapshotJson { get; set; } = string.Empty;
    public string? PreviousSnapshotJson { get; set; }
    public string SnapshotHash { get; set; } = string.Empty;
    public string ChangeReason { get; set; } = string.Empty;
    public int? ChangedByUserId { get; set; }
    public string? ChangedByName { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    public CommissionRule Rule { get; set; } = null!;
    public ICollection<BookingCommission> Commissions { get; set; } = new List<BookingCommission>();
    public ICollection<FinancialWorkflowAuditEntry> AuditEntries { get; set; } = new List<FinancialWorkflowAuditEntry>();
}
