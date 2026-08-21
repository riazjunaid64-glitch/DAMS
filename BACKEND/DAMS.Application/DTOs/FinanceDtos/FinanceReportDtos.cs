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
        /// This figure does NOT currently roll into equity on the Balance Sheet or the Trial
        /// Balance. Those are double-entry positions and the only entry the books hold for a
        /// purchase is Dr Fixed Asset / Cr Bank: the asset is still carried at full cost and no
        /// account has been approved to take the balancing credit, so charging the cost there as
        /// well would put them out by exactly that amount. Each of those statements therefore
        /// carries its own ledger-only retained figure and DISCLOSES the purchases it could not
        /// absorb (<see cref="BalanceSheetDto.UnpostedFixedAssetCharge"/>, for that statement's own
        /// window). How the ledger should carry the balancing side while the asset stays at cost is
        /// an OPEN accountant/client decision — DAMS does not claim the two statements are formally
        /// reconciled, and nothing here invents an account to make them appear so.
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
        /// It is NOT a second Net Profit and must not be compared with one. It accumulates over THIS
        /// statement's own window — <see cref="RetainedProfitStart"/> to <see cref="AsAt"/> — which
        /// is generally not the window any P&amp;L was run for, so subtracting one from the other is
        /// only meaningful when the two windows happen to coincide. What this statement states about
        /// the difference is confined to its own window: see
        /// <see cref="UnpostedFixedAssetCharge"/>.
        /// </para>
        /// </summary>
        public decimal RetainedProfit { get; set; }

        /// <summary>
        /// Fixed assets bought inside THIS statement's own window (<see cref="RetainedProfitStart"/>
        /// to <see cref="AsAt"/>), at gross cost: spending that has been charged to Net Profit but
        /// that this ledger position could not absorb, because the asset above is still carried at
        /// full cost and no balancing account has been approved. Zero when there were no purchases,
        /// and never part of any total on this statement.
        /// <para>
        /// Scoped to this window on purpose, and deliberately NOT described as the gap against a
        /// P&amp;L: a P&amp;L run for some other period has no arithmetic relationship to
        /// <see cref="RetainedProfit"/> at all. How the ledger should carry the balancing side is an
        /// open accountant/client decision; until it is made, this is a disclosure, not a
        /// reconciliation.
        /// </para>
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
