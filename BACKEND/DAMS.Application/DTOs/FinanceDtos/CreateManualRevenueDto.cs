namespace DAMS.Application.DTOs.FinanceDtos
{
    public class CreateManualRevenueDto
    {
        public int? ProjectId { get; set; }

        public int? FinanceAccountId { get; set; }

        public decimal Amount { get; set; }

        public string RevenueType { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? Reference { get; set; }

        public DateTime? Date { get; set; }
    }
}
