using DAMS.Application.Common;
using DAMS.Application.DTOs.CommissionRebateDtos;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Domain.Enums;

namespace DAMS.Application.Interfaces
{
    public interface ICommissionBookingLifecycle
    {
        Task HandleBookingCancelledAsync(int bookingId, string reason, FinancialWorkflowActor actor,
            CancellationToken cancellationToken = default);
    }

    public interface ICommissionRebateService : ICommissionBookingLifecycle
    {
        Task<CommissionRebateSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);
        Task<PagedResult<ThirdPartyPartnerDto>> GetPartnersAsync(string? search, bool? isActive, int skip, int take,
            CancellationToken cancellationToken = default);
        Task<ThirdPartyPartnerDto> CreatePartnerAsync(SaveThirdPartyPartnerDto dto, FinancialWorkflowActor actor,
            CancellationToken cancellationToken = default);
        Task<ThirdPartyPartnerDto> UpdatePartnerAsync(int id, SaveThirdPartyPartnerDto dto, FinancialWorkflowActor actor,
            CancellationToken cancellationToken = default);
        Task<ThirdPartyPartnerDto> SetPartnerStatusAsync(int id, SetPartnerStatusDto dto, FinancialWorkflowActor actor,
            CancellationToken cancellationToken = default);
        Task<ThirdPartyAttributionDto> SaveAttributionAsync(int? id, SaveThirdPartyAttributionDto dto,
            FinancialWorkflowActor actor, CancellationToken cancellationToken = default);
        Task<PagedResult<CommissionRuleDto>> GetRulesAsync(bool? isActive, int skip, int take, CancellationToken cancellationToken = default);
        Task<CommissionRuleDto> CreateRuleAsync(SaveCommissionRuleDto dto, FinancialWorkflowActor actor,
            CancellationToken cancellationToken = default);
        Task<CommissionRuleDto> UpdateRuleAsync(int id, SaveCommissionRuleDto dto, FinancialWorkflowActor actor,
            CancellationToken cancellationToken = default);
        Task<PagedResult<BookingCommissionDto>> GetCommissionsAsync(BookingCommissionStatus? status, int? partnerId,
            int? projectId, int skip, int take, CancellationToken cancellationToken = default);
        Task<PagedResult<CustomerRebateDto>> GetRebatesAsync(CustomerRebateStatus? status, int? projectId,
            int skip, int take, CancellationToken cancellationToken = default);
        Task<BookingCommissionRebateWorkspaceDto> GetBookingWorkspaceAsync(int bookingId,
            CancellationToken cancellationToken = default);
        Task<PagedResult<FinancialAuditDto>> GetBookingAuditAsync(int bookingId, int skip, int take,
            CancellationToken cancellationToken = default);
        Task<BookingCommissionRebateWorkspaceDto> CreateCommissionAsync(int bookingId, CreateBookingCommissionDto dto,
            FinancialWorkflowActor actor, CancellationToken cancellationToken = default);
        Task<BookingCommissionRebateWorkspaceDto> ChangeCommissionStatusAsync(int bookingId, int commissionId,
            CommissionStatusChangeDto dto, FinancialWorkflowActor actor, CancellationToken cancellationToken = default);
        Task<BookingCommissionRebateWorkspaceDto> RecordPayoutAsync(int bookingId, int commissionId,
            RecordCommissionPayoutDto dto, FinancialWorkflowActor actor, CancellationToken cancellationToken = default);
        Task<BookingCommissionRebateWorkspaceDto> ReversePayoutAsync(int bookingId, int commissionId, int payoutId,
            ReverseMoneyMovementDto dto, FinancialWorkflowActor actor, CancellationToken cancellationToken = default);
        Task<BookingCommissionRebateWorkspaceDto> CreateRebateAsync(int bookingId, CreateCustomerRebateDto dto,
            FinancialWorkflowActor actor, CancellationToken cancellationToken = default);
        Task<BookingCommissionRebateWorkspaceDto> ChangeRebateStatusAsync(int bookingId, int rebateId,
            RebateStatusChangeDto dto, FinancialWorkflowActor actor, CancellationToken cancellationToken = default);
        Task<BookingCommissionRebateWorkspaceDto> RecordRebateDisbursementAsync(int bookingId, int rebateId,
            RecordRebateDisbursementDto dto, FinancialWorkflowActor actor, CancellationToken cancellationToken = default);
        Task<BookingCommissionRebateWorkspaceDto> ReverseRebateDisbursementAsync(int bookingId, int rebateId,
            int disbursementId, ReverseMoneyMovementDto dto, FinancialWorkflowActor actor,
            CancellationToken cancellationToken = default);
        Task<FinancialEvidenceDto> UploadEvidenceAsync(FinancialEvidenceOwnerType ownerType, int ownerId,
            FinancialEvidenceUpload upload, FinancialWorkflowActor actor, CancellationToken cancellationToken = default);
        Task<FinancialEvidenceDownload> DownloadEvidenceAsync(int evidenceId, FinancialWorkflowActor actor,
            CancellationToken cancellationToken = default);
    }
}
