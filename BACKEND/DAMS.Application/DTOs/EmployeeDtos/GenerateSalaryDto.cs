namespace DAMS.Application.DTOs.EmployeeDtos
{
    public class GenerateSalaryDto
    {
        public decimal Amount { get; set; }

        /// <summary>
        /// The date the money actually left the account. This is the posting date of the Expense and
        /// of the bank/cash movement, so it has to be the real one: August's payroll settled on
        /// 5 September is a September cash movement, and dating it 1 August puts the bank balance
        /// wrong for every day in between even though the totals still balance.
        /// </summary>
        public DateTime PayDate { get; set; }

        /// <summary>
        /// The payroll period this salary is FOR, independent of when it was paid. Sent together as
        /// month + year; omit both and the period follows <see cref="PayDate"/>, which is the old
        /// behaviour and stays correct whenever payroll is settled inside its own month.
        /// </summary>
        public int? PayMonth { get; set; }

        public int? PayYear { get; set; }

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
