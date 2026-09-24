using DAMS.Application.Common;
using DAMS.Application.DTOs.BookingRequestDtos;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace DAMS.Application.Services
{
    /// <summary>
    /// The website enquiry form. Since lead management, a submitted request is an enquiry —
    /// not a held unit and not a confirmed booking. Each request is backed by a Lead that
    /// carries the sales workflow; the request row remains the customer-facing record of
    /// what they submitted, so existing "My requests" screens keep working unchanged.
    /// </summary>
    public class BookingRequestService : IBookingRequestService
    {
        private readonly AppDbContext _context;
        private readonly ILeadService _leadService;
        private readonly INotificationEventService? _notifications;

        /// <param name="notifications">
        /// Optional: an enquiry must be accepted whether or not the notification platform is
        /// available, so every call into it happens after the request is committed.
        /// </param>
        public BookingRequestService(AppDbContext context, ILeadService leadService, INotificationEventService? notifications = null)
        {
            _context = context;
            _leadService = leadService;
            _notifications = notifications;
        }

        private async Task NotifyQuietlyAsync(Func<INotificationEventService, Task> action)
        {
            if (_notifications == null)
                return;

            try
            {
                await action(_notifications);
            }
            catch (Exception)
            {
                // A notification problem must never fail an enquiry or a review decision.
            }
        }

        public async Task<BookingRequestResponseDto> CreateBookingRequestAsync(CreateBookingRequestDto dto, int? userId)
        {
            var unit = await _context.Units
                .Include(u => u.Project)
                .FirstOrDefaultAsync(u => u.Id == dto.UnitId);

            if (unit == null)
                throw new InvalidOperationException("Unit not found.");

            // A unit that is already sold or on a payment plan cannot take new enquiries;
            // an available unit can take as many as it likes — every enquirer is a lead.
            if (unit.Status is not (UnitStatus.Available or UnitStatus.PendingReview))
                throw new InvalidOperationException("This unit is no longer available for enquiries.");

            // Different people may all enquire about the same unit, but the same signed-in
            // person submitting the same unit twice is a double-click, not a second enquiry.
            if (userId.HasValue)
            {
                var alreadyPending = await _context.BookingRequests.AnyAsync(
                    br => br.UnitId == dto.UnitId
                          && br.UserId == userId.Value
                          && br.Status == BookingRequestStatus.Pending);

                if (alreadyPending)
                    throw new InvalidOperationException("You already have a pending request for this unit.");
            }

            var bookingRequest = new BookingRequest
            {
                UnitId = dto.UnitId,
                // From the caller's token, resolved by the controller — never from the payload.
                // This is the value the whole ownership chain is later rebuilt from, so the
                // moment it can be influenced by what somebody typed, it stops being proof of
                // anything. Everything else on this row, the email included, is contact detail
                // the enquirer supplied about themselves and nothing decides access from it.
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

            await RunInTransactionAsync(async () =>
            {
                // The lead needs the request id for its external reference, so the request is
                // written first and the two are linked inside the same transaction.
                _context.BookingRequests.Add(bookingRequest);
                await _context.SaveChangesAsync();

                await _leadService.EnsureLeadForBookingRequestAsync(bookingRequest, actor: null);
                await _context.SaveChangesAsync();
            });

            await NotifyQuietlyAsync(n => n.NotifyBookingRequestReceivedAsync(bookingRequest.Id));

            return await MapToResponseAsync(bookingRequest.Id)
                ?? throw new InvalidOperationException("Created booking request could not be loaded.");
        }

        public async Task<BookingRequestResponseDto?> GetBookingRequestByIdAsync(int id)
        {
            return await MapToResponseAsync(id);
        }

        public async Task<BookingRequestListDto> GetBookingRequestsAsync(BookingRequestFilterDto filter)
        {
            var query = _context.BookingRequests
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

            if (filter.RequestedFrom.HasValue)
                query = query.Where(br => br.RequestedAt >= filter.RequestedFrom.Value);

            if (filter.RequestedTo.HasValue)
                query = query.Where(br => br.RequestedAt < filter.RequestedTo.Value);

            var totalCount = await query.CountAsync();
            var page = Math.Max(1, filter.Page);
            var pageSize = Math.Clamp(filter.PageSize, 1, 100);

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
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(Projection)
                .ToListAsync();

            return new BookingRequestListDto
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }

        public async Task<List<BookingRequestResponseDto>> GetMyBookingRequestsAsync(int userId)
        {
            // Single projected query instead of one round-trip per request id (N+1).
            return await _context.BookingRequests
                .AsNoTracking()
                .Where(br => br.UserId == userId)
                .OrderByDescending(br => br.RequestedAt)
                .Select(Projection)
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

            var adminContext = new LeadUserContext
            {
                UserId = adminUserId,
                Role = LeadRoles.Admin,
                DisplayName = await _context.Users
                    .Where(u => u.UserId == adminUserId)
                    .Select(u => u.FullName)
                    .FirstOrDefaultAsync()
            };

            await RunInTransactionAsync(async () =>
            {
                // Requests submitted before lead management have no lead yet.
                var leadId = await _leadService.EnsureLeadForBookingRequestAsync(bookingRequest, adminContext)
                    ?? throw new InvalidOperationException(
                        "This request's contact details match more than one open lead, so it is waiting in Leads → " +
                        "Held enquiries. Choose the lead it belongs to there, then approve it.");
                await _context.SaveChangesAsync();

                // Approval is the conversion: it is what creates the customer and booking,
                // records who did it, and marks the lead Won. Nothing else may set Won.
                var conversion = await _leadService.ConvertAsync(leadId, new ConvertLeadDto
                {
                    BookingRequestId = bookingRequest.Id,
                    UnitId = bookingRequest.UnitId,
                    CNIC = bookingRequest.CNIC,
                    Notes = $"Approved from website booking request #{bookingRequest.Id}."
                }, adminContext);

                var request = await _context.BookingRequests.FirstAsync(br => br.Id == bookingRequestId);
                request.Status = BookingRequestStatus.Approved;
                request.ReviewedAt = DateTime.UtcNow;
                request.ReviewedByUserId = adminUserId;
                request.CustomerId = conversion.CustomerId;
                request.LeadId = leadId;
                request.UpdatedAt = DateTime.UtcNow;

                var booking = await _context.Bookings.FirstOrDefaultAsync(b => b.Id == conversion.BookingId);
                if (booking == null || (booking.BookingRequestId.HasValue && booking.BookingRequestId != request.Id))
                    throw new InvalidOperationException("The converted booking belongs to another request.");
                booking.BookingRequestId = request.Id;

                await _context.SaveChangesAsync();
            });

            var approved = await MapToResponseAsync(bookingRequestId)
                ?? throw new InvalidOperationException("Approved booking request could not be loaded.");

            var newBookingId = await _context.Bookings
                .AsNoTracking()
                .Where(b => b.BookingRequestId == bookingRequestId)
                .Select(b => (int?)b.Id)
                .FirstOrDefaultAsync();

            if (newBookingId.HasValue)
                await NotifyQuietlyAsync(n => n.NotifyBookingStatusAsync(
                    newBookingId.Value, NotificationType.BookingApproved, null, adminUserId));

            return approved;
        }

        public async Task<BookingRequestResponseDto> RejectBookingRequestAsync(int bookingRequestId, int adminUserId, string? rejectionReason)
        {
            var bookingRequest = await _context.BookingRequests
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

            await _context.SaveChangesAsync();

            await NotifyQuietlyAsync(n => n.NotifyBookingRequestRejectedAsync(bookingRequestId, rejectionReason, adminUserId));

            return await MapToResponseAsync(bookingRequestId)
                ?? throw new InvalidOperationException("Rejected booking request could not be loaded.");
        }

        public async Task<BookingRequestResponseDto> CancelBookingRequestAsync(int bookingRequestId, int userId)
        {
            var bookingRequest = await _context.BookingRequests
                .FirstOrDefaultAsync(br => br.Id == bookingRequestId);

            if (bookingRequest == null)
                throw new InvalidOperationException("Booking request not found.");

            if (bookingRequest.UserId != userId)
                throw new InvalidOperationException("You can only cancel your own booking requests.");

            if (bookingRequest.Status != BookingRequestStatus.Pending)
                throw new InvalidOperationException("Only pending booking requests can be cancelled.");

            bookingRequest.Status = BookingRequestStatus.Cancelled;
            bookingRequest.ReviewedAt = DateTime.UtcNow;
            bookingRequest.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return await MapToResponseAsync(bookingRequestId)
                ?? throw new InvalidOperationException("Cancelled booking request could not be loaded.");
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
                ["cancelled"] = stats.FirstOrDefault(s => s.Status == BookingRequestStatus.Cancelled)?.Count ?? 0,
                ["total"] = stats.Sum(s => s.Count)
            };

            return result;
        }

        private async Task RunInTransactionAsync(Func<Task> action)
        {
            if (_context.Database.CurrentTransaction != null)
            {
                await action();
                return;
            }

            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                await action();
                await transaction.CommitAsync();
            });
        }

        private static readonly System.Linq.Expressions.Expression<Func<BookingRequest, BookingRequestResponseDto>> Projection =
            br => new BookingRequestResponseDto
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
                LeadId = br.LeadId,
                CreatedAt = br.CreatedAt
            };

        private async Task<BookingRequestResponseDto?> MapToResponseAsync(int id)
        {
            return await _context.BookingRequests
                .AsNoTracking()
                .Where(br => br.Id == id)
                .Select(Projection)
                .FirstOrDefaultAsync();
        }
    }
}
