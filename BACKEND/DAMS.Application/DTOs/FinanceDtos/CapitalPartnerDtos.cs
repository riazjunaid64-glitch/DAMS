using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.FinanceDtos
{
    public class SaveCapitalPartnerDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Cnic { get; set; }
        public string? Ntn { get; set; }
        public decimal ProfitSharePercent { get; set; }
        public int? FinanceAccountId { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime? JoinedDate { get; set; }
        public DateTime? ExitedDate { get; set; }
        public string? ConcurrencyToken { get; set; }
    }

    public sealed class CapitalPartnerDto : SaveCapitalPartnerDto
    {
        public int Id { get; set; }
        public string? FinanceAccountName { get; set; }
        public decimal OpeningBalance { get; set; }
        public decimal Contributions { get; set; }
        public decimal Withdrawals { get; set; }
        public decimal ProfitShare { get; set; }
        public decimal LossShare { get; set; }
        public decimal ClosingBalance { get; set; }
        public DateTime CreatedAt { get; set; }
        public new string ConcurrencyToken { get; set; } = string.Empty;
    }

    public class SaveCapitalTransactionDto
    {
        public CapitalTransactionType Type { get; set; }
        public decimal Amount { get; set; }
        public DateTime Date { get; set; }
        public int? FinanceAccountId { get; set; }
        public string? Reference { get; set; }
        public string? Note { get; set; }
    }

    public sealed class CapitalTransactionDto : SaveCapitalTransactionDto
    {
        public int Id { get; set; }
        public decimal? ProfitSharePercentSnapshot { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public sealed class CapitalPartnerStatementDto
    {
        public int PartnerId { get; set; }
        public string PartnerName { get; set; } = string.Empty;
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public decimal OpeningBalance { get; set; }
        public List<CapitalTransactionDto> Transactions { get; set; } = [];
        public decimal ClosingBalance { get; set; }
    }

    public sealed class SaveCapitalPartnerSharesDto
    {
        public List<CapitalPartnerShareDto> Partners { get; set; } = [];
    }

    public sealed class CapitalPartnerShareDto
    {
        public int Id { get; set; }
        public decimal ProfitSharePercent { get; set; }
        public bool IsActive { get; set; }
        public string ConcurrencyToken { get; set; } = string.Empty;
    }
}
