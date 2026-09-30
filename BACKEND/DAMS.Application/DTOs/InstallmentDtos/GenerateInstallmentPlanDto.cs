using System.ComponentModel.DataAnnotations;
using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.InstallmentDtos
{
    public class GenerateInstallmentPlanDto
    {
        [Range(0.01, double.MaxValue)]
        public decimal AgreedSalePrice { get; set; }

        // Discount is entered as a percentage of the agreed sale price (0–100).
        [Range(0, 100)]
        public decimal DiscountPercent { get; set; }

        [StringLength(500)]
        public string? DiscountReason { get; set; }

        // Nullable on the wire so [Required] can tell "left out" from a value: a non-nullable enum
        // or date that was omitted arrives as its default (Monthly, 0001-01-01) and passes.
        [Required(ErrorMessage = "Frequency is required.")]
        public InstallmentFrequency? Frequency { get; set; }

        [Range(1, 600)]
        public int NumberOfInstallments { get; set; }

        // The due date of installment 1.
        [Required(ErrorMessage = "First due date is required.")]
        public DateTime? InstallmentStartDate { get; set; }

        [Range(0, double.MaxValue)]
        public decimal PossessionAmount { get; set; }

        public DateTime? PossessionDueDate { get; set; }

        /// <summary>
        /// When true, replaces an existing schedule. Allowed only when all installments
        /// are Pending and no installment payments exist.
        /// </summary>
        public bool Regenerate { get; set; }
    }
}
