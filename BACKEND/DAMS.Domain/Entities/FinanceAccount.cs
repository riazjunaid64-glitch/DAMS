using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    public class FinanceAccount
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public FinanceAccountType Type { get; set; }
        public string AccountHolderName { get; set; } = string.Empty;
        public decimal OpeningBalance { get; set; }
        public string? BankOrWalletName { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
        public ICollection<ManualRevenue> ManualRevenues { get; set; } = new List<ManualRevenue>();
        public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
        public ICollection<CommissionPayout> CommissionPayouts { get; set; } = new List<CommissionPayout>();
        public ICollection<RebateDisbursement> RebateDisbursements { get; set; } = new List<RebateDisbursement>();
    }
}
