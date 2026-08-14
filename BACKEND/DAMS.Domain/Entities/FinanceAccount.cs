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
        public string? LedgerCode { get; set; }
        public int DisplayOrder { get; set; }
        public FinanceSystemAccountRole SystemRole { get; set; } = FinanceSystemAccountRole.None;
        public string? BankOrWalletName { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
        public ICollection<ManualRevenue> ManualRevenues { get; set; } = new List<ManualRevenue>();
        public ICollection<Expense> Expenses { get; set; } = new List<Expense>();

        /// <summary>Asset purchases this account PAID FOR — an outflow, net of tax withheld.</summary>
        public ICollection<AssetPurchase> AssetPurchasesPaid { get; set; } = new List<AssetPurchase>();

        /// <summary>Asset purchases capitalised INTO this account — an inflow, at the gross price.
        /// Only ever populated on a fixed-asset account.</summary>
        public ICollection<AssetPurchase> AssetPurchasesReceived { get; set; } = new List<AssetPurchase>();
        public ICollection<CommissionPayout> CommissionPayouts { get; set; } = new List<CommissionPayout>();
        public ICollection<RebateDisbursement> RebateDisbursements { get; set; } = new List<RebateDisbursement>();
        public ICollection<WhtDeposit> WhtDeposits { get; set; } = new List<WhtDeposit>();
        public ICollection<OpeningBalanceEntry> OpeningBalanceEntries { get; set; } = new List<OpeningBalanceEntry>();
        public ICollection<CapitalPartner> CapitalPartners { get; set; } = new List<CapitalPartner>();
        public ICollection<CapitalTransaction> CapitalCashTransactions { get; set; } = new List<CapitalTransaction>();
        public ICollection<Loan> Loans { get; set; } = new List<Loan>();
        public ICollection<LoanTransaction> LoanCashTransactions { get; set; } = new List<LoanTransaction>();
    }
}
