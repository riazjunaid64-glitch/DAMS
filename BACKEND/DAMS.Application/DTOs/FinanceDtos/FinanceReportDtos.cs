using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.FinanceDtos
{
    public sealed class ProfitAndLossDto
    {
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public string PeriodLabel { get; set; } = string.Empty;
        public string? ProjectName { get; set; }
        public List<PnlLineDto> IncomeLines { get; set; } = [];
        public decimal TotalIncome { get; set; }
        public List<PnlLineDto> ExpenseLines { get; set; } = [];
        public decimal TotalExpenses { get; set; }

        /// <summary>
        /// The result on the formal statement: <see cref="TotalIncome"/> − <see cref="TotalExpenses"/>,
        /// built only from entries the books actually hold. It is what the Balance Sheet carries as
        /// retained profit, which is why the two always agree and the statement balances.
        /// <para>
        /// It does NOT include <see cref="PendingFixedAssetCharge"/>. The client's rule deducts that,
        /// and the dashboard's Net Profit does deduct it; this statement cannot until the accountant
        /// names the account that carries the balancing credit.
        /// </para>
        /// </summary>
        public decimal NetProfit { get; set; }

        public decimal? PriorTotalIncome { get; set; }
        public decimal? PriorTotalExpenses { get; set; }
        public decimal? PriorNetProfit { get; set; }

        /// <summary>
        /// Fixed assets bought in the period — DISCLOSED, not deducted. Zero when there were none.
        /// <para>
        /// Not a statement line and not in any total above. It carries no debit and no credit, because
        /// no such entry exists: the books hold only Dr Fixed Asset / Cr Bank for a purchase. The
        /// client's rule says this amount should also reduce Net Profit, and the account that would
        /// take the other side of that reduction has not been decided — so the statement reports the
        /// figure as outstanding rather than applying half of an entry it cannot complete.
        /// </para>
        /// </summary>
        public decimal PendingFixedAssetCharge { get; set; }
    }

    public sealed class PnlLineDto
    {
        public int? CategoryId { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public decimal? PriorAmount { get; set; }
        public int TransactionCount { get; set; }
    }

    public sealed class TrialBalanceDto
    {
        public List<DateTime> ColumnDates { get; set; } = [];
        public List<TrialBalanceRowDto> Rows { get; set; } = [];
        public List<decimal> ColumnDebitTotals { get; set; } = [];
        public List<decimal> ColumnCreditTotals { get; set; } = [];
        public List<bool> ColumnBalanced { get; set; } = [];
    }

    public sealed class TrialBalanceRowDto
    {
        public int AccountId { get; set; }
        public string? LedgerCode { get; set; }
        public string AccountName { get; set; } = string.Empty;
        public FinanceAccountType Type { get; set; }
        public List<decimal> DebitBalances { get; set; } = [];
        public List<decimal> CreditBalances { get; set; } = [];
    }

    public sealed class BalanceSheetDto
    {
        public DateTime AsAt { get; set; }
        public List<BsGroupDto> AssetGroups { get; set; } = [];
        public decimal TotalAssets { get; set; }
        public List<BsGroupDto> LiabilityGroups { get; set; } = [];
        public decimal TotalLiabilities { get; set; }
        public List<BsLineDto> CapitalLines { get; set; } = [];

        /// <summary>
        /// The retained result carried in Capital, for the window from the opening baseline to
        /// <see cref="AsAt"/> — the same figure the P&amp;L reports as its Net Profit, built only from
        /// entries the books hold. Fixed-asset purchases are not deducted from it: their cost has no
        /// approved balancing account, so deducting it here would leave the sheet out by that amount
        /// on the strength of an entry nobody has authorised. The asset stays in Fixed Assets at cost
        /// and the pending deduction is disclosed on the P&amp;L instead.
        /// </summary>
        public decimal RetainedProfit { get; set; }

        public decimal TotalCapital { get; set; }
        public decimal TotalLiabilitiesAndCapital { get; set; }
        public bool IsBalanced { get; set; }
        public decimal Imbalance { get; set; }

        /// <summary>
        /// Why the statement is out, populated only when it is. Never a difference row: no balancing
        /// figure is invented to make the totals meet. Every cause listed here is a real bookkeeping
        /// fault to be corrected in the data — fixed-asset purchases are not among them, because their
        /// unapproved deduction is kept off this statement rather than plugged into it.
        /// </summary>
        public List<string> UnbalancedAccounts { get; set; } = [];
    }

    public sealed class BsGroupDto
    {
        public string Name { get; set; } = string.Empty;
        public List<BsLineDto> Lines { get; set; } = [];
        public decimal Total { get; set; }
    }

    public sealed class BsLineDto
    {
        public int AccountId { get; set; }
        public string? LedgerCode { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    public sealed class FinanceExportDto
    {
        public string FileName { get; set; } = string.Empty;
        public byte[] Content { get; set; } = [];
    }
}
