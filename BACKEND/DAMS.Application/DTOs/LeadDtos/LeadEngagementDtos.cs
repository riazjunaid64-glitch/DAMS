using System.ComponentModel.DataAnnotations;
using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.LeadDtos
{
    public class RecordLeadCommunicationDto
    {
        [Required]
        public LeadCommunicationChannel Channel { get; set; }

        [Required]
        public LeadCommunicationDirection Direction { get; set; }

        public DateTime? OccurredAt { get; set; }

        /// <summary>
        /// False when the customer could not be reached. A failed attempt is still recorded,
        /// but it does not count as first contact.
        /// </summary>
        public bool Connected { get; set; } = true;

        [Required]
        [StringLength(2000, MinimumLength = 2)]
        public string Summary { get; set; } = string.Empty;

        [StringLength(2000)]
        public string? CustomerResponse { get; set; }

        [StringLength(500)]
        public string? NextAction { get; set; }

        public DateTime? NextActionAt { get; set; }

        // Set only by channel integrations replaying provider messages.
        [StringLength(50)]
        public string? ExternalProvider { get; set; }

        [StringLength(200)]
        public string? ExternalMessageId { get; set; }
    }

    public class LeadCommunicationDto
    {
        public int Id { get; set; }

        public int LeadId { get; set; }

        public LeadCommunicationChannel Channel { get; set; }

        public LeadCommunicationDirection Direction { get; set; }

        public DateTime OccurredAt { get; set; }

        public int? EmployeeId { get; set; }

        public string? EmployeeName { get; set; }

        public string Summary { get; set; } = string.Empty;

        public string? CustomerResponse { get; set; }

        public string? NextAction { get; set; }

        public DateTime? NextActionAt { get; set; }

        public string? ExternalProvider { get; set; }

        public DateTime CreatedAt { get; set; }

        public List<LeadDocumentDto> Attachments { get; set; } = new();
    }

    public class CreateLeadCommentDto
    {
        [Required]
        [StringLength(4000, MinimumLength = 1)]
        public string Body { get; set; } = string.Empty;

        public int? ParentCommentId { get; set; }

        /// <summary>User ids of colleagues tagged on this comment. Mentions grant them lead access.</summary>
        public List<int> MentionedUserIds { get; set; } = new();

        public bool IsManagerReviewRequest { get; set; }

        public bool IsDecisionRecord { get; set; }
    }

    public class LeadCommentDto
    {
        public int Id { get; set; }

        public int LeadId { get; set; }

        public int? ParentCommentId { get; set; }

        public string Body { get; set; } = string.Empty;

        public bool IsManagerReviewRequest { get; set; }

        public bool IsDecisionRecord { get; set; }

        public int AuthorUserId { get; set; }

        public string? AuthorName { get; set; }

        public List<LeadMentionDto> Mentions { get; set; } = new();

        public DateTime CreatedAt { get; set; }
    }

    public class LeadMentionDto
    {
        public int UserId { get; set; }

        public string? Name { get; set; }
    }

    public class CreateLeadFollowUpDto
    {
        public LeadFollowUpType Type { get; set; } = LeadFollowUpType.FollowUp;

        /// <summary>Defaults to the lead's assigned employee.</summary>
        public int? AssignedEmployeeId { get; set; }

        [Required]
        [StringLength(200, MinimumLength = 2)]
        public string Title { get; set; } = string.Empty;

        [StringLength(1000)]
        public string? Notes { get; set; }

        [Required]
        public DateTime DueAt { get; set; }

        public DateTime? RemindAt { get; set; }

        public TaskPriority Priority { get; set; } = TaskPriority.Medium;
    }

    public class CompleteLeadFollowUpDto
    {
        [Required]
        [StringLength(1000, MinimumLength = 2)]
        public string Outcome { get; set; } = string.Empty;

        /// <summary>Optional next follow-up to schedule immediately after this one closes.</summary>
        public DateTime? NextFollowUpAt { get; set; }

        [StringLength(200)]
        public string? NextFollowUpTitle { get; set; }
    }

    public class RescheduleLeadFollowUpDto
    {
        [Required]
        public DateTime DueAt { get; set; }

        public DateTime? RemindAt { get; set; }

        [Required]
        [StringLength(500, MinimumLength = 3)]
        public string Reason { get; set; } = string.Empty;
    }

    public class LeadFollowUpDto
    {
        public int Id { get; set; }

        public int LeadId { get; set; }

        public string LeadReference { get; set; } = string.Empty;

        public string LeadName { get; set; } = string.Empty;

        public LeadFollowUpType Type { get; set; }

        public int AssignedEmployeeId { get; set; }

        public string? AssignedEmployeeName { get; set; }

        public string Title { get; set; } = string.Empty;

        public string? Notes { get; set; }

        public DateTime DueAt { get; set; }

        public DateTime? RemindAt { get; set; }

        public TaskPriority Priority { get; set; }

        public LeadFollowUpStatus Status { get; set; }

        public DateTime? CompletedAt { get; set; }

        public string? Outcome { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    public class ScheduleSiteVisitDto
    {
        public int? ProjectId { get; set; }

        public int? UnitId { get; set; }

        /// <summary>Defaults to the lead's assigned employee.</summary>
        public int? AssignedEmployeeId { get; set; }

        [Required]
        public DateTime ScheduledAt { get; set; }

        [Required]
        [StringLength(300, MinimumLength = 3)]
        public string MeetingLocation { get; set; } = string.Empty;

        [StringLength(500)]
        public string? CustomerAttendees { get; set; }

        [StringLength(500)]
        public string? InternalAttendees { get; set; }

        public DateTime? RemindAt { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }
    }

    public class RescheduleSiteVisitDto
    {
        [Required]
        public DateTime ScheduledAt { get; set; }

        [StringLength(300)]
        public string? MeetingLocation { get; set; }

        public DateTime? RemindAt { get; set; }

        [Required]
        [StringLength(500, MinimumLength = 3)]
        public string Reason { get; set; } = string.Empty;
    }

    public class CompleteSiteVisitDto
    {
        [Required]
        public LeadSiteVisitOutcome Outcome { get; set; }

        [Required]
        [StringLength(500, MinimumLength = 3)]
        public string NextAction { get; set; } = string.Empty;

        [StringLength(1000)]
        public string? OutcomeNotes { get; set; }

        [StringLength(1000)]
        public string? CustomerFeedback { get; set; }
    }

    public class CloseSiteVisitDto
    {
        [Required]
        [StringLength(500, MinimumLength = 3)]
        public string Reason { get; set; } = string.Empty;
    }

    public class LeadSiteVisitDto
    {
        public int Id { get; set; }

        public int LeadId { get; set; }

        public string LeadReference { get; set; } = string.Empty;

        public string LeadName { get; set; } = string.Empty;

        public int? ProjectId { get; set; }

        public string? ProjectName { get; set; }

        public int? UnitId { get; set; }

        public string? UnitNumber { get; set; }

        public int AssignedEmployeeId { get; set; }

        public string? AssignedEmployeeName { get; set; }

        public DateTime ScheduledAt { get; set; }

        public string MeetingLocation { get; set; } = string.Empty;

        public string? CustomerAttendees { get; set; }

        public string? InternalAttendees { get; set; }

        public LeadSiteVisitStatus Status { get; set; }

        public DateTime? RemindAt { get; set; }

        public string? Notes { get; set; }

        public LeadSiteVisitOutcome? Outcome { get; set; }

        public string? OutcomeNotes { get; set; }

        public string? CustomerFeedback { get; set; }

        public string? NextAction { get; set; }

        public DateTime? CompletedAt { get; set; }

        public DateTime? OriginalScheduledAt { get; set; }

        public int RescheduleCount { get; set; }

        public string? CancellationReason { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    public class LeadDocumentDto
    {
        public int Id { get; set; }

        public int LeadId { get; set; }

        public int? CommunicationId { get; set; }

        public LeadDocumentCategory Category { get; set; }

        public string FileName { get; set; } = string.Empty;

        public string ContentType { get; set; } = string.Empty;

        public long FileSize { get; set; }

        public string? Description { get; set; }

        public int? UploadedByUserId { get; set; }

        public string? UploadedByName { get; set; }

        public DateTime UploadedAt { get; set; }
    }

    // Lead alerts are read through the central notification inbox (/api/notifications),
    // filtered by category, so there is no lead-only notification DTO any more.
}
