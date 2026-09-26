using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.LeadDtos
{
    public class LeadResponseDto
    {
        public int Id { get; set; }

        public string LeadReference { get; set; } = string.Empty;

        public string FirstName { get; set; } = string.Empty;

        public string? LastName { get; set; }

        public string FullName { get; set; } = string.Empty;

        public string? Phone { get; set; }

        public string? WhatsappNumber { get; set; }

        public string? Email { get; set; }

        public string? Address { get; set; }

        public string? City { get; set; }

        public LeadContactMethod PreferredContactMethod { get; set; }

        public string? PreferredContactTime { get; set; }

        public int LeadSourceId { get; set; }

        public string SourceCode { get; set; } = string.Empty;

        public string SourceName { get; set; } = string.Empty;

        public string? SourceDetails { get; set; }

        public string? CampaignName { get; set; }

        public string? CampaignReference { get; set; }

        public string? AdReference { get; set; }

        public string? ExternalProvider { get; set; }

        public string? ExternalLeadId { get; set; }

        public string? ExternalFormReference { get; set; }

        public DateTime? ExternalSubmittedAt { get; set; }

        public LeadIntegrationStatus IntegrationStatus { get; set; }

        public string? IntegrationError { get; set; }

        public int? InterestedProjectId { get; set; }

        public string? InterestedProjectName { get; set; }

        public int? InterestedUnitId { get; set; }

        public string? InterestedUnitNumber { get; set; }

        public string? PropertyType { get; set; }

        public string? PreferredLocation { get; set; }

        public decimal? BudgetMin { get; set; }

        public decimal? BudgetMax { get; set; }

        public LeadPurchaseIntent PurchaseIntent { get; set; }

        public LeadPaymentPreference PaymentPreference { get; set; }

        public string? Notes { get; set; }

        public int? AssignedEmployeeId { get; set; }

        public string? AssignedEmployeeName { get; set; }

        public int? AssignedTeamId { get; set; }

        public string? AssignedTeamName { get; set; }

        public LeadAssignmentState AssignmentState { get; set; }

        public DateTime? AssignedAt { get; set; }

        public LeadStage Stage { get; set; }

        public LeadQualification Qualification { get; set; }

        public DateTime? LastActivityAt { get; set; }

        public string? LastActivitySummary { get; set; }

        public DateTime? NextActionAt { get; set; }

        public string? NextActionSummary { get; set; }

        public DateTime? FirstContactAt { get; set; }

        public DateTime? LastContactAt { get; set; }

        public DateTime? ConvertedAt { get; set; }

        public int? ConvertedCustomerId { get; set; }

        public int? ConvertedBookingId { get; set; }

        public string? ConvertedBookingReference { get; set; }

        public int? ClosureReasonId { get; set; }

        public string? ClosureReasonName { get; set; }

        public string? ClosureNotes { get; set; }

        public DateTime? ClosedAt { get; set; }

        public DateTime? ReactivateOn { get; set; }

        public int? BookingRequestId { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        // Base64 RowVersion. Sent back with an edit so a form opened before a newer change
        // cannot silently overwrite it.
        public string ConcurrencyToken { get; set; } = string.Empty;

        public int OpenFollowUpCount { get; set; }

        public int DocumentCount { get; set; }
    }

    public class LeadListDto
    {
        public List<LeadResponseDto> Items { get; set; } = new();

        public int TotalCount { get; set; }

        public int Page { get; set; }

        public int PageSize { get; set; }

        public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
    }

    public class LeadFilterDto
    {
        public LeadStage? Stage { get; set; }

        public LeadAssignmentState? AssignmentState { get; set; }

        public LeadQualification? Qualification { get; set; }

        public int? LeadSourceId { get; set; }

        public int? AssignedEmployeeId { get; set; }

        public int? AssignedTeamId { get; set; }

        public int? ProjectId { get; set; }

        public string? CampaignName { get; set; }

        public LeadPaymentPreference? PaymentPreference { get; set; }

        public bool? Unassigned { get; set; }

        /// <summary>Only leads whose next action is already past due.</summary>
        public bool? OverdueOnly { get; set; }

        /// <summary>Open leads with no activity during the configured inactivity window.</summary>
        public bool? InactiveOnly { get; set; }

        public int? UnitId { get; set; }

        public DateTime? CreatedFrom { get; set; }

        public DateTime? CreatedTo { get; set; }

        public string? SearchTerm { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 20;

        /// <summary>createdat | lastactivity | nextaction | stage.</summary>
        public string SortBy { get; set; } = "createdat";

        public bool SortDescending { get; set; } = true;
    }

    public class LeadActivityDto
    {
        public int Id { get; set; }

        public int LeadId { get; set; }

        public LeadActivityType Type { get; set; }

        public string Summary { get; set; } = string.Empty;

        public string? Notes { get; set; }

        public LeadCommunicationChannel? Channel { get; set; }

        public string? PreviousValue { get; set; }

        public string? NewValue { get; set; }

        public int? CommunicationId { get; set; }

        public int? FollowUpId { get; set; }

        public int? SiteVisitId { get; set; }

        public int? DocumentId { get; set; }

        public int? CommentId { get; set; }

        public int? BookingId { get; set; }

        public int? CustomerId { get; set; }

        public int? PerformedByUserId { get; set; }

        public string? PerformedByName { get; set; }

        public bool IsSystemGenerated { get; set; }

        public DateTime OccurredAt { get; set; }
    }
}
