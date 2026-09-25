using System.Security.Claims;
using DAMS.Application.Common;
using DAMS.Application.DTOs.IntegrationDtos;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Domain.Entities;

namespace DAMS.Application.Interfaces
{
    /// <summary>Resolves the acting staff member (role + employee record) from a token.</summary>
    public interface ILeadUserContextResolver
    {
        Task<LeadUserContext> ResolveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
    }

    public interface ILeadService
    {
        /// <summary>
        /// The single entry point every channel uses to create a lead. Applies duplicate
        /// detection and external-submission idempotency.
        /// </summary>
        /// <param name="trustedExternal">
        /// Set only by a verified provider or internal system flow. Client-supplied provider
        /// fields never make a staff intake replay or enrich an external submission.
        /// </param>
        Task<LeadIntakeResultDto> IngestAsync(
            LeadIntakeDto dto,
            LeadUserContext? actor,
            bool trustedExternal = false,
            CancellationToken cancellationToken = default);

        Task<LeadResponseDto?> GetByIdAsync(int id, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<LeadListDto> GetLeadsAsync(LeadFilterDto filter, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<List<LeadActivityDto>> GetTimelineAsync(int leadId, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<List<LeadAssignmentHistoryDto>> GetAssignmentHistoryAsync(int leadId, LeadUserContext ctx, CancellationToken cancellationToken = default);

        /// <summary>
        /// The immutable provider receipts behind this lead, each with every answer the person
        /// gave — including the ones DAMS has no field for.
        /// </summary>
        Task<List<LeadExternalSubmissionDto>> GetExternalSubmissionsAsync(int leadId, LeadUserContext ctx, CancellationToken cancellationToken = default);

        /// <summary>
        /// The provider's original data behind one of those receipts, and the webhook event that
        /// delivered it. Admins and managers only, and only for a lead they can already see.
        /// </summary>
        Task<LeadExternalSubmissionRawDto> GetExternalSubmissionRawAsync(int leadId, int submissionId, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<LeadResponseDto> UpdateAsync(int id, UpdateLeadDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<LeadResponseDto> AssignAsync(int id, AssignLeadDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<LeadResponseDto> ChangeStageAsync(int id, ChangeLeadStageDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<LeadResponseDto> UpdateQualificationAsync(int id, UpdateLeadQualificationDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<LeadResponseDto> CloseAsync(int id, bool dormant, CloseLeadDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<LeadResponseDto> ReopenAsync(int id, ReopenLeadDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default);

        Task<LeadConversionResultDto> ConvertAsync(int id, ConvertLeadDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates leads for historical booking requests that predate lead management.
        /// Repeatable: requests already linked to a lead are skipped.
        /// </summary>
        Task<LeadBackfillResultDto> BackfillFromBookingRequestsAsync(LeadUserContext ctx, CancellationToken cancellationToken = default);

        /// <summary>
        /// Guarantees the website request has a lead behind it, creating one through the
        /// shared ingestion pipeline when it does not. Sets <c>request.LeadId</c>; the caller
        /// saves. Returns the lead id.
        /// </summary>
        /// <summary>The request's lead, or null while its enquiry waits in the held-enquiry review
        /// because its details match more than one open lead.</summary>
        Task<int?> EnsureLeadForBookingRequestAsync(BookingRequest request, LeadUserContext? actor, CancellationToken cancellationToken = default);

        /// <summary>Closes the held enquiry of a booking request that was rejected or cancelled.
        /// Staged only; the caller's save of the request writes it.</summary>
        Task CloseIntakeHoldsForBookingRequestAsync(int bookingRequestId, int? userId, string reason, CancellationToken cancellationToken = default);

        Task<LeadIntakeHoldListDto> GetIntakeHoldsAsync(LeadUserContext ctx, CancellationToken cancellationToken = default);

        /// <summary>Adds a held enquiry to the chosen lead and returns it, or dismisses the enquiry and returns null.</summary>
        Task<LeadResponseDto?> ResolveIntakeHoldAsync(int holdId, ResolveLeadIntakeHoldDto dto, LeadUserContext ctx, CancellationToken cancellationToken = default);

        /// <summary>
        /// Closes a lead on the system's behalf — used when the outcome is decided outside
        /// the lead workspace, such as a customer withdrawing their website request. Silently
        /// does nothing if the lead is already closed, so callers stay idempotent.
        /// </summary>
        Task CloseFromSystemAsync(int leadId, bool dormant, string reasonCode, string summary, string? notes, int? actingUserId, CancellationToken cancellationToken = default);
    }
}
