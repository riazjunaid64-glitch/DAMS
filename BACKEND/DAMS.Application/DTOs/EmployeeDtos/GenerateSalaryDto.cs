namespace DAMS.Application.DTOs.EmployeeDtos
{
    public class GenerateSalaryDto
    {
        public decimal Amount { get; set; }

        public DateTime PayDate { get; set; }

        public string? Notes { get; set; }
    }
}
