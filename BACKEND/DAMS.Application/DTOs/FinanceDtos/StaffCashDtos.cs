using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.FinanceDtos
{
    public sealed class CreateStaffCashHolderDto
    {
        public string PersonName { get; set; } = string.Empty;
    }

    public sealed class SaveStaffCashTransferDto
    {
        public StaffCashMovementType Type { get; set; }
        public decimal Amount { get; set; }
        public DateTime Date { get; set; }
        public int CounterpartyFinanceAccountId { get; set; }
        public string? Reference { get; set; }
        public string? Note { get; set; }

        /// <summary>
        /// The row version of the movement being corrected, and absent when one is being recorded.
        /// </summary>
        /// <remarks>
        /// Nullable deliberately. A non-nullable string is implicitly required, so the model binder
        /// rejected every NEW movement before it reached the service — "The ConcurrencyToken field
        /// is required." — because a create legitimately has no version to send. Presence was never
        /// the check that matters: <c>ApplyToken</c> compares the token against the stored row
        /// version and refuses a blank one for a row that has one, which is the guard that counts.
        /// </remarks>
        public string? ConcurrencyToken { get; set; }
    }

    public sealed class StaffCashOverviewDto
    {
        /// <summary>Positive balances only: company money physically held by staff.</summary>
        public decimal TotalHeldByStaff { get; set; }

        /// <summary>Absolute value of negative balances: amounts the company owes staff.</summary>
        public decimal TotalOwedToStaff { get; set; }

        /// <summary>Signed total of every staff float.</summary>
        public decimal NetStaffBalance { get; set; }
        public decimal CashAndBankBalance { get; set; }
        public decimal TrackedCompanyCash { get; set; }
        public int HoldingCount { get; set; }
        public int OwedCount { get; set; }
        public List<StaffCashHolderDto> Holders { get; set; } = [];
    }

    public sealed class StaffCashHolderDto
    {
        public int FinanceAccountId { get; set; }
        public string PersonName { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public decimal OpeningBalance { get; set; }
        public decimal CurrentBalance { get; set; }
        public DateTime? OutstandingSince { get; set; }
        public int? DaysOutstanding { get; set; }
        public DateTime? LastActivityDate { get; set; }
        public int TransactionCount { get; set; }
    }

    public sealed class StaffCashStatementDto
    {
        public StaffCashHolderDto Holder { get; set; } = new();
        public List<StaffCashHistoryItemDto> Items { get; set; } = [];
        public bool HasMore { get; set; }
        public string? NextCursor { get; set; }
    }

    public sealed class StaffCashHistoryItemDto
    {
        public string RecordType { get; set; } = string.Empty;
        public int RecordId { get; set; }
        public string Kind { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? Reference { get; set; }
        public string? ProjectName { get; set; }
        public decimal Amount { get; set; }
        public decimal GrossAmount { get; set; }
        public decimal WhtAmount { get; set; }
        public decimal RunningBalance { get; set; }
        public StaffCashMovementType? MovementType { get; set; }
        public int? CounterpartyFinanceAccountId { get; set; }
        public string? CounterpartyFinanceAccountName { get; set; }
        public string? Note { get; set; }
        public string? ConcurrencyToken { get; set; }

        /// <summary>The slip for the cash that changed hands, or the receipt behind an expense paid
        /// from the float. Null when the movement has none.</summary>
        public FinanceAttachmentDto? Attachment { get; set; }
    }
}
