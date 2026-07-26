using System.ComponentModel.DataAnnotations;
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

        [Required]
        [StringLength(50, MinimumLength = 7)]
        public string Phone { get; set; } = string.Empty;

        [StringLength(50)]
        public string? WhatsappNumber { get; set; }

        [EmailAddress]
        [StringLength(200)]
        public string? Email { get; set; }

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
}
