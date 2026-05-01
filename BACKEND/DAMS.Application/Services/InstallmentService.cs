using DAMS.Application.DTOs.InstallmentDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public class InstallmentService : IInstallmentService
    {
        private readonly AppDbContext _context;

        public InstallmentService(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Create installment with validation on due date
        /// </summary>
        public async Task<InstallmentResponseDto> CreateInstallmentAsync(CreateInstallmentRequestDto request)
        {
            // Validate due date is in the future
            if (request.DueDate <= DateTime.UtcNow)
                throw new InvalidOperationException("Installment due date must be in the future.");

            // Validate amount
            if (request.Amount <= 0)
                throw new InvalidOperationException("Installment amount must be greater than 0.");

            // Get booking to validate
            var booking = await _context.Bookings
                .Include(b => b.Installments)
                .FirstOrDefaultAsync(b => b.Id == request.BookingId && !b.IsDeleted);

            if (booking == null)
                throw new InvalidOperationException($"Booking with ID {request.BookingId} not found.");

            // Rule: Total of all installments must equal remaining amount
            var currentTotal = booking.Installments.Sum(i => i.Amount);
            var newTotal = currentTotal + request.Amount;

            if (newTotal > booking.RemainingAmount)
                throw new InvalidOperationException(
                    $"Total installments ({newTotal}) exceed remaining amount ({booking.RemainingAmount}).");

            // Create installment
            var installment = new Installment
            {
                BookingId = request.BookingId,
                DueDate = request.DueDate,
                Amount = request.Amount,
                Status = InstallmentStatus.Pending,
                PaidDate = null
            };

            _context.Installments.Add(installment);
            await _context.SaveChangesAsync();

            return MapToResponseDto(installment);
        }

        /// <summary>
        /// Get all installments for a booking
        /// </summary>
        public async Task<List<InstallmentResponseDto>> GetBookingInstallmentsAsync(int bookingId)
        {
            var installments = await _context.Installments
                .Where(i => i.BookingId == bookingId)
                .OrderBy(i => i.DueDate)
                .ToListAsync();

            return installments.Select(i => MapToResponseDto(i)).ToList();
        }

        /// <summary>
        /// Get installment by ID
        /// </summary>
        public async Task<InstallmentResponseDto?> GetInstallmentByIdAsync(int installmentId)
        {
            var installment = await _context.Installments
                .FirstOrDefaultAsync(i => i.Id == installmentId);

            return installment == null ? null : MapToResponseDto(installment);
        }

        /// <summary>
        /// Get pending (unpaid) installments for a booking
        /// </summary>
        public async Task<List<InstallmentResponseDto>> GetPendingInstallmentsAsync(int bookingId)
        {
            var installments = await _context.Installments
                .Where(i => i.BookingId == bookingId && i.Status != InstallmentStatus.Paid)
                .OrderBy(i => i.DueDate)
                .ToListAsync();

            return installments.Select(i => MapToResponseDto(i)).ToList();
        }

        /// <summary>
        /// Get overdue installments (due date passed and not paid)
        /// </summary>
        public async Task<List<InstallmentResponseDto>> GetOverdueInstallmentsAsync()
        {
            var today = DateTime.UtcNow;

            var installments = await _context.Installments
                .Where(i => i.DueDate < today && i.Status != InstallmentStatus.Paid)
                .OrderBy(i => i.DueDate)
                .ToListAsync();

            return installments.Select(i => MapToResponseDto(i)).ToList();
        }

        /// <summary>
        /// Validate total of all installments equals remaining amount
        /// </summary>
        public async Task<bool> ValidateInstallmentTotalAsync(int bookingId)
        {
            var booking = await _context.Bookings
                .Include(b => b.Installments)
                .FirstOrDefaultAsync(b => b.Id == bookingId && !b.IsDeleted);

            if (booking == null)
                return false;

            var totalInstallments = booking.Installments.Sum(i => i.Amount);
            return totalInstallments == booking.RemainingAmount;
        }

        /// <summary>
        /// Map installment entity to DTO with calculated fields
        /// </summary>
        private InstallmentResponseDto MapToResponseDto(Installment installment)
        {
            var today = DateTime.UtcNow;
            var isOverdue = installment.DueDate < today && installment.Status != InstallmentStatus.Paid;

            return new InstallmentResponseDto
            {
                Id = installment.Id,
                BookingId = installment.BookingId,
                DueDate = installment.DueDate,
                Amount = installment.Amount,
                Status = installment.Status,
                PaidDate = installment.PaidDate,
                IsOverdue = isOverdue
            };
        }
    }
}
