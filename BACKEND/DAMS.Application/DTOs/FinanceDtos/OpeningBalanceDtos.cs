using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.FinanceDtos
{
    public sealed class OpeningBalanceSetDto
    {
        public int Id { get; set; }
        public DateTime AsAtDate { get; set; }
        public bool IsCommitted { get; set; }
        public DateTime? CommittedAt { get; set; }
        public int? CommittedByUserId { get; set; }
        public decimal TotalDebits { get; set; }
        public decimal TotalCredits { get; set; }
        public decimal Difference { get; set; }
        public List<OpeningBalanceEntryDto> Entries { get; set; } = [];
        public List<OpeningBalanceAuditDto> AuditEntries { get; set; } = [];
        public string ConcurrencyToken { get; set; } = string.Empty;
    }

    public sealed class OpeningBalanceEntryDto
    {
        public int FinanceAccountId { get; set; }
        public string AccountName { get; set; } = string.Empty;
        public string? LedgerCode { get; set; }
        public FinanceAccountType AccountType { get; set; }
        public int DisplayOrder { get; set; }
        public decimal DebitAmount { get; set; }
        public decimal CreditAmount { get; set; }
        public string? Note { get; set; }
    }

    public sealed class OpeningBalanceAuditDto
    {
        public string Action { get; set; } = string.Empty;
        public int? UserId { get; set; }
        public DateTime OccurredAt { get; set; }
        public string? Note { get; set; }
    }

    public sealed class CreateOpeningBalanceSetDto
    {
        public DateTime AsAtDate { get; set; }
    }

    public sealed class SaveOpeningBalanceSetDto
    {
        /// <summary>
        /// Corrects the go-live date of a draft that has not been committed. Nullable because an
        /// omitted date must leave the stored one alone rather than reset it to default(DateTime).
        /// </summary>
        public DateTime? AsAtDate { get; set; }
        public List<SaveOpeningBalanceEntryDto> Entries { get; set; } = [];
        public string ConcurrencyToken { get; set; } = string.Empty;
    }

    public sealed class SaveOpeningBalanceEntryDto
    {
        public int FinanceAccountId { get; set; }
        public decimal DebitAmount { get; set; }
        public decimal CreditAmount { get; set; }
        public string? Note { get; set; }
    }

    public sealed class ReopenOpeningBalanceSetDto
    {
        public bool WarningAccepted { get; set; }
        public string? Note { get; set; }
        public string ConcurrencyToken { get; set; } = string.Empty;
    }
}
