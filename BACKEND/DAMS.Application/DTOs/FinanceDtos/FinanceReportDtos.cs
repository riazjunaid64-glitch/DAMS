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
        /// <see cref="TotalIncome"/> − <see cref="TotalExpenses"/>, and the ONLY Net Profit DAMS
        /// reports: the dashboard card, this statement, its export and the Net Profit drill-down all
        /// carry this same figure for the same period. Fixed-asset purchases are inside it, as a
        /// "Fixed Asset Purchases" line in <see cref="ExpenseLines"/> — the client's rule is that
        /// buying an asset spends the money.
        /// <para>
        /// It is therefore LOWER than <see cref="BalanceSheetDto.RetainedProfit"/> by exactly
        /// <see cref="BalanceSheetDto.UnpostedFixedAssetCharge"/>, which that statement discloses.
        /// The Balance Sheet has to reconcile against an asset still carried at full cost, so it
        /// stays ledger-only until the accountant names the account taking the balancing credit.
        /// </para>
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
        /// <see cref="AsAt"/>, built only from entries the books hold — which is what lets this sheet
        /// balance on real double entry. Fixed-asset purchases are not deducted from it: the asset
        /// above is carried at full cost and no account has been approved to take the balancing
        /// credit, so deducting it here would put the sheet out by exactly that amount.
        /// <para>
        /// It is consequently HIGHER than the P&amp;L's Net Profit for the same window by
        /// <see cref="UnpostedFixedAssetCharge"/>. That difference is the open accounting decision,
        /// not a second profit measure, and it closes the moment the accountant names the account.
        /// </para>
        /// </summary>
        public decimal RetainedProfit { get; set; }

        /// <summary>
        /// Fixed assets bought between <see cref="RetainedProfitStart"/> and <see cref="AsAt"/>, at
        /// gross cost: the single, fully explained difference between <see cref="RetainedProfit"/>
        /// here and Net Profit on the P&amp;L. Zero when there were no purchases, and never part of
        /// any total on this statement.
        /// </summary>
        public decimal UnpostedFixedAssetCharge { get; set; }

        /// <summary>The first day <see cref="RetainedProfit"/> accumulates from — the committed
        /// opening baseline, or null when no baseline has been committed and it runs from the
        /// beginning of the records.</summary>
        public DateTime? RetainedProfitStart { get; set; }

        public decimal TotalCapital { get; set; }
        public decimal TotalLiabilitiesAndCapital { get; set; }
        public bool IsBalanced { get; set; }
        public decimal Imbalance { get; set; }

        /// <summary>
        /// Why the statement is out, populated only when it is. Never a difference row: no balancing
        /// figure is invented to make the totals meet. Every cause listed here is a real bookkeeping
        /// fault to be corrected in the data — fixed-asset purchases are not among them, because
        /// their unapproved deduction is kept off this statement and disclosed in
        /// <see cref="UnpostedFixedAssetCharge"/> rather than plugged into it.
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
