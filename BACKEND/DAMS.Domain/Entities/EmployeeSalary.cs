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

        public Employee Employee { get; set; } = null!;

        public Project? Project { get; set; }

        public Expense? Expense { get; set; }
    }
}
