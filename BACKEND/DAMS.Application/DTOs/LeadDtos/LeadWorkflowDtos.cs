using System.ComponentModel.DataAnnotations;
using DAMS.Application.Common;
using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.LeadDtos
{
    public class UpdateLeadDto : IValidatableObject
    {
        private readonly HashSet<string> _provided = [];
        private string _firstName = string.Empty;
        private string? _lastName, _phone, _whatsappNumber, _email, _address, _city;
        private string? _preferredContactTime, _sourceDetails, _campaignName, _campaignReference, _adReference;
        private int? _interestedProjectId, _interestedUnitId;
        private string? _propertyType, _preferredLocation, _notes, _concurrencyToken;
        private decimal? _budgetMin, _budgetMax;
        private LeadContactMethod _preferredContactMethod = LeadContactMethod.Phone;
        private LeadPurchaseIntent _purchaseIntent = LeadPurchaseIntent.Unknown;
        private LeadPaymentPreference _paymentPreference = LeadPaymentPreference.Unknown;

        [StringLength(100)]
        public string FirstName { get => _firstName; set { _provided.Add(nameof(FirstName)); _firstName = value; } }

        [StringLength(100)]
        public string? LastName { get => _lastName; set { _provided.Add(nameof(LastName)); _lastName = value; } }

        /// <summary>Optional; LeadService rejects an edit that would leave no contact method at all.</summary>
        [StringLength(50)]
        public string? Phone { get => _phone; set { _provided.Add(nameof(Phone)); _phone = value; } }

        [StringLength(50)]
        public string? WhatsappNumber { get => _whatsappNumber; set { _provided.Add(nameof(WhatsappNumber)); _whatsappNumber = value; } }

        /// <summary>Optional; a blank value means "no email", exactly as on <see cref="LeadIntakeDto.Email"/>.</summary>
        [EmailAddress]
        [StringLength(200)]
        public string? Email { get => _email; set { _provided.Add(nameof(Email)); _email = OptionalInput.BlankAsNull(value); } }

        [StringLength(500)]
        public string? Address { get => _address; set { _provided.Add(nameof(Address)); _address = value; } }

        [StringLength(100)]
        public string? City { get => _city; set { _provided.Add(nameof(City)); _city = value; } }

        public LeadContactMethod PreferredContactMethod { get => _preferredContactMethod; set { _provided.Add(nameof(PreferredContactMethod)); _preferredContactMethod = value; } }

        [StringLength(100)]
        public string? PreferredContactTime { get => _preferredContactTime; set { _provided.Add(nameof(PreferredContactTime)); _preferredContactTime = value; } }

        [StringLength(500)]
        public string? SourceDetails { get => _sourceDetails; set { _provided.Add(nameof(SourceDetails)); _sourceDetails = value; } }

        [StringLength(200)]
        public string? CampaignName { get => _campaignName; set { _provided.Add(nameof(CampaignName)); _campaignName = value; } }

        [StringLength(200)]
        public string? CampaignReference { get => _campaignReference; set { _provided.Add(nameof(CampaignReference)); _campaignReference = value; } }

        [StringLength(200)]
        public string? AdReference { get => _adReference; set { _provided.Add(nameof(AdReference)); _adReference = value; } }

        public int? InterestedProjectId { get => _interestedProjectId; set { _provided.Add(nameof(InterestedProjectId)); _interestedProjectId = value; } }

        public int? InterestedUnitId { get => _interestedUnitId; set { _provided.Add(nameof(InterestedUnitId)); _interestedUnitId = value; } }

        [StringLength(100)]
        public string? PropertyType { get => _propertyType; set { _provided.Add(nameof(PropertyType)); _propertyType = value; } }

        [StringLength(200)]
        public string? PreferredLocation { get => _preferredLocation; set { _provided.Add(nameof(PreferredLocation)); _preferredLocation = value; } }

        [Range(0, 999999999999)]
        public decimal? BudgetMin { get => _budgetMin; set { _provided.Add(nameof(BudgetMin)); _budgetMin = value; } }

        [Range(0, 999999999999)]
        public decimal? BudgetMax { get => _budgetMax; set { _provided.Add(nameof(BudgetMax)); _budgetMax = value; } }

        public LeadPurchaseIntent PurchaseIntent { get => _purchaseIntent; set { _provided.Add(nameof(PurchaseIntent)); _purchaseIntent = value; } }

        public LeadPaymentPreference PaymentPreference { get => _paymentPreference; set { _provided.Add(nameof(PaymentPreference)); _paymentPreference = value; } }

        [StringLength(2000)]
        public string? Notes { get => _notes; set { _provided.Add(nameof(Notes)); _notes = value; } }

        // Base64 RowVersion from the lead the edit form was opened with.
        public string? ConcurrencyToken { get => _concurrencyToken; set { _provided.Add(nameof(ConcurrencyToken)); _concurrencyToken = value; } }

        public bool WasProvided(string propertyName) => _provided.Contains(propertyName);

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (WasProvided(nameof(FirstName)) && (string.IsNullOrWhiteSpace(FirstName) || FirstName.Trim().Length < 2))
                yield return new ValidationResult("The FirstName field must contain at least 2 characters.", [nameof(FirstName)]);
        }
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
