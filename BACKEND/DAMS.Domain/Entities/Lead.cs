using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>
    /// A potential buyer, whatever channel they arrived through. Every enquiry becomes a
    /// Lead first; Customers and Bookings are created only when the lead converts.
    /// </summary>
    public class Lead
    {
        public int Id { get; set; }

        // Human-readable identifier for staff (e.g. LD-000123).
        public string LeadReference { get; set; } = string.Empty;

        // ── Person ──
        public string FirstName { get; set; } = string.Empty;

        public string? LastName { get; set; }

        public string Phone { get; set; } = string.Empty;

        // Digits-only copies used for duplicate detection and indexed lookups. Written by
        // the application whenever the display value changes.
        public string NormalizedPhone { get; set; } = string.Empty;

        public string? WhatsappNumber { get; set; }

        public string? NormalizedWhatsapp { get; set; }

        public string? Email { get; set; }

        public string? NormalizedEmail { get; set; }

        public string? Address { get; set; }

        public string? City { get; set; }

        public LeadContactMethod PreferredContactMethod { get; set; } = LeadContactMethod.Phone;

        public string? PreferredContactTime { get; set; }

        // ── Origin ──
        public int LeadSourceId { get; set; }

        public string? SourceDetails { get; set; }

        public string? CampaignName { get; set; }

        public string? CampaignReference { get; set; }

        public string? AdReference { get; set; }

        // ── Integration metadata (Meta, portals, telephony, ...) ──
        public string? ExternalProvider { get; set; }

        public string? ExternalLeadId { get; set; }

        public string? ExternalFormReference { get; set; }

        public DateTime? ExternalSubmittedAt { get; set; }

        // Raw provider payload kept verbatim as JSON for troubleshooting and replay.
        public string? IntegrationPayload { get; set; }

        public LeadIntegrationStatus IntegrationStatus { get; set; } = LeadIntegrationStatus.NotApplicable;

        public string? IntegrationError { get; set; }

        // ── Interest ──
        public int? InterestedProjectId { get; set; }

        public int? InterestedUnitId { get; set; }

        public string? PropertyType { get; set; }

        public string? PreferredLocation { get; set; }

        public decimal? BudgetMin { get; set; }

        public decimal? BudgetMax { get; set; }

        public LeadPurchaseIntent PurchaseIntent { get; set; } = LeadPurchaseIntent.Unknown;

        public string? Notes { get; set; }

        // ── Ownership ──
        public int? AssignedEmployeeId { get; set; }

        public int? AssignedTeamId { get; set; }

        public LeadAssignmentState AssignmentState { get; set; } = LeadAssignmentState.Unassigned;

        public DateTime? AssignedAt { get; set; }

        public int? AssignedByUserId { get; set; }

        // ── Pipeline ──
        public LeadStage Stage { get; set; } = LeadStage.New;

        public LeadQualification Qualification { get; set; } = LeadQualification.Unqualified;

        public DateTime? LastActivityAt { get; set; }

        public string? LastActivitySummary { get; set; }

        public DateTime? NextActionAt { get; set; }

        public string? NextActionSummary { get; set; }

        public DateTime? FirstContactAt { get; set; }

        public DateTime? LastContactAt { get; set; }

        // ── Outcome ──
        public DateTime? ConvertedAt { get; set; }

        public int? ConvertedByUserId { get; set; }

        public int? ConvertedCustomerId { get; set; }

        public int? ConvertedBookingId { get; set; }

        public int? ClosureReasonId { get; set; }

        public string? ClosureNotes { get; set; }

        public DateTime? ClosedAt { get; set; }

        // Optional date a dormant lead should be picked up again.
        public DateTime? ReactivateOn { get; set; }

        public int? CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        // Guards against two concurrent stage/assignment/conversion updates overwriting
        // one another, exactly as Booking does for money.
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        // Navigation
        public LeadSource Source { get; set; } = null!;

        public LeadClosureReason? ClosureReason { get; set; }

        public Employee? AssignedEmployee { get; set; }

        public Team? AssignedTeam { get; set; }

        public Project? InterestedProject { get; set; }

        public Unit? InterestedUnit { get; set; }

        public Customer? ConvertedCustomer { get; set; }

        public Booking? ConvertedBooking { get; set; }

        public ICollection<LeadActivity> Activities { get; set; } = new List<LeadActivity>();

        public ICollection<LeadAssignmentHistory> AssignmentHistory { get; set; } = new List<LeadAssignmentHistory>();

        public ICollection<LeadCommunication> Communications { get; set; } = new List<LeadCommunication>();

        public ICollection<LeadFollowUp> FollowUps { get; set; } = new List<LeadFollowUp>();

        public ICollection<LeadSiteVisit> SiteVisits { get; set; } = new List<LeadSiteVisit>();

        public ICollection<LeadDocument> Documents { get; set; } = new List<LeadDocument>();

        public ICollection<LeadComment> Comments { get; set; } = new List<LeadComment>();
    }
}
