namespace DAMS.Application.DTOs.EmployeeDtos
{
    public class GenerateSalaryDto
    {
        public decimal Amount { get; set; }

        public DateTime PayDate { get; set; }

        /// <summary>
        /// The cash, bank or staff-float account the salary was paid from. Required, exactly as it is
        /// on a manual expense: generating a salary writes a real expense row, and an expense with no
        /// paying account debits the P&amp;L with no credit anywhere — the Trial Balance and Balance
        /// Sheet then go out by the salary amount, which the formal reports report as an
        /// "Unassigned expenses" imbalance.
        /// </summary>
        public int? FinanceAccountId { get; set; }

        public string? Notes { get; set; }
    }
}
