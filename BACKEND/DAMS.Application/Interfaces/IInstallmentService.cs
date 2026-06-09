using DAMS.Application.DTOs.InstallmentDtos;

namespace DAMS.Application.Interfaces
{
    public interface IInstallmentService
    {
        Task<InstallmentScheduleDto> GetScheduleAsync(int bookingId);

        Task<InstallmentScheduleDto> GenerateScheduleAsync(int bookingId, GenerateInstallmentPlanDto dto, int adminUserId);

        Task<InstallmentScheduleDto> RecordInstallmentPaymentAsync(int bookingId, int installmentId, RecordInstallmentPaymentDto dto, int adminUserId);
    }
}
