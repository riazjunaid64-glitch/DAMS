namespace DAMS.Application.DTOs.ExpenseDtos
{
    public class ExpenseResponseDto
    {
        public int Id { get; set; }

        public int? ProjectId { get; set; }

        public string? ProjectName { get; set; }

        public decimal Amount { get; set; }

        public string Category { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? Vendor { get; set; }

        public DateTime Date { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
