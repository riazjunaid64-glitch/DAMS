using DAMS.Application.Common;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;

namespace DAMS.Application.Interfaces
{
    public interface ILeadCommunicationService
    {
        Task<LeadCommunicationDto> RecordAsync(int leadId, RecordLeadCommunicationDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<List<LeadCommunicationDto>> GetForLeadAsync(int leadId, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<LeadCommentDto> AddCommentAsync(int leadId, CreateLeadCommentDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<List<LeadCommentDto>> GetCommentsAsync(int leadId, LeadUserContext ctx, CancellationToken cancellationToken = default);
    }

    public interface ILeadFollowUpService
    {
        Task<LeadFollowUpDto> CreateAsync(int leadId, CreateLeadFollowUpDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<LeadFollowUpDto> CompleteAsync(int followUpId, CompleteLeadFollowUpDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<LeadFollowUpDto> RescheduleAsync(int followUpId, RescheduleLeadFollowUpDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<LeadFollowUpDto> CancelAsync(int followUpId, string reason, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<List<LeadFollowUpDto>> GetForLeadAsync(int leadId, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<List<LeadFollowUpDto>> GetMyFollowUpsAsync(LeadUserContext ctx, bool overdueOnly, CancellationToken cancellationToken = default);
    }

    public interface ILeadSiteVisitService
    {
        Task<LeadSiteVisitDto> ScheduleAsync(int leadId, ScheduleSiteVisitDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<LeadSiteVisitDto> RescheduleAsync(int visitId, RescheduleSiteVisitDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<LeadSiteVisitDto> CompleteAsync(int visitId, CompleteSiteVisitDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<LeadSiteVisitDto> CancelAsync(int visitId, CloseSiteVisitDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<LeadSiteVisitDto> MarkMissedAsync(int visitId, CloseSiteVisitDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<List<LeadSiteVisitDto>> GetForLeadAsync(int leadId, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<List<LeadSiteVisitDto>> GetUpcomingAsync(LeadUserContext ctx, int days, CancellationToken cancellationToken = default);
    }

    public interface ILeadDocumentService
    {
        Task<LeadDocumentDto> UploadAsync(int leadId, LeadDocumentUpload upload, LeadDocumentCategory category, string? description, int? communicationId, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<List<LeadDocumentDto>> GetForLeadAsync(int leadId, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<LeadDocumentDownload> DownloadAsync(int documentId, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task DeleteAsync(int documentId, LeadUserContext ctx, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The lead module's doorway into the central notification platform. Reading a lead
    /// alert happens through the shared notification inbox, so this is write-only.
    /// </summary>
    public interface ILeadNotificationService
    {
        /// <summary>
        /// Queues a notification unless an identical one already exists. Returns true when a
        /// new row was added. Never saves — the caller's SaveChanges commits it with the
        /// rest of the operation, so a lead alert and the change that caused it are atomic.
        /// </summary>
        Task<bool> QueueAsync(int leadId, int recipientUserId, NotificationType type, string title, string? body, string dedupKey, bool isEscalation = false, CancellationToken cancellationToken = default);

        /// <summary>Notifies every admin and the owning manager(s) for a lead.</summary>
        Task<int> QueueForSupervisorsAsync(Lead lead, NotificationType type, string title, string? body, string dedupKeySuffix, bool isEscalation = false, CancellationToken cancellationToken = default);
    }

    /// <summary>Time-based alerts and escalations. Safe to run repeatedly.</summary>
    public interface ILeadAlertService
    {
        Task<LeadAlertScanResultDto> RunScanAsync(CancellationToken cancellationToken = default);
    }

    public interface ILeadConfigurationService
    {
        Task<List<LeadSourceDto>> GetSourcesAsync(bool includeInactive, CancellationToken cancellationToken = default);

        Task<LeadSourceDto> CreateSourceAsync(CreateLeadSourceDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<LeadSourceDto> UpdateSourceAsync(int id, UpdateLeadSourceDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<List<LeadClosureReasonDto>> GetClosureReasonsAsync(bool includeInactive, LeadClosureReasonKind? kind, CancellationToken cancellationToken = default);

        Task<LeadClosureReasonDto> CreateClosureReasonAsync(CreateLeadClosureReasonDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<LeadClosureReasonDto> UpdateClosureReasonAsync(int id, UpdateLeadClosureReasonDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<List<TeamDto>> GetTeamsAsync(CancellationToken cancellationToken = default);

        Task<TeamDto> CreateTeamAsync(SaveTeamDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<TeamDto> UpdateTeamAsync(int id, SaveTeamDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default);
    }

    public interface ILeadReportingService
    {
        Task<EmployeeLeadDashboardDto> GetEmployeeDashboardAsync(LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<ManagerLeadDashboardDto> GetManagerDashboardAsync(LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<AdminLeadDashboardDto> GetAdminDashboardAsync(LeadUserContext ctx, DateTime? from, DateTime? to, CancellationToken cancellationToken = default);
    }

    /// <summary>Private storage for lead documents. Keys are opaque, never URLs.</summary>
    public interface ILeadDocumentStorage
    {
        Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default);

        Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default);

        Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default);
    }
}
