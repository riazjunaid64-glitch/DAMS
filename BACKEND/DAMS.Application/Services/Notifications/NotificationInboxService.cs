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
        private readonly NotificationEligibilityPolicy _eligibility;
        private bool? _schemaAvailable;
        private (int UserId, NotificationEligibilityPolicy.ResourceScope Scope)? _scope;

        public NotificationInboxService(
            AppDbContext context,
            TimeProvider clock,
            NotificationEligibilityPolicy eligibility)
        {
            _context = context;
            // Expiry is judged against the same clock the delivery worker uses, so a
            // notification never lingers in an inbox after it has stopped being sendable.
            _clock = clock;
            _eligibility = eligibility;
        }

        public async Task<NotificationPageDto> GetAsync(
            NotificationUserContext ctx, NotificationFilterDto filter, CancellationToken cancellationToken = default)
        {
            _eligibility.EnsureCategoryAllowed(ctx, filter.Category);

            if (!await SchemaExistsAsync(cancellationToken))
            {
                return new NotificationPageDto
                {
                    Page = filter.Page < 1 ? 1 : filter.Page,
                    PageSize = filter.PageSize is < 1 or > 100 ? 20 : filter.PageSize
                };
            }

            var query = await MineAsync(ctx, cancellationToken);

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
                UnreadCount = await (await UnreadQueryAsync(ctx, cancellationToken)).CountAsync(cancellationToken),
                Page = page,
                PageSize = pageSize
            };
        }

        public async Task<NotificationSummaryDto> GetSummaryAsync(
            NotificationUserContext ctx, int take, CancellationToken cancellationToken = default)
        {
            if (!await SchemaExistsAsync(cancellationToken))
                return new NotificationSummaryDto();

            var unread = await UnreadQueryAsync(ctx, cancellationToken);

            var byCategory = await unread
                .GroupBy(n => n.Category)
                .Select(g => new { Category = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            var recent = await (await MineAsync(ctx, cancellationToken))
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

        public async Task<int> GetUnreadCountAsync(NotificationUserContext ctx, CancellationToken cancellationToken = default) =>
            await SchemaExistsAsync(cancellationToken)
                ? await (await UnreadQueryAsync(ctx, cancellationToken)).CountAsync(cancellationToken)
                : 0;

        public async Task<NotificationDto> MarkReadAsync(
            int notificationId, NotificationUserContext ctx, CancellationToken cancellationToken = default)
        {
            if (!await SchemaExistsAsync(cancellationToken))
                throw new LeadNotFoundException("Notification not found.");

            var notification = await (await VisibleTrackedAsync(ctx, cancellationToken))
                .FirstOrDefaultAsync(n => n.Id == notificationId, cancellationToken)
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
            _eligibility.EnsureCategoryAllowed(ctx, category);

            if (!await SchemaExistsAsync(cancellationToken))
                return 0;

            var query = (await VisibleTrackedAsync(ctx, cancellationToken)).Where(n => !n.IsRead);

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
            if (!await SchemaExistsAsync(cancellationToken))
                return;

            var notification = await (await VisibleTrackedAsync(ctx, cancellationToken))
                .FirstOrDefaultAsync(n => n.Id == notificationId, cancellationToken)
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
            if (!await SchemaExistsAsync(cancellationToken))
                throw new LeadNotFoundException("Notification not found.");

            var notification = await (await VisibleTrackedAsync(ctx, cancellationToken))
                .FirstOrDefaultAsync(n => n.Id == notificationId, cancellationToken)
                ?? throw new LeadNotFoundException("Notification not found.");

            if (!notification.IsRead)
            {
                notification.IsRead = true;
                notification.ReadAt = DateTime.UtcNow;
                notification.SeenAt ??= notification.ReadAt;
                await _context.SaveChangesAsync(cancellationToken);
            }

            var dto = Map(notification);

            if (notification.EntityType is NotificationEntityType.None or NotificationEntityType.Announcement)
            {
                var routeAccess = notification.Type is NotificationType.AdminAnnouncement
                    or NotificationType.ProjectUpdated or NotificationType.AccountSecurity
                    ? await CheckAnnouncementAccessAsync(notification.DeepLink, ctx, cancellationToken)
                    : (false, "You no longer have permission to open this record.");
                var safeLink = NotificationLink.Sanitize(notification.DeepLink);
                return new NotificationOpenResult
                {
                    Allowed = routeAccess.Item1,
                    DeepLink = routeAccess.Item1 ? safeLink : null,
                    Message = routeAccess.Item2,
                    Notification = dto
                };
            }

            if (notification.EntityType == NotificationEntityType.Account
                && notification.EntityId is null or <= 0)
            {
                var routeAccess = await CheckAnnouncementAccessAsync(notification.DeepLink, ctx, cancellationToken);
                return new NotificationOpenResult
                {
                    Allowed = routeAccess.Allowed,
                    DeepLink = routeAccess.Allowed ? NotificationLink.Sanitize(notification.DeepLink) : null,
                    Message = routeAccess.Message,
                    Notification = dto
                };
            }

            if (notification.EntityId is null or <= 0)
            {
                return new NotificationOpenResult
                {
                    Allowed = false,
                    DeepLink = null,
                    Message = "The record this notification refers to is no longer available.",
                    Notification = dto
                };
            }

            var access = await CheckEntityAccessAsync(notification, ctx, cancellationToken);

            return new NotificationOpenResult
            {
                Allowed = access.Allowed,
                DeepLink = access.Allowed ? NotificationLink.Sanitize(notification.DeepLink) : null,
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
                case NotificationEntityType.Announcement:
                    return await CheckAnnouncementAccessAsync(notification.DeepLink, ctx, cancellationToken);

                case NotificationEntityType.Account:
                    return id == ctx.UserId
                        ? await CheckAnnouncementAccessAsync(notification.DeepLink, ctx, cancellationToken)
                        : (false, denied);

                case NotificationEntityType.Lead:
                case NotificationEntityType.LeadFollowUp:
                case NotificationEntityType.LeadSiteVisit:
                case NotificationEntityType.LeadComment:
                {
                    // The link always lands on the lead workspace, so the lead is what has to
                    // be reachable, resolved through the lead module's own scope rules.
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
                {
                    if (!await _context.Projects.AsNoTracking().AnyAsync(p => p.Id == id, cancellationToken))
                        return (false, gone);

                    return await _eligibility.CanReceiveAsync(notification, cancellationToken)
                        ? (true, "Opened.")
                        : (false, denied);
                }

                case NotificationEntityType.Unit:
                    return await _context.Units.AsNoTracking().AnyAsync(u => u.Id == id, cancellationToken)
                        ? (true, "Opened.")
                        : (false, gone);

                default:
                    return (false, denied);
            }
        }

        private async Task<(bool Allowed, string Message)> CheckAnnouncementAccessAsync(
            string? deepLink, NotificationUserContext ctx, CancellationToken cancellationToken)
        {
            const string denied = "You no longer have permission to open this record.";
            var safe = NotificationLink.Sanitize(deepLink);
            if (safe == null)
                return (true, "Opened.");

            var path = safe.Split('?', '#')[0].TrimEnd('/');
            if (path.Length == 0)
                path = "/";

            if (path is "/" or "/notifications" or "/projects" or "/about" or "/contact" or "/application-form"
                || TryRouteId(path, "/projects/", out _)
                || TryRouteId(path, "/units/", out _))
                return (true, "Opened.");

            if (path == "/my-projects")
                return string.Equals(ctx.Role, "Client", StringComparison.OrdinalIgnoreCase)
                    ? (true, "Opened.")
                    : (false, denied);

            if (TryRouteId(path, "/my-projects/", out var bookingId))
            {
                var ownsBooking = string.Equals(ctx.Role, "Client", StringComparison.OrdinalIgnoreCase)
                    && await _context.Bookings.AsNoTracking().AnyAsync(booking =>
                        booking.Id == bookingId && booking.Customer.UserId == ctx.UserId, cancellationToken);
                return ownsBooking ? (true, "Opened.") : (false, denied);
            }

            if (path == "/crm")
                return ctx.IsStaff ? (true, "Opened.") : (false, denied);

            if (path == "/crm/settings")
                return ctx.IsAdmin ? (true, "Opened.") : (false, denied);

            if (TryRouteId(path, "/crm/leads/", out var leadId))
            {
                if (!ctx.IsStaff)
                    return (false, denied);

                var leadCtx = await BuildLeadContextAsync(ctx, cancellationToken);
                var visible = await LeadAccess.Scope(_context.Leads.AsNoTracking(), leadCtx)
                    .AnyAsync(lead => lead.Id == leadId, cancellationToken);
                return visible ? (true, "Opened.") : (false, denied);
            }

            var adminRoute = path == "/bookings"
                             || path.StartsWith("/confirmed-bookings", StringComparison.OrdinalIgnoreCase)
                             || path.StartsWith("/customers", StringComparison.OrdinalIgnoreCase)
                             || path.StartsWith("/employees", StringComparison.OrdinalIgnoreCase)
                             || path.StartsWith("/finance", StringComparison.OrdinalIgnoreCase)
                             || path == "/notifications/settings";
            return adminRoute && ctx.IsAdmin ? (true, "Opened.") : (false, denied);
        }

        private static bool TryRouteId(string path, string prefix, out int id)
        {
            id = 0;
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                   && int.TryParse(path[prefix.Length..], out id)
                   && id > 0;
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

        private async Task<IQueryable<Notification>> MineAsync(
            NotificationUserContext ctx, CancellationToken cancellationToken) =>
            await ScopedAsync(_context.Notifications.AsNoTracking(), ctx, cancellationToken);

        private async Task<IQueryable<Notification>> VisibleTrackedAsync(
            NotificationUserContext ctx, CancellationToken cancellationToken) =>
            await ScopedAsync(_context.Notifications, ctx, cancellationToken);

        /// <summary>
        /// Ownership, expiry, the role's type/category matrix and — so a misrouted or corrupted
        /// row never shows its wording in a listing, a bell or a count — the reader's current
        /// ownership of the record each notification points at.
        /// </summary>
        private async Task<IQueryable<Notification>> ScopedAsync(
            IQueryable<Notification> source, NotificationUserContext ctx, CancellationToken cancellationToken)
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            var owned = source.Where(n => n.RecipientUserId == ctx.UserId
                                          && (n.ExpiresAt == null || n.ExpiresAt > now));

            // Keyed by account, not just cached: one instance answers several calls per
            // request, and must never reuse what a different reader owns.
            if (_scope?.UserId != ctx.UserId)
                _scope = (ctx.UserId, await _eligibility.BuildResourceScopeAsync(ctx.UserId, ctx.Role, cancellationToken));

            return _eligibility.ApplyResourceScope(_eligibility.ApplyRoleScope(owned, ctx.Role), _scope.Value.Scope);
        }

        private async Task<IQueryable<Notification>> UnreadQueryAsync(
            NotificationUserContext ctx, CancellationToken cancellationToken) =>
            (await MineAsync(ctx, cancellationToken)).Where(n => !n.IsRead && !n.IsArchived);

        private async Task<bool> SchemaExistsAsync(CancellationToken cancellationToken)
        {
            if (_schemaAvailable.HasValue)
                return _schemaAvailable.Value;

            _schemaAvailable = await NotificationSchemaProbe.ExistsAsync(_context, cancellationToken);
            return _schemaAvailable.Value;
        }

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
