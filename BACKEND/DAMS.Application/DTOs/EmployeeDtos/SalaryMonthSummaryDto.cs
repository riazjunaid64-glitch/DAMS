namespace DAMS.Application.DTOs.EmployeeDtos
{
    public class SalaryMonthSummaryDto
    {
        public int Month { get; set; }
        public int Year { get; set; }
        public string MonthLabel { get; set; } = string.Empty;
        public int Count { get; set; }
        public decimal TotalAmount { get; set; }
    }
}
