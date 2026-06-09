namespace DAMS.Application.DTOs.EmployeeDtos
{
    public class SalaryBatchItemDto
    {
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public decimal BaseSalary { get; set; }
        public bool IsPaid { get; set; }
        public int? SalaryRecordId { get; set; }
        public decimal? PaidAmount { get; set; }
        public DateTime? PayDate { get; set; }
        public string? ProjectName { get; set; }
    }
}
