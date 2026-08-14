using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>
    /// A balance-sheet-only movement between a physical company account and one person's staff
    /// float. It never touches profit; expenses paid from the float are recorded separately.
    /// </summary>
    public sealed class StaffCashTransfer
    {
        public int Id { get; set; }
        public int StaffFinanceAccountId { get; set; }
        public int CounterpartyFinanceAccountId { get; set; }
        public StaffCashMovementType Type { get; set; }
        public decimal Amount { get; set; }
        public DateTime Date { get; set; }
        public string? Reference { get; set; }
        public string? Note { get; set; }
        public int? CreatedByUserId { get; set; }
        public int? UpdatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public byte[] RowVersion { get; set; } = [];

        public FinanceAccount StaffFinanceAccount { get; set; } = null!;
        public FinanceAccount CounterpartyFinanceAccount { get; set; } = null!;
    }
}
