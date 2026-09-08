using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.FinanceDtos
{
    public class CreateFinanceAccountDto
    {
        public string Name { get; set; } = string.Empty;
        public FinanceAccountType Type { get; set; }
        public string AccountHolderName { get; set; } = string.Empty;
        public decimal OpeningBalance { get; set; }
        public string? LedgerCode { get; set; }
        public int DisplayOrder { get; set; }
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
        public bool IsCashLike => AccountBalanceDirection.IsCashLike(Type);
    }

    public sealed class FinanceAccountResponseDto : FinanceAccountOptionDto
    {
        public decimal OpeningBalance { get; set; }
        public string? LedgerCode { get; set; }
        public int DisplayOrder { get; set; }
        public FinanceSystemAccountRole SystemRole { get; set; }
        public bool IsSystemAccount => SystemRole != FinanceSystemAccountRole.None;
        public string? BankOrWalletName { get; set; }
        public string? Description { get; set; }
        public decimal RevenueReceived { get; set; }

        /// <summary>Cash that left the account: expenses NET of tax withheld, plus commissions,
        /// rebates and FBR deposits.</summary>
        public decimal ExpensesPaid { get; set; }

        /// <summary>Tax withheld from this account's expenses — inside the balance, but owed to
        /// FBR rather than available to spend.</summary>
        public decimal WhtWithheld { get; set; }

        public decimal WhtDeposited { get; set; }

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
        public string? Description { get; set; }
        public int? ProjectId { get; set; }
        public string ProjectName { get; set; } = "General";

        /// <summary>Signed cash effect on the account. For an expense this is the net paid, not
        /// the invoice total.</summary>
        public decimal Amount { get; set; }

        /// <summary>Invoice total, set on expense rows so the detail view can show gross, tax and
        /// net side by side. Zero elsewhere.</summary>
        public decimal GrossAmount { get; set; }

        public decimal WhtAmount { get; set; }

        // Audit ordering is deliberately separate from the user-selected business date. It makes
        // two entries on the same date deterministic even when their ids came from different tables.
        public DateTime PostedAt { get; set; }
        public int SourceOrder { get; set; }
    }

    /// <summary>
    /// Internal statement slice shared by the physical-account ledger and Trial Balance details.
    /// Movements use the account's normal-balance direction; the report layer converts them to Dr/Cr.
    /// </summary>
    public sealed class FinanceAccountLedgerSliceDto
    {
        public int AccountId { get; set; }
        public string AccountName { get; set; } = string.Empty;
        public FinanceAccountType AccountType { get; set; }
        public decimal OpeningNormalBalance { get; set; }
        public decimal PeriodNormalMovement { get; set; }
        public decimal NormalMovementBeforePage { get; set; }
        public List<FinanceAccountTransactionDto> Items { get; set; } = [];
        public bool HasMore { get; set; }
    }

    public sealed class FinanceHolderBalanceDto
    {
        public string AccountHolderName { get; set; } = string.Empty;
        public int AccountCount { get; set; }
        public decimal CurrentBalance { get; set; }
    }

    public sealed class FinanceAccountsPageDto
    {
        public List<FinanceAccountResponseDto> Items { get; set; } = [];
        public bool HasMore { get; set; }
        public FinanceAccountsOverviewDto Overview { get; set; } = new();
    }

    public sealed class FinanceAccountsOverviewDto
    {
        public int ActiveAccounts { get; set; }
        public int InactiveAccounts { get; set; }
        public decimal TotalBalance { get; set; }
        public List<FinanceHolderBalanceDto> HolderBalances { get; set; } = new();
    }
}
