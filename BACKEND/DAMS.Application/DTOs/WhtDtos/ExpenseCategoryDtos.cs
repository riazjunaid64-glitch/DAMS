namespace DAMS.Application.DTOs.WhtDtos
{
    public class SaveExpenseCategoryDto
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>Stable slug. Ignored on update — the code is the join key for reporting and
        /// must not move once expenses reference it.</summary>
        public string Code { get; set; } = string.Empty;

        public string? Description { get; set; }
        public bool IsWhtApplicable { get; set; } = true;
        public decimal FilerRate { get; set; }
        public decimal NonFilerRate { get; set; }
        public decimal AnnualThreshold { get; set; }
        public string? TaxSection { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; } = true;

        /// <summary>Required on update; ignored on create.</summary>
        public string? ConcurrencyToken { get; set; }
    }

    public class ExpenseCategoryDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsWhtApplicable { get; set; }
        public decimal FilerRate { get; set; }
        public decimal NonFilerRate { get; set; }
        public decimal AnnualThreshold { get; set; }
        public string? TaxSection { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; }

        /// <summary>How many expenses already reference this category — the warning an admin
        /// needs before retiring one.</summary>
        public int ExpenseCount { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string ConcurrencyToken { get; set; } = string.Empty;
    }
}
