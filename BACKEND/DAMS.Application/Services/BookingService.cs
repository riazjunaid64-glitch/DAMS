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

        public async Task<BookingResponseDto> CreateBookingAsync(int clientUserId, CreateBookingRequestDto dto)
        {
            if (dto.DownPaymentAmount <= 0)
                throw new Exception("Down payment amount must be greater than zero.");

            var unit = await _context.Units
                .Include(u => u.Project)
                .FirstOrDefaultAsync(u => u.Id == dto.UnitId);
            if (unit == null)
                throw new Exception("Unit not found.");

            var statusNormalized = unit.Status.Trim();
            if (!string.Equals(statusNormalized, "Available", StringComparison.OrdinalIgnoreCase))
                throw new Exception("This unit is not available for booking.");

            var existing = await _context.Bookings
                .AnyAsync(b => b.UnitId == dto.UnitId && b.Status != BookingStatus.Cancelled);
            if (existing)
                throw new Exception("This unit already has an active booking.");

            if (dto.DownPaymentAmount > unit.Price)
                throw new Exception("Down payment cannot exceed unit price.");

            var booking = new Booking
            {
                ClientId = clientUserId,
                UnitId = dto.UnitId,
                Status = BookingStatus.Pending,
                BookingDate = DateTime.UtcNow,
                UnitPriceAtBooking = unit.Price,
                DownPaymentAmount = dto.DownPaymentAmount
            };

            _context.Bookings.Add(booking);

            var downPayment = new Payment
            {
                Booking = booking,
                InstallmentId = null,
                Amount = dto.DownPaymentAmount,
                PaymentMethod = dto.DownPaymentMethod,
                PaymentReference = string.IsNullOrWhiteSpace(dto.PaymentReference)
                    ? null
                    : dto.PaymentReference.Trim(),
                PaidAt = DateTime.UtcNow
            };
            _context.Payments.Add(downPayment);

            unit.Status = "Booked";
            unit.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return await MapBookingAsync(booking.Id)
                ?? throw new Exception("Failed to load booking after create.");
        }

        public async Task<List<BookingResponseDto>> GetBookingsForClientAsync(int clientUserId)
        {
            var ids = await _context.Bookings
                .AsNoTracking()
                .Where(b => b.ClientId == clientUserId)
                .OrderByDescending(b => b.BookingDate)
                .Select(b => b.Id)
                .ToListAsync();

            var list = new List<BookingResponseDto>();
            foreach (var id in ids)
            {
                var mapped = await MapBookingAsync(id);
                if (mapped != null)
                    list.Add(mapped);
            }

            return list;
        }

        private async Task<BookingResponseDto?> MapBookingAsync(int bookingId)
        {
            var row = await _context.Bookings
                .AsNoTracking()
                .Include(b => b.Unit).ThenInclude(u => u.Project)
                .Include(b => b.Payments)
                .FirstOrDefaultAsync(b => b.Id == bookingId);
            if (row == null)
                return null;

            return new BookingResponseDto
            {
                Id = row.Id,
                ClientId = row.ClientId,
                UnitId = row.UnitId,
                UnitNumber = row.Unit.UnitNumber,
                ProjectId = row.Unit.ProjectId,
                ProjectName = row.Unit.Project.ProjectName,
                Status = row.Status,
                BookingDate = row.BookingDate,
                UnitPriceAtBooking = row.UnitPriceAtBooking,
                DownPaymentAmount = row.DownPaymentAmount,
                Payments = row.Payments
                    .OrderByDescending(p => p.PaidAt)
                    .Select(p => new BookingPaymentResponseDto
                    {
                        Id = p.Id,
                        Amount = p.Amount,
                        PaymentMethod = p.PaymentMethod,
                        PaymentReference = p.PaymentReference,
                        PaidAt = p.PaidAt,
                        InstallmentId = p.InstallmentId
                    })
                    .ToList()
            };
        }
    }
}
