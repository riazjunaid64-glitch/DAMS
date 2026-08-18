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

        /// <summary>The accounting result. Ties to the Balance Sheet's retained profit for the same
        /// window, and to the Trial Balance.</summary>
        public decimal NetProfit { get; set; }

        /// <summary>
        /// Fixed assets bought in the period, at their gross cost — a MEMORANDUM line, not an
        /// expense line. It is not inside <see cref="TotalExpenses"/> and must never be added to
        /// <see cref="ExpenseLines"/>: doing so would put the Trial Balance out by this amount.
        /// </summary>
        public decimal CapitalisedPurchases { get; set; }

        /// <summary>
        /// <see cref="NetProfit"/> − <see cref="CapitalisedPurchases"/>. The client's management
        /// view of the period: asset purchases spend money, so they reduce it. Reported beside the
        /// accounting result rather than instead of it, because only the accounting result
        /// reconciles to the statements.
        /// </summary>
        public decimal ManagementNetProfit { get; set; }

        public decimal? PriorTotalIncome { get; set; }
        public decimal? PriorTotalExpenses { get; set; }
        public decimal? PriorNetProfit { get; set; }
        public decimal? PriorCapitalisedPurchases { get; set; }
        public decimal? PriorManagementNetProfit { get; set; }
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

        /// <summary>The accounting retained result carried in Capital. This — not
        /// <see cref="ManagementRetainedProfit"/> — is the figure <see cref="IsBalanced"/> is
        /// computed from.</summary>
        public decimal RetainedProfit { get; set; }

        public decimal TotalCapital { get; set; }
        public decimal TotalLiabilitiesAndCapital { get; set; }
        public bool IsBalanced { get; set; }
        public decimal Imbalance { get; set; }
        public List<string> UnbalancedAccounts { get; set; } = [];

        /// <summary>
        /// Gross cost of every fixed asset bought in the same window <see cref="RetainedProfit"/>
        /// covers. A MEMORANDUM figure: the assets it bought are already carried in the Fixed Assets
        /// group above, so this is emphatically not a second deduction from the sheet.
        /// </summary>
        public decimal CapitalisedPurchases { get; set; }

        /// <summary>
        /// <see cref="RetainedProfit"/> − <see cref="CapitalisedPurchases"/>: what the client's
        /// management reporting treats as the result. Shown as a note beneath the statement, never
        /// substituted into Capital — substituting it would break <see cref="IsBalanced"/> by exactly
        /// <see cref="CapitalisedPurchases"/>, which is the trap this pair of fields exists to avoid.
        /// </summary>
        public decimal ManagementRetainedProfit { get; set; }
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
