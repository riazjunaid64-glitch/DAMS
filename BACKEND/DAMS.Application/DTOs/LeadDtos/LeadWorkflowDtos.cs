using System.ComponentModel.DataAnnotations;
using DAMS.Application.Common;
using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.LeadDtos
{
    public class UpdateLeadDto
    {
        [Required]
        [StringLength(100, MinimumLength = 2)]
        public string FirstName { get; set; } = string.Empty;

        [StringLength(100)]
        public string? LastName { get; set; }

        /// <summary>Optional; LeadService rejects an edit that would leave no contact method at all.</summary>
        [StringLength(50)]
        public string? Phone { get; set; }

        [StringLength(50)]
        public string? WhatsappNumber { get; set; }

        /// <summary>Optional; a blank value means "no email", exactly as on <see cref="LeadIntakeDto.Email"/>.</summary>
        [EmailAddress]
        [StringLength(200)]
        public string? Email { get => _email; set => _email = OptionalInput.BlankAsNull(value); }
        private string? _email;

        [StringLength(500)]
        public string? Address { get; set; }

        [StringLength(100)]
        public string? City { get; set; }

        public LeadContactMethod PreferredContactMethod { get; set; } = LeadContactMethod.Phone;

        [StringLength(100)]
        public string? PreferredContactTime { get; set; }

        [StringLength(500)]
        public string? SourceDetails { get; set; }

        [StringLength(200)]
        public string? CampaignName { get; set; }

        [StringLength(200)]
        public string? CampaignReference { get; set; }

        [StringLength(200)]
        public string? AdReference { get; set; }

        public int? InterestedProjectId { get; set; }

        public int? InterestedUnitId { get; set; }

        [StringLength(100)]
        public string? PropertyType { get; set; }

        [StringLength(200)]
        public string? PreferredLocation { get; set; }

        [Range(0, 999999999999)]
        public decimal? BudgetMin { get; set; }

        [Range(0, 999999999999)]
        public decimal? BudgetMax { get; set; }

        public LeadPurchaseIntent PurchaseIntent { get; set; } = LeadPurchaseIntent.Unknown;

        [StringLength(2000)]
        public string? Notes { get; set; }
    }

    public class AssignLeadDto
    {
        public int? EmployeeId { get; set; }

        public int? TeamId { get; set; }

        [StringLength(500)]
        public string? Reason { get; set; }
    }

    public class LeadAssignmentHistoryDto
    {
        public int Id { get; set; }

        public int? PreviousEmployeeId { get; set; }

        public string? PreviousEmployeeName { get; set; }

        public int? PreviousTeamId { get; set; }

        public int? AssignedEmployeeId { get; set; }

        public string? AssignedEmployeeName { get; set; }

        public int? AssignedTeamId { get; set; }

        public string? Reason { get; set; }

        public int? AssignedByUserId { get; set; }

        public string? AssignedByName { get; set; }

        public DateTime AssignedAt { get; set; }
    }

    public class ChangeLeadStageDto
    {
        [Required]
        public LeadStage Stage { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }
    }

    public class UpdateLeadQualificationDto
    {
        [Required]
        public LeadQualification Qualification { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }
    }

    public class CloseLeadDto
    {
        /// <summary>Configured closure reason. Required for both Lost and Dormant.</summary>
        [Required]
        public int ClosureReasonId { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }

        /// <summary>Optional date a dormant lead should resurface. Dormant closures only.</summary>
        public DateTime? ReactivateOn { get; set; }
    }

    public class ReopenLeadDto
    {
        /// <summary>Stage to reopen into. Must be an active (non-terminal) stage.</summary>
        public LeadStage Stage { get; set; } = LeadStage.Contacted;

        [Required]
        [StringLength(1000, MinimumLength = 3)]
        public string Reason { get; set; } = string.Empty;
    }

    public class ConvertLeadDto
    {
        internal int? BookingRequestId { get; set; }

        /// <summary>Unit being booked. Required — conversion always produces a booking.</summary>
        [Required]
        public int UnitId { get; set; }

        /// <summary>Existing customer to link. When omitted, matched or created from the lead.</summary>
        public int? CustomerId { get; set; }

        [StringLength(50)]
        public string? CNIC { get; set; }

        [StringLength(200)]
        public string? FatherName { get; set; }

        [Range(0.01, 9999999999999)]
        public decimal? AgreedSalePrice { get; set; }

        [Range(0, 100)]
        public decimal? DiscountPercent { get; set; }

        [StringLength(500)]
        public string? DiscountReason { get; set; }

        [Range(0, 9999999999999)]
        public decimal? BookingAmountRequired { get; set; }

        public DateTime? BookingAmountDueDate { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }
    }

    public class LeadConversionResultDto
    {
        /// <summary>False when the lead had already been converted and the existing result was returned.</summary>
        public bool Created { get; set; }

        public int LeadId { get; set; }

        public string LeadReference { get; set; } = string.Empty;

        public int CustomerId { get; set; }

        public bool CustomerWasCreated { get; set; }

        public int BookingId { get; set; }

        public string BookingReference { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;
    }

    public class LeadBackfillResultDto
    {
        public int BookingRequestsScanned { get; set; }

        public int LeadsCreated { get; set; }

        public int AlreadyLinked { get; set; }

        /// <summary>Requests skipped because their enquiry is waiting in the held-enquiry review.</summary>
        public int HeldForReview { get; set; }

        public string Message { get; set; } = string.Empty;
    }
}
