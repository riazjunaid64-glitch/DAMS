namespace DAMS.Application.DTOs.FinanceDtos
{
    /// <summary>The 5 dashboard summary cards for the selected project + date range.</summary>
    public class FinancialSummaryDto
    {
        public decimal TotalRevenue { get; set; }
        public decimal AutomaticRevenue { get; set; }
        public decimal ManualRevenue { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal NetProfit { get; set; }
        public decimal OutstandingAmount { get; set; }
        public decimal OverdueAmount { get; set; }

        /// <summary>Tax withheld from expenses in the period. Money that is inside
        /// <see cref="TotalExpenses"/> but has not left the bank — it is owed to FBR.</summary>
        public decimal WhtWithheld { get; set; }

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

    /// <summary>A single line in the Net Profit breakdown: a revenue (+) or expense (−) entry.</summary>
    public class NetProfitLineDto
    {
        public DateTime Date { get; set; }
        public string ProjectName { get; set; } = "—";
        public string Label { get; set; } = string.Empty;

        /// <summary>"revenue" or "expense".</summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>Signed amount: positive for revenue, negative for expense.</summary>
        public decimal Amount { get; set; }
    }

    /// <summary>One page of rows for an infinite-scroll table.</summary>
    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = new();

        /// <summary>True when more rows exist beyond this page.</summary>
        public bool HasMore { get; set; }
    }
}
