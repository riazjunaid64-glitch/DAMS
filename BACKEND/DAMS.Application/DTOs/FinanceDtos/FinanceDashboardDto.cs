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
    }

    /// <summary>A single row in the revenue table (automatic payment OR manual revenue).</summary>
    public class RevenueLineDto
    {
        public DateTime Date { get; set; }
        public int? ProjectId { get; set; }
        public string ProjectName { get; set; } = "—";

        /// <summary>e.g. Booking Amount, Installment Payment, Possession Payment, Transfer Charges...</summary>
        public string RevenueType { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        /// <summary>"Payment" (automatic) or "Manual Revenue".</summary>
        public string Source { get; set; } = string.Empty;

        public string? Reference { get; set; }

        /// <summary>Set for manual revenue rows so they can be edited/deleted from the UI.</summary>
        public int? ManualRevenueId { get; set; }
    }

    /// <summary>A single row in the expense table.</summary>
    public class ExpenseLineDto
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public int? ProjectId { get; set; }
        public string ProjectName { get; set; } = "—";
        public string Category { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string? Description { get; set; }
        public string? Reference { get; set; }
    }

    public class FinanceDashboardDto
    {
        public FinancialSummaryDto Summary { get; set; } = new();
        public List<RevenueLineDto> Revenue { get; set; } = new();
        public List<ExpenseLineDto> Expenses { get; set; } = new();
    }
}
