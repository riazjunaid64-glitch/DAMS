namespace DAMS.Application.DTOs.EmployeeDtos
{
    public class SalaryResponseDto
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime PayDate { get; set; }
        public int PayMonth { get; set; }
        public int PayYear { get; set; }
        public int? ProjectId { get; set; }
        public string? ProjectName { get; set; }
        public int? ExpenseId { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
