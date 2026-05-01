using DAMS.Application.DTOs.InstallmentDtos;

namespace DAMS.Application.Interfaces
{
    public interface IInstallmentService
    {
        /// <summary>
        /// Create installment plan with validation:
        /// - Due date must be in the future
        /// - Total of all installments must equal booking.RemainingAmount
        /// </summary>
        Task<InstallmentResponseDto> CreateInstallmentAsync(CreateInstallmentRequestDto request);

        /// <summary>
        /// Get all installments for a booking
        /// </summary>
        Task<List<InstallmentResponseDto>> GetBookingInstallmentsAsync(int bookingId);

        /// <summary>
        /// Get installment by ID
        /// </summary>
        Task<InstallmentResponseDto?> GetInstallmentByIdAsync(int installmentId);

        /// <summary>
        /// Get pending (unpaid) installments for a booking
        /// </summary>
        Task<List<InstallmentResponseDto>> GetPendingInstallmentsAsync(int bookingId);

        /// <summary>
        /// Get overdue installments (due date passed and not paid)
        /// </summary>
        Task<List<InstallmentResponseDto>> GetOverdueInstallmentsAsync();

        /// <summary>
        /// Validate total of all installments equals remaining amount
        /// </summary>
        Task<bool> ValidateInstallmentTotalAsync(int bookingId);
    }
}
