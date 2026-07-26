using DAMS.Application.Common;
using DAMS.Application.DTOs.NotificationDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DAMS.Api.Controllers
{
    /// <summary>
    /// Every authenticated user's own notification surface: their inbox, their preferences
    /// and their browser subscriptions. Nothing here takes a recipient from the request — the
    /// caller is always resolved from the token, so no identifier a client can change reaches
    /// somebody else's data.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/notifications")]
    public class NotificationsController : ControllerBase
    {
        private readonly INotificationUserContextResolver _resolver;
        private readonly INotificationInboxService _inbox;
        private readonly INotificationPreferenceService _preferences;
        private readonly IPushSubscriptionService _push;

        public NotificationsController(
            INotificationUserContextResolver resolver,
            INotificationInboxService inbox,
            INotificationPreferenceService preferences,
            IPushSubscriptionService push)
        {
            _resolver = resolver;
            _inbox = inbox;
            _preferences = preferences;
            _push = push;
        }

        // ── Inbox ───────────────────────────────────────────────────────────────────

        [HttpGet]
        public Task<IActionResult> Get(
            [FromQuery] NotificationCategory? category,
            [FromQuery] bool unreadOnly = false,
            [FromQuery] bool includeArchived = false,
            [FromQuery] string? search = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken cancellationToken = default) =>
            RunAsync(ctx => _inbox.GetAsync(ctx, new NotificationFilterDto
            {
                Category = category,
                UnreadOnly = unreadOnly,
                IncludeArchived = includeArchived,
                Search = search,
                Page = page,
                PageSize = pageSize
            }, cancellationToken), cancellationToken);

        /// <summary>Unread count plus the most recent items — one call for the bell and drawer.</summary>
        [HttpGet("summary")]
        public Task<IActionResult> GetSummary([FromQuery] int take = 10, CancellationToken cancellationToken = default) =>
            RunAsync(ctx => _inbox.GetSummaryAsync(ctx, take, cancellationToken), cancellationToken);

        [HttpGet("unread-count")]
        public Task<IActionResult> GetUnreadCount(CancellationToken cancellationToken) =>
            RunAsync(async ctx => new { count = await _inbox.GetUnreadCountAsync(ctx, cancellationToken) }, cancellationToken);

        [HttpPost("{id:int}/read")]
        public Task<IActionResult> MarkRead(int id, CancellationToken cancellationToken) =>
            RunAsync(ctx => _inbox.MarkReadAsync(id, ctx, cancellationToken), cancellationToken);

        [HttpPost("read-all")]
        public Task<IActionResult> MarkAllRead([FromQuery] NotificationCategory? category, CancellationToken cancellationToken) =>
            RunAsync(async ctx => new { updated = await _inbox.MarkAllReadAsync(ctx, category, cancellationToken) }, cancellationToken);

        [HttpPost("{id:int}/archive")]
        public Task<IActionResult> Archive(int id, CancellationToken cancellationToken) =>
            RunAsync(ctx => _inbox.ArchiveAsync(id, ctx, cancellationToken), cancellationToken);

        /// <summary>
        /// Marks the notification read and says where it should open. Access to the related
        /// record is re-checked now, so a link that was valid when it was written but is not
        /// any more gives a clear answer instead of a broken page.
        /// </summary>
        [HttpPost("{id:int}/open")]
        public Task<IActionResult> Open(int id, CancellationToken cancellationToken) =>
            RunAsync(ctx => _inbox.OpenAsync(id, ctx, cancellationToken), cancellationToken);

        // The live stream lives in NotificationStreamController: it is held open for minutes
        // at a time and must not keep a database connection rented while it waits.

        // ── Preferences ─────────────────────────────────────────────────────────────

        [HttpGet("preferences")]
        public Task<IActionResult> GetPreferences(CancellationToken cancellationToken) =>
            RunAsync(ctx => _preferences.GetAsync(ctx, cancellationToken), cancellationToken);

        [HttpPut("preferences")]
        public Task<IActionResult> UpdatePreferences(
            [FromBody] UpdateNotificationPreferencesDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _preferences.UpdateAsync(ctx, dto, cancellationToken), cancellationToken);

        // ── Browser push ────────────────────────────────────────────────────────────

        [HttpGet("push/config")]
        public Task<IActionResult> GetPushConfig(CancellationToken cancellationToken) =>
            RunAsync(ctx => _push.GetConfigAsync(ctx, cancellationToken), cancellationToken);

        [HttpPost("push/subscribe")]
        [EnableRateLimiting("notificationWrite")]
        public Task<IActionResult> Subscribe(
            [FromBody] RegisterPushSubscriptionDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _push.RegisterAsync(ctx, dto, cancellationToken), cancellationToken);

        [HttpPost("push/unsubscribe")]
        public Task<IActionResult> Unsubscribe(
            [FromBody] UnregisterPushSubscriptionDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _push.UnregisterAsync(ctx, dto.Endpoint, cancellationToken), cancellationToken);

        /// <summary>
        /// Removes every browser subscription for this login. The frontend calls this on
        /// sign-out so the next person to use a shared computer cannot receive the previous
        /// user's notifications.
        /// </summary>
        [HttpPost("push/unsubscribe-all")]
        public Task<IActionResult> UnsubscribeAll(CancellationToken cancellationToken) =>
            RunAsync(async ctx => new { removed = await _push.UnregisterAllAsync(ctx.UserId, cancellationToken) }, cancellationToken);

        [HttpGet("push/devices")]
        public Task<IActionResult> GetDevices(CancellationToken cancellationToken) =>
            RunAsync(ctx => _push.GetMyDevicesAsync(ctx, cancellationToken), cancellationToken);

        [HttpPost("push/test")]
        [EnableRateLimiting("notificationWrite")]
        public Task<IActionResult> SendTestPush(CancellationToken cancellationToken) =>
            RunAsync(async ctx => new { delivered = await _push.SendTestAsync(ctx, cancellationToken) }, cancellationToken);

        // ── Plumbing ────────────────────────────────────────────────────────────────

        private async Task<IActionResult> RunAsync<T>(Func<NotificationUserContext, Task<T>> action, CancellationToken cancellationToken)
        {
            try
            {
                var ctx = await _resolver.ResolveAsync(User, cancellationToken);
                return Ok(await action(ctx));
            }
            catch (LeadNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (LeadAuthorizationException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
            }
            catch (NotificationTemplateException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private async Task<IActionResult> RunAsync(Func<NotificationUserContext, Task> action, CancellationToken cancellationToken)
        {
            try
            {
                var ctx = await _resolver.ResolveAsync(User, cancellationToken);
                await action(ctx);
                return NoContent();
            }
            catch (LeadNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (LeadAuthorizationException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
