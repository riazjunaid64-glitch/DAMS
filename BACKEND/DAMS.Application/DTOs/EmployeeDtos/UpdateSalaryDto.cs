namespace DAMS.Application.DTOs.EmployeeDtos
{
    public class UpdateSalaryDto
    {
        public decimal? Amount { get; set; }

        /// <summary>The date the money actually left the account. Moving it re-dates the linked
        /// expense and nothing else — the payroll period below is a separate decision.</summary>
        public DateTime? PayDate { get; set; }

        /// <summary>The payroll period this salary is for. Sent together as month + year; omit both
        /// to leave the period where it is.</summary>
        public int? PayMonth { get; set; }

        public int? PayYear { get; set; }

        public string? Notes { get; set; }

        /// <summary>
        /// The <c>concurrencyToken</c> the salary was read with. Required once the record carries a
        /// version: a correction rewrites the amount, the date, the period and the linked Expense,
        /// so accepting a save without it would let the slower of two admins silently undo the
        /// other's work — including moving the posted expense back.
        /// </summary>
        public string? ConcurrencyToken { get; set; }
    }
}
