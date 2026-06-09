namespace DAMS.Application.DTOs.EmployeeDtos
{
    public class UpdateSalaryDto
    {
        public decimal? Amount { get; set; }

        public DateTime? PayDate { get; set; }

        public string? Notes { get; set; }
    }
}
