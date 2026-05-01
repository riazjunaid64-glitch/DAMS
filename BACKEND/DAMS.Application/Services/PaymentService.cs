using DAMS.Application.DTOs.PaymentDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public class PaymentService : IPaymentService
    {
        private readonly AppDbContext _context;
        private readonly IBookingService _bookingService;

        public PaymentService(AppDbContext context, IBookingService bookingService)
        {
            _context = context;
            _bookingService = bookingService;
        }

        /// <summary>
        /// Process payment with comprehensive validation and booking status update
        /// </summary>
        public async Task<PaymentResponseDto> ProcessPaymentAsync(CreatePaymentRequestDto request)
        {
            // Validate amount
            if (request.Amount <= 0)
                throw new InvalidOperationException("Payment amount must be greater than 0.");

            // Get booking
            var booking = await _context.Bookings
                .Include(b => b.Installments)
                .FirstOrDefaultAsync(b => b.Id == request.BookingId && !b.IsDeleted);

            if (booking == null)
                throw new InvalidOperationException($"Booking with ID {request.BookingId} not found.");

            // Rule: Payment validation based on payment type
            if (request.InstallmentId.HasValue)
            {
                // Installment payment - InstallmentId required
                await ValidateInstallmentPaymentAsync(booking, request.InstallmentId.Value, request.Amount);
            }
            else
            {
                // Down payment - only allowed once and no installment needed
                var existingDownPayment = await _context.Payments
                    .Where(p => p.BookingId == request.BookingId 
                        && p.InstallmentId == null 
                        && !p.IsDeleted)
                    .FirstOrDefaultAsync();

                if (existingDownPayment != null)
                    throw new InvalidOperationException("Down payment already processed for this booking.");
            }

            // Rule: Total payments must not exceed total booking price
            if (!await ValidatePaymentAmountAsync(request.BookingId, request.Amount))
                throw new InvalidOperationException(
                    $"Payment amount exceeds remaining balance. Remaining: {booking.TotalPrice - booking.AmountPaid}");

            // Create payment record
            var payment = new Payment
            {
                BookingId = request.BookingId,
                InstallmentId = request.InstallmentId,
                Amount = request.Amount,
                PaymentDate = DateTime.UtcNow,
                PaymentMethod = request.PaymentMethod,
                PaymentReference = request.PaymentReference,
                IsDeleted = false
            };

            // If installment payment, mark installment as Paid
            if (request.InstallmentId.HasValue)
            {
                var installment = booking.Installments
                    .FirstOrDefault(i => i.Id == request.InstallmentId.Value);

                if (installment != null && installment.Amount == request.Amount)
                {
                    installment.Status = InstallmentStatus.Paid;
                    installment.PaidDate = DateTime.UtcNow;
                    _context.Installments.Update(installment);
                }
            }

            // Update booking's AmountPaid
            booking.AmountPaid += request.Amount;

            _context.Payments.Add(payment);
            _context.Bookings.Update(booking);
            await _context.SaveChangesAsync();

            if (booking.AmountPaid >= booking.TotalPrice)
            {
                await _bookingService.CompleteBookingIfPaidAsync(booking.Id);
            }

            return MapToResponseDto(payment);
        }

        /// <summary>
        /// Get all payments for a booking
        /// </summary>
        public async Task<List<PaymentResponseDto>> GetBookingPaymentsAsync(int bookingId)
        {
            var payments = await _context.Payments
                .Where(p => p.BookingId == bookingId && !p.IsDeleted)
                .OrderByDescending(p => p.PaymentDate)
                .ToListAsync();

            return payments.Select(p => MapToResponseDto(p)).ToList();
        }

        /// <summary>
        /// Get payment by ID
        /// </summary>
        public async Task<PaymentResponseDto?> GetPaymentByIdAsync(int paymentId)
        {
            var payment = await _context.Payments
                .FirstOrDefaultAsync(p => p.Id == paymentId && !p.IsDeleted);

            return payment == null ? null : MapToResponseDto(payment);
        }

        /// <summary>
        /// Calculate total amount paid for a booking
        /// </summary>
        public async Task<decimal> GetTotalPaidAsync(int bookingId)
        {
            return await _context.Payments
                .Where(p => p.BookingId == bookingId && !p.IsDeleted)
                .SumAsync(p => p.Amount);
        }

        /// <summary>
        /// Validate payment amount doesn't exceed booking total
        /// </summary>
        public async Task<bool> ValidatePaymentAmountAsync(int bookingId, decimal paymentAmount)
        {
            var booking = await _context.Bookings
                .FirstOrDefaultAsync(b => b.Id == bookingId && !b.IsDeleted);

            if (booking == null)
                return false;

            var totalPaid = await GetTotalPaidAsync(bookingId);
            return (totalPaid + paymentAmount) <= booking.TotalPrice;
        }

        /// <summary>
        /// Get down payments for a booking
        /// </summary>
        public async Task<List<PaymentResponseDto>> GetDownPaymentsAsync(int bookingId)
        {
            var downPayments = await _context.Payments
                .Where(p => p.BookingId == bookingId 
                    && p.InstallmentId == null 
                    && !p.IsDeleted)
                .OrderByDescending(p => p.PaymentDate)
                .ToListAsync();

            return downPayments.Select(p => MapToResponseDto(p)).ToList();
        }

        /// <summary>
        /// Validate installment payment
        /// </summary>
        private async Task ValidateInstallmentPaymentAsync(Booking booking, int installmentId, decimal amount)
        {
            var installment = await _context.Installments
                .FirstOrDefaultAsync(i => i.Id == installmentId && i.BookingId == booking.Id);

            if (installment == null)
                throw new InvalidOperationException($"Installment with ID {installmentId} not found for this booking.");

            // Rule: Cannot pay an installment already marked as Paid
            if (installment.Status == InstallmentStatus.Paid)
                throw new InvalidOperationException("This installment has already been paid.");

            // Payment amount should match installment amount
            if (amount != installment.Amount)
                throw new InvalidOperationException(
                    $"Payment amount ({amount}) does not match installment amount ({installment.Amount}).");
        }

        /// <summary>
        /// Map payment entity to DTO
        /// </summary>
        private PaymentResponseDto MapToResponseDto(Payment payment)
        {
            return new PaymentResponseDto
            {
                Id = payment.Id,
                BookingId = payment.BookingId,
                InstallmentId = payment.InstallmentId,
                Amount = payment.Amount,
                PaymentDate = payment.PaymentDate,
                PaymentMethod = payment.PaymentMethod,
                PaymentReference = payment.PaymentReference,
                IsDownPayment = payment.InstallmentId == null
            };
        }
    }
}
