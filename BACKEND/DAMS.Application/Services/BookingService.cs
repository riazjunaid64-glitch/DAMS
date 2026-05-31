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
        private readonly ICustomerService _customerService;

        public BookingService(AppDbContext context, ICustomerService customerService)
        {
            _context = context;
            _customerService = customerService;
        }

        public async Task<BookingResponseDto> CreateBookingAsync(CreateBookingDto dto, int adminUserId)
        {
            var unit = await _context.Units
                .Include(u => u.Project)
                .FirstOrDefaultAsync(u => u.Id == dto.UnitId);

            if (unit == null)
                throw new InvalidOperationException("Unit not found.");

            if (unit.Status != UnitStatus.Available)
                throw new InvalidOperationException("This unit is not available for booking.");

            await EnsureNoActiveBookingAsync(unit.Id);

            int customerId;
            if (dto.CustomerId.HasValue)
            {
                var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == dto.CustomerId.Value);
                if (customer == null)
                    throw new InvalidOperationException("Customer not found.");
                if (customer.Status == CustomerStatus.Blocked)
                    throw new InvalidOperationException("This customer is blocked and cannot be booked.");
                customerId = customer.Id;
            }
            else if (dto.NewCustomer != null)
            {
                customerId = await _customerService.FindOrCreateCustomerAsync(
                    dto.NewCustomer.FullName,
                    dto.NewCustomer.Phone,
                    dto.NewCustomer.CNIC,
                    dto.NewCustomer.Email,
                    dto.NewCustomer.Address,
                    dto.Source,
                    dto.NewCustomer.SourceNotes,
                    adminUserId);
            }
            else
            {
                throw new InvalidOperationException("Provide an existing customer id or new customer details.");
            }

            var agreedSalePrice = dto.AgreedSalePrice ?? unit.Price;
            var discount = dto.DiscountAmount ?? 0m;
            var bookingAmountRequired = dto.BookingAmountRequired ?? 0m;

            var booking = new Booking
            {
                CustomerId = customerId,
                UnitId = unit.Id,
                BookingRequestId = null,
                Source = dto.Source,
                AssignedSalesUserId = dto.AssignedSalesUserId,
                Status = BookingStatus.AwaitingBookingAmount,
                ListPrice = unit.Price,
                AgreedSalePrice = agreedSalePrice,
                DiscountAmount = discount,
                DiscountReason = string.IsNullOrWhiteSpace(dto.DiscountReason) ? null : dto.DiscountReason.Trim(),
                BookingAmountRequired = bookingAmountRequired,
                BookingAmountReceived = 0m,
                TotalInstallmentAmount = agreedSalePrice - bookingAmountRequired,
                BookingDate = DateTime.UtcNow,
                BookingAmountDueDate = dto.BookingAmountDueDate,
                CustomerNotes = string.IsNullOrWhiteSpace(dto.CustomerNotes) ? null : dto.CustomerNotes.Trim(),
                InternalNotes = string.IsNullOrWhiteSpace(dto.InternalNotes) ? null : dto.InternalNotes.Trim(),
                CreatedByUserId = adminUserId,
                CreatedAt = DateTime.UtcNow
            };

            await PersistNewBookingAsync(booking, unit);

            return await GetResponseAsync(booking.Id);
        }

        public async Task<BookingResponseDto> CreateBookingForApprovedRequestAsync(BookingRequest request, int customerId, int adminUserId)
        {
            var unit = await _context.Units
                .Include(u => u.Project)
                .FirstOrDefaultAsync(u => u.Id == request.UnitId);

            if (unit == null)
                throw new InvalidOperationException("Unit not found.");

            await EnsureNoActiveBookingAsync(unit.Id);

            var booking = new Booking
            {
                CustomerId = customerId,
                UnitId = unit.Id,
                BookingRequestId = request.Id,
                Source = CustomerSource.Website,
                Status = BookingStatus.AwaitingBookingAmount,
                ListPrice = unit.Price,
                AgreedSalePrice = unit.Price,
                DiscountAmount = 0m,
                BookingAmountRequired = 0m,
                BookingAmountReceived = 0m,
                TotalInstallmentAmount = unit.Price,
                BookingDate = DateTime.UtcNow,
                CustomerNotes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                CreatedByUserId = adminUserId,
                CreatedAt = DateTime.UtcNow
            };

            await PersistNewBookingAsync(booking, unit);

            return await GetResponseAsync(booking.Id);
        }

        public async Task<BookingResponseDto?> GetBookingByIdAsync(int id)
        {
            var exists = await _context.Bookings.AnyAsync(b => b.Id == id);
            if (!exists) return null;
            return await GetResponseAsync(id);
        }

        public async Task<BookingListDto> GetBookingsAsync(BookingFilterDto filter)
        {
            var query = _context.Bookings
                .AsNoTracking()
                .Include(b => b.Customer)
                .Include(b => b.Unit).ThenInclude(u => u.Project)
                .AsQueryable();

            if (filter.Status.HasValue)
                query = query.Where(b => b.Status == filter.Status.Value);

            if (filter.ProjectId.HasValue)
                query = query.Where(b => b.Unit.ProjectId == filter.ProjectId.Value);

            if (filter.CustomerId.HasValue)
                query = query.Where(b => b.CustomerId == filter.CustomerId.Value);

            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                var term = filter.SearchTerm.Trim().ToLower();
                query = query.Where(b =>
                    b.BookingReference.ToLower().Contains(term) ||
                    b.Customer.FullName.ToLower().Contains(term) ||
                    b.Customer.Phone.Contains(term) ||
                    b.Unit.UnitNumber.ToLower().Contains(term) ||
                    b.Unit.Project.ProjectName.ToLower().Contains(term));
            }

            var totalCount = await query.CountAsync();

            var page = filter.Page < 1 ? 1 : filter.Page;
            var pageSize = filter.PageSize is < 1 or > 100 ? 20 : filter.PageSize;

            var entities = await query
                .OrderByDescending(b => b.BookingDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var items = entities.Select(MapProjection).ToList();

            return new BookingListDto
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }

        public async Task<BookingResponseDto> CancelBookingAsync(int id, string? reason, int adminUserId)
        {
            var booking = await _context.Bookings
                .Include(b => b.Unit)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null)
                throw new InvalidOperationException("Booking not found.");

            if (booking.Status == BookingStatus.Cancelled)
                throw new InvalidOperationException("Booking is already cancelled.");

            if (booking.Status is BookingStatus.PossessionGiven or BookingStatus.SaleCompleted)
                throw new InvalidOperationException("A booking that reached possession or completion cannot be cancelled here.");

            booking.Status = BookingStatus.Cancelled;
            booking.InternalNotes = AppendNote(booking.InternalNotes, reason, adminUserId);
            booking.UpdatedAt = DateTime.UtcNow;

            // Release the unit back to the market.
            booking.Unit.Status = UnitStatus.Available;
            booking.Unit.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return await GetResponseAsync(booking.Id);
        }

        private async Task EnsureNoActiveBookingAsync(int unitId)
        {
            var hasActive = await _context.Bookings
                .AnyAsync(b => b.UnitId == unitId && b.Status != BookingStatus.Cancelled);

            if (hasActive)
                throw new InvalidOperationException("This unit already has an active booking.");
        }

        private async Task PersistNewBookingAsync(Booking booking, Unit unit)
        {
            _context.Bookings.Add(booking);

            unit.Status = UnitStatus.Reserved;
            unit.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // Reference depends on the generated id.
            booking.BookingReference = $"BK-{booking.Id:D6}";
            await _context.SaveChangesAsync();
        }

        private async Task<BookingResponseDto> GetResponseAsync(int id)
        {
            var booking = await _context.Bookings
                .AsNoTracking()
                .Include(b => b.Customer)
                .Include(b => b.Unit).ThenInclude(u => u.Project)
                .FirstAsync(b => b.Id == id);

            return MapProjection(booking);
        }

        private static BookingResponseDto MapProjection(Booking b)
        {
            return new BookingResponseDto
            {
                Id = b.Id,
                BookingReference = b.BookingReference,
                CustomerId = b.CustomerId,
                CustomerName = b.Customer != null ? b.Customer.FullName : string.Empty,
                CustomerPhone = b.Customer != null ? b.Customer.Phone : string.Empty,
                UnitId = b.UnitId,
                UnitNumber = b.Unit != null ? b.Unit.UnitNumber : string.Empty,
                UnitType = b.Unit != null ? b.Unit.UnitType : string.Empty,
                ProjectId = b.Unit != null ? b.Unit.ProjectId : 0,
                ProjectName = b.Unit != null && b.Unit.Project != null ? b.Unit.Project.ProjectName : string.Empty,
                BookingRequestId = b.BookingRequestId,
                Source = b.Source,
                Status = b.Status,
                ListPrice = b.ListPrice,
                AgreedSalePrice = b.AgreedSalePrice,
                DiscountAmount = b.DiscountAmount,
                DiscountReason = b.DiscountReason,
                BookingAmountRequired = b.BookingAmountRequired,
                BookingAmountReceived = b.BookingAmountReceived,
                TotalInstallmentAmount = b.TotalInstallmentAmount,
                BookingDate = b.BookingDate,
                BookingAmountDueDate = b.BookingAmountDueDate,
                BookingAmountConfirmedDate = b.BookingAmountConfirmedDate,
                PossessionDate = b.PossessionDate,
                CompletionDate = b.CompletionDate,
                CustomerNotes = b.CustomerNotes,
                InternalNotes = b.InternalNotes,
                CreatedAt = b.CreatedAt,
                UpdatedAt = b.UpdatedAt
            };
        }

        private static string AppendNote(string? existing, string? reason, int adminUserId)
        {
            var entry = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC] Cancelled by user {adminUserId}" +
                        (string.IsNullOrWhiteSpace(reason) ? "." : $": {reason.Trim()}");
            return string.IsNullOrWhiteSpace(existing) ? entry : $"{existing}\n{entry}";
        }
    }
}
