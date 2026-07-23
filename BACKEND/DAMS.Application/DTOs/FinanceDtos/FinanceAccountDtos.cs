using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.FinanceDtos
{
    public class CreateFinanceAccountDto
    {
        public string Name { get; set; } = string.Empty;
        public FinanceAccountType Type { get; set; }
        public string AccountHolderName { get; set; } = string.Empty;
        public decimal OpeningBalance { get; set; }
        public string? BankOrWalletName { get; set; }
        public string? Description { get; set; }
    }

    public sealed class UpdateFinanceAccountDto : CreateFinanceAccountDto
    {
        public string ConcurrencyToken { get; set; } = string.Empty;
    }

    public class FinanceAccountOptionDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public FinanceAccountType Type { get; set; }
        public string AccountHolderName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    public sealed class FinanceAccountResponseDto : FinanceAccountOptionDto
    {
        public decimal OpeningBalance { get; set; }
        public string? BankOrWalletName { get; set; }
        public string? Description { get; set; }
        public decimal RevenueReceived { get; set; }
        public decimal ExpensesPaid { get; set; }
        public decimal NetMovement { get; set; }
        public decimal CurrentBalance { get; set; }
        public int TransactionCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string ConcurrencyToken { get; set; } = string.Empty;
    }

    public sealed class FinanceAccountTransactionDto
    {
        public string Kind { get; set; } = string.Empty;
        public int RecordId { get; set; }
        public DateTime Date { get; set; }
        public string Label { get; set; } = string.Empty;
        public string? Reference { get; set; }
        public string ProjectName { get; set; } = "General";
        public decimal Amount { get; set; }
    }

    public sealed class FinanceHolderBalanceDto
    {
        public string AccountHolderName { get; set; } = string.Empty;
        public int AccountCount { get; set; }
        public decimal CurrentBalance { get; set; }
    }

    public sealed class FinanceAccountsOverviewDto
    {
        public int ActiveAccounts { get; set; }
        public int InactiveAccounts { get; set; }
        public decimal TotalBalance { get; set; }
        public List<FinanceHolderBalanceDto> HolderBalances { get; set; } = new();
    }
}
