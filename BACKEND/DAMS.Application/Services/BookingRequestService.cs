using DAMS.Application.DTOs.BookingRequestDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public class BookingRequestService : IBookingRequestService
    {
        private readonly AppDbContext _context;
        private readonly ICustomerService _customerService;
        private readonly IBookingService _bookingService;

        public BookingRequestService(
            AppDbContext context,
            ICustomerService customerService,
            IBookingService bookingService)
        {
            _context = context;
            _customerService = customerService;
            _bookingService = bookingService;
        }

        public async Task<BookingRequestResponseDto> CreateBookingRequestAsync(CreateBookingRequestDto dto, int? userId)
        {
            var unit = await _context.Units
                .Include(u => u.Project)
                .FirstOrDefaultAsync(u => u.Id == dto.UnitId);

            if (unit == null)
                throw new InvalidOperationException("Unit not found.");

            if (unit.Status != UnitStatus.Available)
                throw new InvalidOperationException("This unit is not available for booking requests.");

            var existingPending = await _context.BookingRequests
                .AnyAsync(br => br.UnitId == dto.UnitId && br.Status == BookingRequestStatus.Pending);

            if (existingPending)
                throw new InvalidOperationException("This unit already has a pending booking request.");

            var bookingRequest = new BookingRequest
            {
                UnitId = dto.UnitId,
                UserId = userId,
                FullName = dto.FullName.Trim(),
                Phone = dto.Phone.Trim(),
                Email = dto.Email.Trim().ToLowerInvariant(),
                CNIC = dto.CNIC.Trim(),
                Address = dto.Address.Trim(),
                Notes = dto.Notes?.Trim(),
                Status = BookingRequestStatus.Pending,
                RequestedAt = DateTime.UtcNow
            };

            _context.BookingRequests.Add(bookingRequest);

            unit.Status = UnitStatus.PendingReview;
            unit.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return await MapToResponseAsync(bookingRequest.Id);
        }

        public async Task<BookingRequestResponseDto?> GetBookingRequestByIdAsync(int id)
        {
            var exists = await _context.BookingRequests.AnyAsync(br => br.Id == id);
            if (!exists) return null;

            return await MapToResponseAsync(id);
        }

        public async Task<BookingRequestListDto> GetBookingRequestsAsync(BookingRequestFilterDto filter)
        {
            var query = _context.BookingRequests
                .Include(br => br.Unit)
                    .ThenInclude(u => u.Project)
                .AsNoTracking()
                .AsQueryable();

            if (filter.Status.HasValue)
                query = query.Where(br => br.Status == filter.Status.Value);

            if (filter.ProjectId.HasValue)
                query = query.Where(br => br.Unit.ProjectId == filter.ProjectId.Value);

            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                var term = filter.SearchTerm.Trim().ToLower();
                query = query.Where(br =>
                    br.FullName.ToLower().Contains(term) ||
                    br.Email.ToLower().Contains(term) ||
                    br.Phone.Contains(term) ||
                    br.CNIC.Contains(term) ||
                    br.Unit.UnitNumber.ToLower().Contains(term) ||
                    br.Unit.Project.ProjectName.ToLower().Contains(term));
            }

            var totalCount = await query.CountAsync();

            query = filter.SortBy.ToLower() switch
            {
                "requestedat" => filter.SortDescending
                    ? query.OrderByDescending(br => br.RequestedAt)
                    : query.OrderBy(br => br.RequestedAt),
                "fullname" => filter.SortDescending
                    ? query.OrderByDescending(br => br.FullName)
                    : query.OrderBy(br => br.FullName),
                "status" => filter.SortDescending
                    ? query.OrderByDescending(br => br.Status)
                    : query.OrderBy(br => br.Status),
                _ => query.OrderByDescending(br => br.RequestedAt)
            };

            var items = await query
                .Skip((filter.Page - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .Select(br => new BookingRequestResponseDto
                {
                    Id = br.Id,
                    UnitId = br.UnitId,
                    UnitNumber = br.Unit.UnitNumber,
                    UnitType = br.Unit.UnitType,
                    UnitPrice = br.Unit.Price,
                    ProjectId = br.Unit.ProjectId,
                    ProjectName = br.Unit.Project.ProjectName,
                    ProjectLocation = br.Unit.Project.Location,
                    UserId = br.UserId,
                    FullName = br.FullName,
                    Phone = br.Phone,
                    Email = br.Email,
                    CNIC = br.CNIC,
                    Address = br.Address,
                    Notes = br.Notes,
                    Status = br.Status,
                    RequestedAt = br.RequestedAt,
                    ReviewedAt = br.ReviewedAt,
                    ReviewedByUserId = br.ReviewedByUserId,
                    RejectionReason = br.RejectionReason,
                    CreatedAt = br.CreatedAt
                })
                .ToListAsync();

            return new BookingRequestListDto
            {
                Items = items,
                TotalCount = totalCount,
                Page = filter.Page,
                PageSize = filter.PageSize
            };
        }

        public async Task<List<BookingRequestResponseDto>> GetMyBookingRequestsAsync(int userId)
        {
            // Single projected query instead of one round-trip per request id (N+1).
            return await _context.BookingRequests
                .AsNoTracking()
                .Where(br => br.UserId == userId)
                .OrderByDescending(br => br.RequestedAt)
                .Select(br => new BookingRequestResponseDto
                {
                    Id = br.Id,
                    UnitId = br.UnitId,
                    UnitNumber = br.Unit.UnitNumber,
                    UnitType = br.Unit.UnitType,
                    UnitPrice = br.Unit.Price,
                    ProjectId = br.Unit.ProjectId,
                    ProjectName = br.Unit.Project.ProjectName,
                    ProjectLocation = br.Unit.Project.Location,
                    UserId = br.UserId,
                    FullName = br.FullName,
                    Phone = br.Phone,
                    Email = br.Email,
                    CNIC = br.CNIC,
                    Address = br.Address,
                    Notes = br.Notes,
                    Status = br.Status,
                    RequestedAt = br.RequestedAt,
                    ReviewedAt = br.ReviewedAt,
                    ReviewedByUserId = br.ReviewedByUserId,
                    ReviewedByName = br.ReviewedBy != null ? br.ReviewedBy.FullName : null,
                    RejectionReason = br.RejectionReason,
                    CreatedAt = br.CreatedAt
                })
                .ToListAsync();
        }

        public async Task<BookingRequestResponseDto> ApproveBookingRequestAsync(int bookingRequestId, int adminUserId)
        {
            var bookingRequest = await _context.BookingRequests
                .Include(br => br.Unit)
                .FirstOrDefaultAsync(br => br.Id == bookingRequestId);

            if (bookingRequest == null)
                throw new InvalidOperationException("Booking request not found.");

            if (bookingRequest.Status != BookingRequestStatus.Pending)
                throw new InvalidOperationException("Only pending booking requests can be approved.");

            // Find or create the business customer from the request contact details.
            var customerId = await _customerService.FindOrCreateCustomerAsync(
                bookingRequest.FullName,
                bookingRequest.Phone,
                bookingRequest.CNIC,
                bookingRequest.Email,
                bookingRequest.Address,
                CustomerSource.Website,
                "Created from website booking request.",
                adminUserId);

            bookingRequest.Status = BookingRequestStatus.Approved;
            bookingRequest.ReviewedAt = DateTime.UtcNow;
            bookingRequest.ReviewedByUserId = adminUserId;
            bookingRequest.CustomerId = customerId;
            bookingRequest.UpdatedAt = DateTime.UtcNow;

            // Creates the Booking (Awaiting Booking Amount) and moves the unit to Reserved.
            await _bookingService.CreateBookingForApprovedRequestAsync(bookingRequest, customerId, adminUserId);

            // Persist approval fields in case booking creation did not flush them.
            await _context.SaveChangesAsync();

            return await MapToResponseAsync(bookingRequestId);
        }

        public async Task<BookingRequestResponseDto> RejectBookingRequestAsync(int bookingRequestId, int adminUserId, string? rejectionReason)
        {
            var bookingRequest = await _context.BookingRequests
                .Include(br => br.Unit)
                .FirstOrDefaultAsync(br => br.Id == bookingRequestId);

            if (bookingRequest == null)
                throw new InvalidOperationException("Booking request not found.");

            if (bookingRequest.Status != BookingRequestStatus.Pending)
                throw new InvalidOperationException("Only pending booking requests can be rejected.");

            bookingRequest.Status = BookingRequestStatus.Rejected;
            bookingRequest.ReviewedAt = DateTime.UtcNow;
            bookingRequest.ReviewedByUserId = adminUserId;
            bookingRequest.RejectionReason = rejectionReason?.Trim();
            bookingRequest.UpdatedAt = DateTime.UtcNow;

            bookingRequest.Unit.Status = UnitStatus.Available;
            bookingRequest.Unit.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return await MapToResponseAsync(bookingRequestId);
        }

        public async Task<bool> HasPendingRequestForUnitAsync(int unitId)
        {
            return await _context.BookingRequests
                .AnyAsync(br => br.UnitId == unitId && br.Status == BookingRequestStatus.Pending);
        }

        public async Task<Dictionary<string, int>> GetBookingRequestStatsAsync()
        {
            var stats = await _context.BookingRequests
                .GroupBy(br => br.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            var result = new Dictionary<string, int>
            {
                ["pending"] = stats.FirstOrDefault(s => s.Status == BookingRequestStatus.Pending)?.Count ?? 0,
                ["approved"] = stats.FirstOrDefault(s => s.Status == BookingRequestStatus.Approved)?.Count ?? 0,
                ["rejected"] = stats.FirstOrDefault(s => s.Status == BookingRequestStatus.Rejected)?.Count ?? 0,
                ["total"] = stats.Sum(s => s.Count)
            };

            return result;
        }

        private async Task<BookingRequestResponseDto> MapToResponseAsync(int id)
        {
            var br = await _context.BookingRequests
                .AsNoTracking()
                .Include(b => b.Unit)
                    .ThenInclude(u => u.Project)
                .Include(b => b.ReviewedBy)
                .FirstAsync(b => b.Id == id);

            return new BookingRequestResponseDto
            {
                Id = br.Id,
                UnitId = br.UnitId,
                UnitNumber = br.Unit.UnitNumber,
                UnitType = br.Unit.UnitType,
                UnitPrice = br.Unit.Price,
                ProjectId = br.Unit.ProjectId,
                ProjectName = br.Unit.Project.ProjectName,
                ProjectLocation = br.Unit.Project.Location,
                UserId = br.UserId,
                FullName = br.FullName,
                Phone = br.Phone,
                Email = br.Email,
                CNIC = br.CNIC,
                Address = br.Address,
                Notes = br.Notes,
                Status = br.Status,
                RequestedAt = br.RequestedAt,
                ReviewedAt = br.ReviewedAt,
                ReviewedByUserId = br.ReviewedByUserId,
                ReviewedByName = br.ReviewedBy?.FullName,
                RejectionReason = br.RejectionReason,
                CreatedAt = br.CreatedAt
            };
        }
    }
}
