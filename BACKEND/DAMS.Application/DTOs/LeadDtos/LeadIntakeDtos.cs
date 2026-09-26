using System.ComponentModel.DataAnnotations;
using DAMS.Application.Common;
using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.LeadDtos
{
    /// <summary>
    /// The single shape every channel uses to hand a prospect to DAMS — manual entry, the
    /// website form, a Meta lead ad, a portal, a webhook. There is deliberately only one
    /// ingestion path so no channel can grow its own parallel lead system.
    /// </summary>
    public class LeadIntakeDto
    {
        [Required]
        [StringLength(100, MinimumLength = 2)]
        public string FirstName { get; set; } = string.Empty;

        [StringLength(100)]
        public string? LastName { get; set; }

        /// <summary>
        /// Optional, because an ad-platform lead may genuinely arrive without one. The rule
        /// that a manually entered lead needs at least one contact method is enforced in
        /// LeadService, where the originating channel is known.
        /// </summary>
        [StringLength(50)]
        public string? Phone { get; set; }

        [StringLength(50)]
        public string? WhatsappNumber { get; set; }

        /// <summary>
        /// Optional. Forms send every field, so a blank value means "no email" and becomes null
        /// here, before [EmailAddress] would reject it as a malformed address.
        /// </summary>
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

        /// <summary>Code of a configured lead source, e.g. "walk_in". Defaults to "manual".</summary>
        [StringLength(50)]
        public string? SourceCode { get; set; }

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

        public LeadPaymentPreference PaymentPreference { get; set; } = LeadPaymentPreference.Unknown;

        [StringLength(2000)]
        public string? Notes { get; set; }

        /// <summary>Optional owner to assign straight away (admin/manager only).</summary>
        public int? AssignedEmployeeId { get; set; }

        public int? AssignedTeamId { get; set; }

        // ── External channel metadata ──
        [StringLength(50)]
        public string? ExternalProvider { get; set; }

        /// <summary>Provider's own id for this submission. Makes ingestion idempotent.</summary>
        [StringLength(200)]
        public string? ExternalLeadId { get; set; }

        [StringLength(200)]
        public string? ExternalFormReference { get; set; }

        public DateTime? ExternalSubmittedAt { get; set; }

        [StringLength(4000)]
        public string? IntegrationPayload { get; set; }

        /// <summary>
        /// When true, a matching existing lead is enriched instead of the call being
        /// rejected as a duplicate. External channels always enrich.
        /// </summary>
        public bool AllowDuplicate { get; set; }

        /// <summary>
        /// The lead a person chose to add this enquiry to. When set, the enquiry is only ever
        /// added to that lead: if the details now match another lead, or no open lead at all,
        /// nothing is written and the result says why. Never creates a lead.
        /// </summary>
        public int? ExpectedExistingLeadId { get; set; }
    }

    public class LeadIntakeResultDto
    {
        public bool IsDuplicate { get; set; }

        /// <summary>True when an existing lead was updated rather than a new one created.</summary>
        public bool EnrichedExisting { get; set; }

        /// <summary>True when the exact same external submission had already been ingested.</summary>
        public bool AlreadyIngested { get; set; }

        public string Message { get; set; } = string.Empty;

        public LeadDuplicateMatchDto? Match { get; set; }

        /// <summary>
        /// True when the details match more than one open lead — say the phone matches one and
        /// the email another. Neither lead was touched; <see cref="ConflictingMatches"/> lists them.
        /// </summary>
        public bool IdentityConflict { get; set; }

        public List<LeadDuplicateMatchDto> ConflictingMatches { get; set; } = [];

        /// <summary>True when an external enquiry with an identity conflict was kept for an
        /// administrator to resolve instead of being added to a lead.</summary>
        public bool HeldForReview { get; set; }

        public int? HoldId { get; set; }

        public LeadResponseDto? Lead { get; set; }
    }

    public class LeadDuplicateMatchDto
    {
        /// <summary>"phone", "whatsapp" or "email".</summary>
        public string MatchedOn { get; set; } = string.Empty;

        public int? LeadId { get; set; }

        public string? LeadReference { get; set; }

        public LeadStage? LeadStage { get; set; }

        public string? LeadOwnerName { get; set; }

        public int? CustomerId { get; set; }

        public string? CustomerName { get; set; }
    }

    public class LeadIntakeHoldListDto
    {
        /// <summary>Every enquiry waiting, which can be more than <see cref="Items"/> shows.</summary>
        public int TotalWaiting { get; set; }

        /// <summary>The oldest waiting enquiries, oldest first.</summary>
        public List<LeadIntakeHoldDto> Items { get; set; } = [];
    }

    /// <summary>An external enquiry waiting for an administrator to choose its lead.</summary>
    public class LeadIntakeHoldDto
    {
        public int Id { get; set; }

        public DateTime ReceivedAt { get; set; }

        public string? Provider { get; set; }

        public string? SourceName { get; set; }

        public string FirstName { get; set; } = string.Empty;

        public string? LastName { get; set; }

        public string? Phone { get; set; }

        public string? WhatsappNumber { get; set; }

        public string? Email { get; set; }

        public string? CampaignName { get; set; }

        public string? Notes { get; set; }

        /// <summary>Set when a website booking request is waiting on this decision; such a hold
        /// can only be resolved to a lead, never dismissed.</summary>
        public int? BookingRequestId { get; set; }

        /// <summary>The leads it matched when it arrived. Only an open one can receive it.</summary>
        public List<LeadIntakeHoldCandidateDto> Candidates { get; set; } = [];
    }

    public class LeadIntakeHoldCandidateDto
    {
        public int LeadId { get; set; }

        public string LeadReference { get; set; } = string.Empty;

        public string LeadName { get; set; } = string.Empty;

        public LeadStage LeadStage { get; set; }

        public string? LeadOwnerName { get; set; }

        /// <summary>"phone", "whatsapp" or "email": which of the enquiry's details this lead matched.</summary>
        public string MatchedOn { get; set; } = string.Empty;

        /// <summary>False once the lead has closed; it must be reopened before it can receive the enquiry.</summary>
        public bool IsOpen { get; set; }
    }

    public class ResolveLeadIntakeHoldDto
    {
        /// <summary>The lead to add the enquiry to. Must be one of the hold's candidates.</summary>
        public int? LeadId { get; set; }

        /// <summary>Discard the enquiry instead of adding it to a lead.</summary>
        public bool Dismiss { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }
    }
}
