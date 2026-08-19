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
        /// The result: <see cref="TotalIncome"/> − <see cref="TotalExpenses"/>. The one profit figure
        /// in the system. Fixed-asset purchases are inside <see cref="ExpenseLines"/> as an ordinary
        /// cost of the period, so nothing has to be added to or taken off this to arrive at the
        /// figure the dashboard and the Balance Sheet's retained profit show.
        /// </summary>
        public decimal NetProfit { get; set; }

        public decimal? PriorTotalIncome { get; set; }
        public decimal? PriorTotalExpenses { get; set; }
        public decimal? PriorNetProfit { get; set; }
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
        /// <see cref="AsAt"/>. Net of the fixed assets bought in that window, exactly like the P&amp;L
        /// figure it comes from. The matching amount is held back inside
        /// <see cref="CapitalLines"/> as "Fixed assets charged to profit", which is what keeps
        /// <see cref="IsBalanced"/> true while the assets stay on the sheet at cost.
        /// </summary>
        public decimal RetainedProfit { get; set; }

        public decimal TotalCapital { get; set; }
        public decimal TotalLiabilitiesAndCapital { get; set; }
        public bool IsBalanced { get; set; }
        public decimal Imbalance { get; set; }
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
