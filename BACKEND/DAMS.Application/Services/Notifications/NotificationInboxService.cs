using DAMS.Application.Common;
using DAMS.Application.DTOs.NotificationDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services.Notifications
{
    /// <summary>
    /// A user's private inbox. Every query starts from the authenticated user id and the
    /// recipient is never taken from the request, so no identifier a caller can change leads
    /// to somebody else's notifications.
    /// </summary>
    public sealed class NotificationInboxService : INotificationInboxService
    {
        private readonly AppDbContext _context;
        private readonly TimeProvider _clock;

        public NotificationInboxService(AppDbContext context, TimeProvider clock)
        {
            _context = context;
            // Expiry is judged against the same clock the delivery worker uses, so a
            // notification never lingers in an inbox after it has stopped being sendable.
            _clock = clock;
        }

        public async Task<NotificationPageDto> GetAsync(
            NotificationUserContext ctx, NotificationFilterDto filter, CancellationToken cancellationToken = default)
        {
            var query = Mine(ctx);

            if (filter.Category.HasValue)
                query = query.Where(n => n.Category == filter.Category.Value);

            if (filter.UnreadOnly)
                query = query.Where(n => !n.IsRead);

            if (!filter.IncludeArchived)
                query = query.Where(n => !n.IsArchived);

            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var term = filter.Search.Trim().ToLower();
                query = query.Where(n => n.Title.ToLower().Contains(term) || n.Message.ToLower().Contains(term));
            }

            var page = filter.Page < 1 ? 1 : filter.Page;
            var pageSize = filter.PageSize is < 1 or > 100 ? 20 : filter.PageSize;

            var totalCount = await query.CountAsync(cancellationToken);

            var items = await query
                .OrderByDescending(n => n.CreatedAt)
                .ThenByDescending(n => n.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(Projection)
                .ToListAsync(cancellationToken);

            return new NotificationPageDto
            {
                Items = items,
                TotalCount = totalCount,
                UnreadCount = await UnreadQuery(ctx).CountAsync(cancellationToken),
                Page = page,
                PageSize = pageSize
            };
        }

        public async Task<NotificationSummaryDto> GetSummaryAsync(
            NotificationUserContext ctx, int take, CancellationToken cancellationToken = default)
        {
            var unread = UnreadQuery(ctx);

            var byCategory = await unread
                .GroupBy(n => n.Category)
                .Select(g => new { Category = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            var recent = await Mine(ctx)
                .Where(n => !n.IsArchived)
                .OrderByDescending(n => n.CreatedAt)
                .ThenByDescending(n => n.Id)
                .Take(Math.Clamp(take, 1, 50))
                .Select(Projection)
                .ToListAsync(cancellationToken);

            return new NotificationSummaryDto
            {
                UnreadCount = byCategory.Sum(x => x.Count),
                UnreadByCategory = byCategory.ToDictionary(x => x.Category.ToString(), x => x.Count),
                Recent = recent
            };
        }

        public Task<int> GetUnreadCountAsync(NotificationUserContext ctx, CancellationToken cancellationToken = default) =>
            UnreadQuery(ctx).CountAsync(cancellationToken);

        public async Task<NotificationDto> MarkReadAsync(
            int notificationId, NotificationUserContext ctx, CancellationToken cancellationToken = default)
        {
            var notification = await _context.Notifications
                .FirstOrDefaultAsync(n => n.Id == notificationId && n.RecipientUserId == ctx.UserId, cancellationToken)
                // Deliberately indistinguishable from "does not exist": probing ids must not
                // reveal that somebody else has a notification with that number.
                ?? throw new LeadNotFoundException("Notification not found.");

            if (!notification.IsRead)
            {
                notification.IsRead = true;
                notification.ReadAt = DateTime.UtcNow;
                notification.SeenAt ??= notification.ReadAt;
                await _context.SaveChangesAsync(cancellationToken);
            }

            return Map(notification);
        }

        public async Task<int> MarkAllReadAsync(
            NotificationUserContext ctx, NotificationCategory? category, CancellationToken cancellationToken = default)
        {
            var query = _context.Notifications
                .Where(n => n.RecipientUserId == ctx.UserId && !n.IsRead);

            if (category.HasValue)
                query = query.Where(n => n.Category == category.Value);

            var now = DateTime.UtcNow;
            var unread = await query.ToListAsync(cancellationToken);
            if (unread.Count == 0)
                return 0;

            foreach (var notification in unread)
            {
                notification.IsRead = true;
                notification.ReadAt = now;
                notification.SeenAt ??= now;
            }

            await _context.SaveChangesAsync(cancellationToken);
            return unread.Count;
        }

        public async Task ArchiveAsync(int notificationId, NotificationUserContext ctx, CancellationToken cancellationToken = default)
        {
            var notification = await _context.Notifications
                .FirstOrDefaultAsync(n => n.Id == notificationId && n.RecipientUserId == ctx.UserId, cancellationToken)
                ?? throw new LeadNotFoundException("Notification not found.");

            if (notification.IsArchived)
                return;

            notification.IsArchived = true;
            notification.ArchivedAt = DateTime.UtcNow;
            notification.IsRead = true;
            notification.ReadAt ??= notification.ArchivedAt;
            await _context.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// Resolves where a notification opens. The stored link is only a hint: access to the
        /// underlying record is re-checked now, because permissions, ownership and the record
        /// itself can all have changed since the notification was written.
        /// </summary>
        public async Task<NotificationOpenResult> OpenAsync(
            int notificationId, NotificationUserContext ctx, CancellationToken cancellationToken = default)
        {
            var notification = await _context.Notifications
                .FirstOrDefaultAsync(n => n.Id == notificationId && n.RecipientUserId == ctx.UserId, cancellationToken)
                ?? throw new LeadNotFoundException("Notification not found.");

            if (!notification.IsRead)
            {
                notification.IsRead = true;
                notification.ReadAt = DateTime.UtcNow;
                notification.SeenAt ??= notification.ReadAt;
                await _context.SaveChangesAsync(cancellationToken);
            }

            var dto = Map(notification);

            if (notification.EntityType == NotificationEntityType.None || notification.EntityId is null or <= 0)
            {
                return new NotificationOpenResult
                {
                    Allowed = true,
                    DeepLink = notification.DeepLink,
                    Message = "Opened.",
                    Notification = dto
                };
            }

            var access = await CheckEntityAccessAsync(notification, ctx, cancellationToken);

            return new NotificationOpenResult
            {
                Allowed = access.Allowed,
                DeepLink = access.Allowed ? notification.DeepLink : null,
                Message = access.Message,
                Notification = dto
            };
        }

        private async Task<(bool Allowed, string Message)> CheckEntityAccessAsync(
            Notification notification, NotificationUserContext ctx, CancellationToken cancellationToken)
        {
            const string gone = "The record this notification refers to is no longer available.";
            const string denied = "You no longer have permission to open this record.";
            var id = notification.EntityId!.Value;

            switch (notification.EntityType)
            {
                case NotificationEntityType.Lead:
                case NotificationEntityType.LeadFollowUp:
                case NotificationEntityType.LeadSiteVisit:
                case NotificationEntityType.LeadComment:
                {
                    // The link always lands on the lead workspace, so the lead is what has to
                    // be reachable — resolved through the lead module's own scope rules.
                    var leadId = notification.EntityType == NotificationEntityType.Lead
                        ? id
                        : await ResolveLeadIdAsync(notification, cancellationToken);

                    if (leadId is null or <= 0)
                        return (false, gone);

                    if (!ctx.IsStaff)
                        return (false, denied);

                    var leadCtx = await BuildLeadContextAsync(ctx, cancellationToken);
                    var visible = await LeadAccess.Scope(_context.Leads.AsNoTracking(), leadCtx)
                        .AnyAsync(l => l.Id == leadId.Value, cancellationToken);

                    return visible ? (true, "Opened.") : (false, denied);
                }

                case NotificationEntityType.Booking:
                case NotificationEntityType.Payment:
                case NotificationEntityType.Installment:
                {
                    var bookingId = notification.EntityType == NotificationEntityType.Booking
                        ? id
                        : ExtractBookingIdFromLink(notification.DeepLink);

                    if (bookingId is null or <= 0)
                        return (false, gone);

                    var booking = await _context.Bookings
                        .AsNoTracking()
                        .Where(b => b.Id == bookingId.Value)
                        .Select(b => new { b.Id, b.Customer.UserId })
                        .FirstOrDefaultAsync(cancellationToken);

                    if (booking == null)
                        return (false, gone);

                    // Admins run the finance desk; a customer may only open their own booking.
                    if (ctx.IsAdmin || booking.UserId == ctx.UserId)
                        return (true, "Opened.");

                    return (false, denied);
                }

                case NotificationEntityType.Customer:
                {
                    var customer = await _context.Customers
                        .AsNoTracking()
                        .Where(c => c.Id == id)
                        .Select(c => new { c.Id, c.UserId })
                        .FirstOrDefaultAsync(cancellationToken);

                    if (customer == null)
                        return (false, gone);

                    return ctx.IsAdmin || customer.UserId == ctx.UserId
                        ? (true, "Opened.")
                        : (false, denied);
                }

                case NotificationEntityType.BookingRequest:
                {
                    var request = await _context.BookingRequests
                        .AsNoTracking()
                        .Where(r => r.Id == id)
                        .Select(r => new { r.Id, r.UserId })
                        .FirstOrDefaultAsync(cancellationToken);

                    if (request == null)
                        return (false, gone);

                    return ctx.IsAdmin || request.UserId == ctx.UserId
                        ? (true, "Opened.")
                        : (false, denied);
                }

                case NotificationEntityType.EmployeeTask:
                {
                    var task = await _context.EmployeeTasks
                        .AsNoTracking()
                        .Where(t => t.Id == id)
                        .Select(t => new { t.Id, t.Employee.UserId })
                        .FirstOrDefaultAsync(cancellationToken);

                    if (task == null)
                        return (false, gone);

                    return ctx.IsAdmin || task.UserId == ctx.UserId
                        ? (true, "Opened.")
                        : (false, denied);
                }

                case NotificationEntityType.Project:
                    return await _context.Projects.AsNoTracking().AnyAsync(p => p.Id == id, cancellationToken)
                        ? (true, "Opened.")
                        : (false, gone);

                case NotificationEntityType.Unit:
                    return await _context.Units.AsNoTracking().AnyAsync(u => u.Id == id, cancellationToken)
                        ? (true, "Opened.")
                        : (false, gone);

                default:
                    return (true, "Opened.");
            }
        }

        private async Task<int?> ResolveLeadIdAsync(Notification notification, CancellationToken cancellationToken)
        {
            var id = notification.EntityId!.Value;
            return notification.EntityType switch
            {
                NotificationEntityType.LeadFollowUp => await _context.LeadFollowUps.AsNoTracking()
                    .Where(f => f.Id == id).Select(f => (int?)f.LeadId).FirstOrDefaultAsync(cancellationToken),
                NotificationEntityType.LeadSiteVisit => await _context.LeadSiteVisits.AsNoTracking()
                    .Where(v => v.Id == id).Select(v => (int?)v.LeadId).FirstOrDefaultAsync(cancellationToken),
                NotificationEntityType.LeadComment => await _context.LeadComments.AsNoTracking()
                    .Where(c => c.Id == id).Select(c => (int?)c.LeadId).FirstOrDefaultAsync(cancellationToken),
                _ => null
            };
        }

        /// <summary>Rebuilds the lead module's own authorisation context so the inbox reuses
        /// its scope rules rather than inventing a second, weaker set.</summary>
        private async Task<LeadUserContext> BuildLeadContextAsync(NotificationUserContext ctx, CancellationToken cancellationToken)
        {
            var employee = await _context.Employees
                .AsNoTracking()
                .Where(e => e.UserId == ctx.UserId)
                .Select(e => new { e.Id, e.TeamId })
                .FirstOrDefaultAsync(cancellationToken);

            var managedTeamIds = new List<int>();
            if (employee != null)
            {
                if (employee.TeamId.HasValue)
                    managedTeamIds.Add(employee.TeamId.Value);

                managedTeamIds.AddRange(await _context.Teams
                    .AsNoTracking()
                    .Where(t => t.ManagerEmployeeId == employee.Id)
                    .Select(t => t.Id)
                    .ToListAsync(cancellationToken));
            }

            return new LeadUserContext
            {
                UserId = ctx.UserId,
                Role = ctx.Role,
                DisplayName = ctx.DisplayName,
                EmployeeId = employee?.Id,
                TeamId = employee?.TeamId,
                ManagedTeamIds = managedTeamIds.Distinct().ToList()
            };
        }

        /// <summary>"/receipt/{bookingId}/{paymentId}" — the booking id is what authorises the view.</summary>
        private static int? ExtractBookingIdFromLink(string? deepLink)
        {
            if (string.IsNullOrWhiteSpace(deepLink))
                return null;

            var segments = deepLink.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2 && segments[0].Equals("receipt", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(segments[1], out var bookingId))
                return bookingId;

            if (segments.Length >= 2 && segments[0].Equals("confirmed-bookings", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(segments[1].Split('?')[0], out var id))
                return id;

            if (segments.Length >= 2 && segments[0].Equals("my-projects", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(segments[1].Split('?')[0], out var myId))
                return myId;

            return null;
        }

        private IQueryable<Notification> Mine(NotificationUserContext ctx)
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            return _context.Notifications
                .AsNoTracking()
                .Where(n => n.RecipientUserId == ctx.UserId
                            && (n.ExpiresAt == null || n.ExpiresAt > now));
        }

        private IQueryable<Notification> UnreadQuery(NotificationUserContext ctx) =>
            Mine(ctx).Where(n => !n.IsRead && !n.IsArchived);

        private static readonly System.Linq.Expressions.Expression<Func<Notification, NotificationDto>> Projection =
            n => new NotificationDto
            {
                Id = n.Id,
                Category = n.Category,
                Type = n.Type,
                Priority = n.Priority,
                Module = n.Module,
                Title = n.Title,
                Message = n.Message,
                EntityType = n.EntityType,
                EntityId = n.EntityId,
                DeepLink = n.DeepLink,
                IsRead = n.IsRead,
                IsEscalation = n.IsEscalation,
                CreatedAt = n.CreatedAt,
                ReadAt = n.ReadAt,
                ExpiresAt = n.ExpiresAt
            };

        // Compiled once: the same shape serves both the SQL projection and single entities.
        private static readonly Func<Notification, NotificationDto> MapCompiled = Projection.Compile();

        private static NotificationDto Map(Notification n) => MapCompiled(n);
    }
}
