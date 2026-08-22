namespace DAMS.Application.DTOs.ExpenseDtos
{
    public sealed class UpdateExpenseDto : CreateExpenseDto
    {
        /// <summary>
        /// The row version the caller loaded. Editing an expense moves its gross cost, its withheld
        /// tax and the paying account's balance together, so a second admin working from a stale copy
        /// must be told to reload rather than silently overwriting the first one's figures.
        /// </summary>
        public string? ConcurrencyToken { get; set; }
    }
}
