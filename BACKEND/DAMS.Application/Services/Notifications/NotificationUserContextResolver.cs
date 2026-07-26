using System.Security.Claims;
using DAMS.Application.Common;
using DAMS.Application.Interfaces;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services.Notifications
{
    /// <summary>
    /// Resolves the caller from the token. The user id comes from the signed claim and is
    /// never accepted from the request body or the query string, which is what makes every
    /// inbox query structurally incapable of reading another person's notifications.
    /// </summary>
    public sealed class NotificationUserContextResolver : INotificationUserContextResolver
    {
        private readonly AppDbContext _context;

        public NotificationUserContextResolver(AppDbContext context)
        {
            _context = context;
        }

        public async Task<NotificationUserContext> ResolveAsync(
            ClaimsPrincipal principal, CancellationToken cancellationToken = default)
        {
            var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                throw new LeadAuthorizationException("Your session is not valid. Sign in again.");

            var user = await _context.Users
                .AsNoTracking()
                .Where(u => u.UserId == userId)
                .Select(u => new { u.UserId, u.FullName, u.Email, Role = u.Role.Role_name })
                .FirstOrDefaultAsync(cancellationToken);

            // The role is read from the database rather than the token: a role that was
            // changed or revoked after the token was issued takes effect immediately for
            // anything the notification platform authorises.
            if (user == null)
                throw new LeadAuthorizationException("Your account no longer exists.");

            return new NotificationUserContext
            {
                UserId = user.UserId,
                Role = user.Role,
                DisplayName = user.FullName,
                Email = user.Email
            };
        }
    }
}
