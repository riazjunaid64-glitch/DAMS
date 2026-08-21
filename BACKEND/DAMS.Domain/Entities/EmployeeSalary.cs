namespace DAMS.Domain.Entities
{
    public class EmployeeSalary
    {
        public int Id { get; set; }

        public int EmployeeId { get; set; }

        public decimal Amount { get; set; }

        public DateTime PayDate { get; set; }

        public int PayMonth { get; set; }

        public int PayYear { get; set; }

        public int? ProjectId { get; set; }

        public string? ProjectName { get; set; }

        public int? ExpenseId { get; set; }

        public string? Notes { get; set; }

        public int? CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Optimistic concurrency token. A salary correction rewrites the amount, the pay date, the
        /// payroll period AND the linked Expense — so two admins fixing the same record from two
        /// screens used to produce one silent winner: whichever save landed second overwrote the
        /// other in full, including moving the expense back onto its own date. Every other mutable
        /// finance record already carries one of these; payroll writes a real Expense, so it is a
        /// finance record too.
        /// </summary>
        public byte[] RowVersion { get; set; } = [];

        public Employee Employee { get; set; } = null!;

        public Project? Project { get; set; }

        public Expense? Expense { get; set; }
    }
}
