using DAMS.Application.DTOs.BookingDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public class BookingService : IBookingService
    {
        private readonly AppDbContext _context;

        public BookingService(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Create a new booking with comprehensive validation
        /// </summary>
        public async Task<BookingResponseDto> CreateBookingAsync(CreateBookingRequestDto request)
        {
            // Validate down payment
            if (request.DownPayment <= 0)
                throw new InvalidOperationException("Down payment must be greater than 0.");

            // Get unit with validation
            var unit = await _context.Units
                .Include(u => u.Project)
                .FirstOrDefaultAsync(u => u.Id == request.UnitId);

            if (unit == null)
                throw new InvalidOperationException($"Unit with ID {request.UnitId} not found.");

            // Rule: Unit must be Available
            if (unit.Status != UnitStatus.Available)
                throw new InvalidOperationException($"Unit is not available. Current status: {unit.Status}");

            // Rule: Down payment must not exceed unit price
            if (request.DownPayment > unit.Price)
                throw new InvalidOperationException($"Down payment ({request.DownPayment}) cannot exceed unit price ({unit.Price}).");

            // Rule: Only one active booking allowed per unit
            var activeBooking = await _context.Bookings
                .Where(b => b.UnitId == request.UnitId 
                    && b.Status == BookingStatus.Active 
                    && !b.IsDeleted)
                .FirstOrDefaultAsync();

            if (activeBooking != null)
                throw new InvalidOperationException($"Unit already has an active booking (Booking ID: {activeBooking.Id}).");

            // Get client with validation
            var client = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == request.ClientId);

            if (client == null)
                throw new InvalidOperationException($"Client with ID {request.ClientId} not found.");

            // Create booking with price snapshot
            var booking = new Booking
            {
                ClientId = request.ClientId,
                UnitId = request.UnitId,
                TotalPrice = unit.Price,  // Snapshot current unit price
                DownPayment = request.DownPayment,
                RemainingAmount = unit.Price - request.DownPayment,
                AmountPaid = request.DownPayment,  // Down payment counts as paid
                BookingDate = DateTime.UtcNow,
                Status = BookingStatus.Active,
                IsDeleted = false
            };

            // Update unit status to Reserved
            unit.Status = UnitStatus.Reserved;
            unit.UpdatedAt = DateTime.UtcNow;

            // Save booking
            _context.Bookings.Add(booking);
            _context.Units.Update(unit);
            await _context.SaveChangesAsync();

            // Create payment record for down payment
            var downPaymentRecord = new Payment
            {
                BookingId = booking.Id,
                InstallmentId = null,  // Down payment has no installment
                Amount = request.DownPayment,
                PaymentDate = DateTime.UtcNow,
                PaymentMethod = PaymentMethod.Cash,  // Default to Cash for down payment
                IsDeleted = false
            };

            _context.Payments.Add(downPaymentRecord);
            await _context.SaveChangesAsync();

            return await MapToResponseDtoAsync(booking, client, unit);
        }

        /// <summary>
        /// Get booking with all related data
        /// </summary>
        public async Task<BookingResponseDto?> GetBookingByIdAsync(int bookingId)
        {
            var booking = await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Unit)
                .Where(b => b.Id == bookingId && !b.IsDeleted)
                .FirstOrDefaultAsync();

            if (booking == null)
                return null;

            return await MapToResponseDtoAsync(booking, booking.Client, booking.Unit);
        }

        /// <summary>
        /// Get all active and cancelled bookings for a client
        /// </summary>
        public async Task<List<BookingResponseDto>> GetClientBookingsAsync(int clientId)
        {
            var bookings = await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Unit)
                .Where(b => b.ClientId == clientId && !b.IsDeleted)
                .OrderByDescending(b => b.BookingDate)
                .ToListAsync();

            var result = new List<BookingResponseDto>();
            foreach (var booking in bookings)
            {
                result.Add(await MapToResponseDtoAsync(booking, booking.Client, booking.Unit));
            }

            return result;
        }

        /// <summary>
        /// Cancel booking and revert unit to Available
        /// </summary>
        public async Task<bool> CancelBookingAsync(int bookingId, string? reason = null)
        {
            var booking = await _context.Bookings
                .Include(b => b.Unit)
                .FirstOrDefaultAsync(b => b.Id == bookingId && !b.IsDeleted);

            if (booking == null)
                throw new InvalidOperationException($"Booking with ID {bookingId} not found.");

            if (booking.Status == BookingStatus.Cancelled)
                throw new InvalidOperationException("Booking is already cancelled.");

            // Optional: Cannot cancel if fully paid
            if (booking.AmountPaid >= booking.TotalPrice)
                throw new InvalidOperationException("Cannot cancel a fully paid booking. Please contact support.");

            // Mark booking as cancelled (soft delete)
            booking.Status = BookingStatus.Cancelled;
            booking.IsDeleted = true;
            booking.DeletedAt = DateTime.UtcNow;

            // Revert unit to Available
            booking.Unit.Status = UnitStatus.Available;
            booking.Unit.UpdatedAt = DateTime.UtcNow;

            _context.Bookings.Update(booking);
            _context.Units.Update(booking.Unit);
            await _context.SaveChangesAsync();

            return true;
        }

        /// <summary>
        /// Mark booking as Completed if total paid equals total price
        /// </summary>
        public async Task<bool> CompleteBookingIfPaidAsync(int bookingId)
        {
            var booking = await _context.Bookings
                .Include(b => b.Unit)
                .FirstOrDefaultAsync(b => b.Id == bookingId && !b.IsDeleted);

            if (booking == null)
                return false;

            // Check if fully paid
            if (booking.AmountPaid >= booking.TotalPrice)
            {
                booking.Status = BookingStatus.Completed;

                // Update unit status to Sold
                booking.Unit.Status = UnitStatus.Sold;
                booking.Unit.UpdatedAt = DateTime.UtcNow;

                _context.Bookings.Update(booking);
                _context.Units.Update(booking.Unit);
                await _context.SaveChangesAsync();

                return true;
            }

            return false;
        }

        /// <summary>
        /// Map booking entity to DTO with related data
        /// </summary>
        private Task<BookingResponseDto> MapToResponseDtoAsync(Booking booking, User client, Unit unit)
        {
            var dto = new BookingResponseDto
            {
                Id = booking.Id,
                ClientId = booking.ClientId,
                UnitId = booking.UnitId,
                TotalPrice = booking.TotalPrice,
                DownPayment = booking.DownPayment,
                RemainingAmount = booking.RemainingAmount,
                AmountPaid = booking.AmountPaid,
                BookingDate = booking.BookingDate,
                Status = booking.Status,
                ClientName = client.FullName,
                ClientEmail = client.Email,
                UnitNumber = unit.UnitNumber,
                UnitType = unit.UnitType,
                UnitPrice = unit.Price
            };

            return Task.FromResult(dto);
        }
    }
}
