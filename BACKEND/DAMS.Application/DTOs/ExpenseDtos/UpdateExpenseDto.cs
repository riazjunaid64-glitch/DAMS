namespace DAMS.Application.DTOs.ExpenseDtos
{
    public class UpdateExpenseDto
    {
        public int? ProjectId { get; set; }

        public decimal Amount { get; set; }

        public string Category { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? Vendor { get; set; }

        public DateTime? Date { get; set; }
    }
}
