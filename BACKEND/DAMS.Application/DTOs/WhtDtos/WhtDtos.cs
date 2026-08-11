using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.WhtDtos
{
    /// <summary>Live preview request from the expense form — sent on every category, vendor or
    /// amount change so the operator sees the tax before saving.</summary>
    public class WhtCalculationRequestDto
    {
        public int? CategoryId { get; set; }
        public int? VendorId { get; set; }
        public decimal GrossAmount { get; set; }
        public DateTime? Date { get; set; }

        /// <summary>Set when previewing an edit, so the expense's own amount is excluded from its
        /// year-to-date total and does not inflate the threshold check against itself.</summary>
        public int? ExcludeExpenseId { get; set; }
    }

    public class WhtCalculationResultDto
    {
        public bool IsWhtApplicable { get; set; }
        public decimal Rate { get; set; }
        public decimal WhtAmount { get; set; }
        public decimal NetPaid { get; set; }
        public bool WhtApplied { get; set; }
        public FilerStatus FilerStatus { get; set; }
        public string? TaxSection { get; set; }
        public bool BelowThreshold { get; set; }
        public decimal AnnualThreshold { get; set; }
        public decimal YearToDateTotal { get; set; }
        public string FinancialYear { get; set; } = string.Empty;

        /// <summary>Plain-language explanation of the figure, shown under the tax fields.</summary>
        public string? Notice { get; set; }
    }

    public class FinanceSettingsDto
    {
        public int FinancialYearStartMonth { get; set; }
        public DateTime? WhtRatesConfirmedAt { get; set; }
        public string? WhtRatesConfirmedByName { get; set; }
        public string CurrentFinancialYear { get; set; } = string.Empty;
        public string ConcurrencyToken { get; set; } = string.Empty;
    }

    public class SaveFinanceSettingsDto
    {
        public int FinancialYearStartMonth { get; set; } = 7;

        /// <summary>Set by the accountant once the rate table has been checked against the current
        /// Finance Act. Clearing it puts the unverified banner back.</summary>
        public bool MarkRatesConfirmed { get; set; }
        public bool ClearRatesConfirmation { get; set; }
        public string? ConcurrencyToken { get; set; }
    }

    /// <summary>Headline position for the WHT payable panel.</summary>
    public class WhtPayableSummaryDto
    {
        public decimal WithheldInPeriod { get; set; }
        public decimal DepositedInPeriod { get; set; }

        /// <summary>All-time withheld minus all-time deposited: the money still owed to FBR.
        /// Not period-filtered, because a liability is a balance, not a flow.</summary>
        public decimal OutstandingPayable { get; set; }

        public decimal TotalWithheldAllTime { get; set; }
        public decimal TotalDepositedAllTime { get; set; }
        public int ExpenseCount { get; set; }
        public int VendorCount { get; set; }
        public List<WhtSectionTotalDto> BySection { get; set; } = new();
    }

    public class WhtSectionTotalDto
    {
        public string TaxSection { get; set; } = string.Empty;
        public decimal GrossAmount { get; set; }
        public decimal WhtAmount { get; set; }
        public int ExpenseCount { get; set; }
    }

    /// <summary>One line of the s.165 withholding statement / vendor certificate.</summary>
    public class WhtVendorLineDto
    {
        public int? VendorId { get; set; }
        public string VendorName { get; set; } = string.Empty;
        public string? Ntn { get; set; }
        public string? Cnic { get; set; }
        public FilerStatus FilerStatus { get; set; }
        public string? TaxSection { get; set; }
        public decimal GrossAmount { get; set; }
        public decimal WhtAmount { get; set; }
        public decimal NetPaid { get; set; }
        public int ExpenseCount { get; set; }
    }

    public class SaveWhtDepositDto
    {
        public int FinanceAccountId { get; set; }
        public decimal Amount { get; set; }
        public DateTime? DepositDate { get; set; }
        public string? ChallanNumber { get; set; }
        public DateTime? PeriodFrom { get; set; }
        public DateTime? PeriodTo { get; set; }
        public string? Notes { get; set; }
        public string? ConcurrencyToken { get; set; }
    }

    public class WhtDepositDto
    {
        public int Id { get; set; }
        public int FinanceAccountId { get; set; }
        public string? FinanceAccountName { get; set; }
        public decimal Amount { get; set; }
        public DateTime DepositDate { get; set; }
        public string? ChallanNumber { get; set; }
        public DateTime? PeriodFrom { get; set; }
        public DateTime? PeriodTo { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public string ConcurrencyToken { get; set; } = string.Empty;
    }
}
