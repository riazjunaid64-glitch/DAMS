using System.ComponentModel.DataAnnotations;
using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.InstallmentDtos
{
    public class GenerateInstallmentPlanDto
    {
        [Range(0.01, double.MaxValue)]
        public decimal AgreedSalePrice { get; set; }

        [Range(0, double.MaxValue)]
        public decimal DiscountAmount { get; set; }

        [StringLength(500)]
        public string? DiscountReason { get; set; }

        [Required]
        public InstallmentFrequency Frequency { get; set; }

        [Range(1, 600)]
        public int NumberOfInstallments { get; set; }

        [Required]
        public DateTime InstallmentStartDate { get; set; }

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
