namespace DAMS.Application.DTOs.FinanceDtos
{
    /// <summary>The 5 dashboard summary cards for the selected project + date range.</summary>
    public class FinancialSummaryDto
    {
        public decimal TotalRevenue { get; set; }

        /// <summary>
        /// Revenue DAMS recognises by itself, as opposed to manually entered revenue: unit sales
        /// recognised at possession, plus amounts retained when a booking is cancelled.
        /// <para>
        /// It is NOT customer receipts. Money taken before possession is a deposit the company owes
        /// back — see <see cref="CustomerDepositsBalance"/> — and never touches profit. The property
        /// name is unchanged so existing clients keep working; what it counts is not.
        /// </para>
        /// </summary>
        public decimal AutomaticRevenue { get; set; }

        /// <summary>
        /// Customer money held but not yet earned, as at the END of the selected range — a
        /// balance, not a period total. Deliberately outside <see cref="TotalRevenue"/> and
        /// <see cref="NetProfit"/>: it is a liability.
        /// </summary>
        public decimal CustomerDepositsBalance { get; set; }

        public decimal ManualRevenue { get; set; }

        /// <summary>
        /// Every cost of the period, gross of any tax withheld — including the full purchase price
        /// of the fixed assets bought in it (<see cref="TotalAssetPurchases"/>). The client's
        /// confirmed rule is that buying an asset spends money, so the period bears that spending
        /// like any other cost; there is no separate capital total held outside this one.
        /// </summary>
        public decimal TotalExpenses { get; set; }

        /// <summary>
        /// The result: <see cref="TotalRevenue"/> − <see cref="TotalExpenses"/>, the figure the Net
        /// Profit drill-down adds up to, and the SAME figure <see cref="ProfitAndLossDto.NetProfit"/>
        /// reports for the same period. One Net Profit rule, applied everywhere: buying an asset
        /// spends the money, so the period bears it.
        /// <para>
        /// NULL, not zero, whenever an account filter is applied — see
        /// <see cref="AccountFilterApplied"/>. Profitability is a property of the business over a
        /// period, not of a bank account: a recognised sale moves no cash and therefore belongs to
        /// no account, so an account-filtered subtraction drops every possession-recognised sale
        /// from the revenue side while keeping every cost paid out of that account. The figure that
        /// came out was still labelled Net Profit, and could show a loss for the account that had
        /// funded a profitable month. Under an account filter the screen reports
        /// <see cref="AccountNetMovement"/> instead, which is what an account can actually answer.
        /// </para>
        /// </summary>
        public decimal? NetProfit { get; set; }

        /// <summary>
        /// Whether a single account (or "unassigned") was selected. When true,
        /// <see cref="TotalRevenue"/> and <see cref="TotalExpenses"/> are what was RECORDED AGAINST
        /// THAT ACCOUNT rather than the whole business — they still reconcile with their own
        /// drill-downs, but they are not the period's revenue and cost — <see cref="NetProfit"/> is
        /// not reported at all, and the deposit/outstanding/overdue balances are suppressed because
        /// they belong to bookings rather than to accounts.
        /// </summary>
        public bool AccountFilterApplied { get; set; }

        public decimal OutstandingAmount { get; set; }
        public decimal OverdueAmount { get; set; }

        /// <summary>Tax withheld from expenses and from fixed-asset purchases in the period. Money
        /// that is inside <see cref="TotalExpenses"/> but has not left the bank — it is owed to FBR,
        /// and it is the same figure the WHT payable account moves by.</summary>
        public decimal WhtWithheld { get; set; }

        /// <summary>Fixed assets bought in the period, at cost (gross of any tax withheld from the
        /// supplier). Purchases into a work-in-progress account — only ever rows inherited from the
        /// previous ERP — are outside this, exactly as they are outside the charge to profit.
        /// A BREAKDOWN of <see cref="TotalExpenses"/>, not an addition to it: the cost is already
        /// inside that total, inside <see cref="NetProfit"/> and inside the P&amp;L's Net Profit. The
        /// asset itself still sits on the Balance Sheet at cost with nothing written off against it,
        /// which is why that statement's retained profit is higher by this amount and says so.</summary>
        public decimal TotalAssetPurchases { get; set; }

        // Populated only when a single finance account is selected. Opening balance and the
        // balance accumulated up to the end of the selected period (period start is ignored so
        // the figure is a true running balance, not a period delta).
        public decimal? AccountOpeningBalance { get; set; }
        public decimal? AccountCurrentBalance { get; set; }

        /// <summary>Cash movement over the period for the selected account. Distinct from
        /// <see cref="NetProfit"/>: expenses count at what actually left the account (net of tax
        /// withheld), and FBR deposits count even though they are not a business cost.</summary>
        public decimal? AccountNetMovement { get; set; }
    }

    /// <summary>A single row in the revenue table (automatic payment OR manual revenue).</summary>
    public class RevenueLineDto
    {
        public DateTime Date { get; set; }
        public int? ProjectId { get; set; }
        public string ProjectName { get; set; } = "—";

        /// <summary>e.g. Booking Amount, Installment Payment, Possession Payment, Transfer Charges...</summary>
        public string RevenueType { get; set; } = string.Empty;
        public int? RevenueCategoryId { get; set; }

        public decimal Amount { get; set; }

        /// <summary>"Payment" (automatic) or "Manual Revenue".</summary>
        public string Source { get; set; } = string.Empty;

        public string? Reference { get; set; }

        public string? Description { get; set; }

        /// <summary>Set for manual revenue rows so they can be edited/deleted from the UI.</summary>
        public int? ManualRevenueId { get; set; }

        public int? FinanceAccountId { get; set; }
        public string? FinanceAccountName { get; set; }
        public string? AccountHolderName { get; set; }

        public FinanceAttachmentDto? Attachment { get; set; }

        /// <summary>Base64 row version, set only on manual-revenue rows (the editable ones).
        /// Recognised sales and retained cancellations are events, not records to be corrected
        /// in place, so they carry none.</summary>
        public string? ConcurrencyToken { get; set; }
    }

    /// <summary>A single row in the expense table.</summary>
    public class ExpenseLineDto
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public int? ProjectId { get; set; }
        public string ProjectName { get; set; } = "—";
        public string Category { get; set; } = string.Empty;
        public int? CategoryId { get; set; }

        /// <summary>Gross — the business cost. Cash paid is <see cref="NetPaid"/>.</summary>
        public decimal Amount { get; set; }

        public bool WhtApplied { get; set; }
        public decimal WhtRate { get; set; }
        public decimal WhtAmount { get; set; }
        public decimal NetPaid { get; set; }
        public bool WhtRateOverridden { get; set; }
        public string? WhtOverrideReason { get; set; }
        public string? WhtTaxSection { get; set; }

        public string? Description { get; set; }
        public string? Reference { get; set; }
        public int? VendorId { get; set; }
        public int? FinanceAccountId { get; set; }
        public string? FinanceAccountName { get; set; }
        public string? AccountHolderName { get; set; }
        public FinanceAttachmentDto? Attachment { get; set; }

        /// <summary>Base64 row version — send it back on the next update or delete.</summary>
        public string ConcurrencyToken { get; set; } = string.Empty;
    }

    /// <summary>
    /// One booking's share of the Customer Deposits liability, as at the selected end date.
    /// Read-only and entirely derived: the authoritative records are the Payment rows and the
    /// possession/cancellation events that clear them.
    /// </summary>
    public class CustomerDepositLineDto
    {
        public int BookingId { get; set; }
        public string BookingReference { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public int? ProjectId { get; set; }
        public string ProjectName { get; set; } = "—";
        public string UnitNumber { get; set; } = string.Empty;

        /// <summary>Booking status today — context only. The balance itself is date-derived.</summary>
        public string BookingStatus { get; set; } = string.Empty;

        /// <summary>AgreedSalePrice − DiscountAmount: what this deposit is being held against.</summary>
        public decimal NetSaleValue { get; set; }

        /// <summary>Customer cash received up to and including the as-at date.</summary>
        public decimal CustomerCashReceived { get; set; }

        /// <summary>The liability still held at the as-at date.</summary>
        public decimal DepositBalance { get; set; }

        /// <summary>Set once possession recognised the sale — even if that happened after the
        /// as-at date, which is exactly when it explains why a balance is about to disappear.</summary>
        public DateTime? RecognitionDate { get; set; }

        public DateTime? CancellationDate { get; set; }
    }

    /// <summary>A booking with an unpaid balance (Agreed Sale Price − Received).</summary>
    public class OutstandingLineDto
    {
        public string BookingReference { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public int? ProjectId { get; set; }
        public string ProjectName { get; set; } = "—";
        public string UnitNumber { get; set; } = string.Empty;
        public decimal AgreedSalePrice { get; set; }
        public decimal ReceivedAmount { get; set; }
        public decimal OutstandingAmount { get; set; }
    }

    /// <summary>An installment that is past due and not fully paid.</summary>
    public class OverdueLineDto
    {
        public string BookingReference { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public int? ProjectId { get; set; }
        public string ProjectName { get; set; } = "—";
        public string UnitNumber { get; set; } = string.Empty;
        public int SequenceNumber { get; set; }
        public string InstallmentType { get; set; } = string.Empty;
        public DateTime DueDate { get; set; }
        public decimal Amount { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal OverdueAmount { get; set; }
    }

    /// <summary>A single line in the Net Profit breakdown.</summary>
    public class NetProfitLineDto
    {
        public DateTime Date { get; set; }
        public string ProjectName { get; set; } = "—";
        public string Label { get; set; } = string.Empty;

        /// <summary>
        /// "revenue" or "expense". The two together sum to
        /// <see cref="FinancialSummaryDto.NetProfit"/>. A fixed-asset purchase is an expense line
        /// like any other cost of the period, so there is no third kind and no row the reader has to
        /// know to exclude before the list agrees with the card above it.
        /// </summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>Signed amount: positive for revenue, negative for a cost.</summary>
        public decimal Amount { get; set; }
    }

    /// <summary>
    /// One component of <see cref="FinancialSummaryDto.TotalExpenses"/> — a row in the Total Expenses
    /// drill-down. The signed <see cref="Amount"/>s across every page total that card exactly.
    /// </summary>
    public class CostLineDto
    {
        public DateTime Date { get; set; }
        public string ProjectName { get; set; } = "—";
        public string Label { get; set; } = string.Empty;

        /// <summary>
        /// "cost" for money the period spent, "reduction" for something that gave a cost back — a
        /// reversed commission or rebate, or a withdrawn customer credit. There is no third kind:
        /// everything in the card is one or the other, so nothing has to be excluded by hand before
        /// the rows agree with it.
        /// </summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>Signed: positive for a cost, negative for a reduction.</summary>
        public decimal Amount { get; set; }

        /// <summary>Set only on ordinary expense rows — the ones that can be opened and corrected.
        /// Null on every other component, which is recorded elsewhere in its own workflow.</summary>
        public int? ExpenseId { get; set; }

        /// <summary>
        /// Which kind of record produced this row: "expense", "assetPurchase", "commission",
        /// "rebate", "customerCredit" or "loanInterest". The drill-down is a list of six different
        /// things, and a reader who clicks one has to be taken to the workflow that owns it — the
        /// label alone cannot be parsed for that, and guessing from it would break the moment a
        /// category is renamed.
        /// </summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>
        /// The id of the record named by <see cref="Source"/>. Always set: every component of the
        /// card is a row in some table. Only "expense" and "assetPurchase" can be edited in place;
        /// the rest identify a record on another page.
        /// </summary>
        public int SourceId { get; set; }
    }

    /// <summary>
    /// The whole Finance dashboard in one answer: the cards, the trend and the project split, all
    /// computed from ONE set of date bounds so they cannot describe different periods.
    /// </summary>
    public class FinanceDashboardDataDto
    {
        public FinancialSummaryDto Summary { get; set; } = new();

        /// <summary>Contiguous windows covering the selected range — every day in the range is in
        /// exactly one of them, so the bars total the cards.</summary>
        public List<FinanceTrendBucketDto> Trend { get; set; } = [];

        public List<FinanceDistributionSliceDto> Distribution { get; set; } = [];
    }

    /// <summary>One bar of the revenue/expense trend, and the exact dates it covers.</summary>
    public class FinanceTrendBucketDto
    {
        public string Label { get; set; } = string.Empty;
        public DateTime From { get; set; }

        /// <summary>Inclusive — the last day counted in this bar.</summary>
        public DateTime To { get; set; }

        public decimal Revenue { get; set; }

        /// <summary>Every cost of the window, on the same terms as
        /// <see cref="FinancialSummaryDto.TotalExpenses"/> — fixed assets included.</summary>
        public decimal Expense { get; set; }
    }

    /// <summary>One slice of revenue by project, for the selected range.</summary>
    public class FinanceDistributionSliceDto
    {
        public string Name { get; set; } = string.Empty;
        public decimal Revenue { get; set; }
        public decimal Percent { get; set; }
    }

    /// <summary>One page of rows for an infinite-scroll table.</summary>
    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = new();

        /// <summary>True when more rows exist beyond this page.</summary>
        public bool HasMore { get; set; }
    }
}
