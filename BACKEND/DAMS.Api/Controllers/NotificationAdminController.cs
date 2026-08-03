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
    /// The admin console for the notification platform: branding, provider settings,
    /// templates, rules, manual and scheduled sends, and delivery history.
    ///
    /// Admin-only at the route level and re-checked inside every service that writes, so a
    /// mis-registered route can never become an open door to mass sending.
    /// </summary>
    [ApiController]
    [Authorize(Roles = LeadRoles.Admin)]
    [Route("api/notification-admin")]
    public class NotificationAdminController : ControllerBase
    {
        private readonly INotificationUserContextResolver _resolver;
        private readonly INotificationConfigurationService _configuration;
        private readonly INotificationAdminService _admin;
        private readonly INotificationEventService _events;

        public NotificationAdminController(
            INotificationUserContextResolver resolver,
            INotificationConfigurationService configuration,
            INotificationAdminService admin,
            INotificationEventService events)
        {
            _resolver = resolver;
            _configuration = configuration;
            _admin = admin;
            _events = events;
        }

        // ── Settings ────────────────────────────────────────────────────────────────

        [HttpGet("settings")]
        public Task<IActionResult> GetSettings(CancellationToken cancellationToken) =>
            RunAsync(_ => _configuration.GetSettingsAsync(cancellationToken), cancellationToken);

        [HttpPut("settings")]
        [EnableRateLimiting("notificationWrite")]
        public Task<IActionResult> UpdateSettings(
            [FromBody] UpdateNotificationSettingsDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _configuration.UpdateSettingsAsync(dto, ctx, cancellationToken), cancellationToken);

        [HttpPost("settings/test-email")]
        [EnableRateLimiting("notificationWrite")]
        public Task<IActionResult> SendTestEmail([FromBody] SendTestEmailDto dto, CancellationToken cancellationToken) =>
            RunAsync(async ctx => new { message = await _configuration.SendTestEmailAsync(dto, ctx, cancellationToken) }, cancellationToken);

        /// <summary>Generates a fresh VAPID key pair. Only the public half comes back.</summary>
        [HttpPost("settings/push-keys")]
        [EnableRateLimiting("notificationWrite")]
        public Task<IActionResult> GeneratePushKeys(CancellationToken cancellationToken) =>
            RunAsync(async ctx => new { publicKey = await _configuration.GeneratePushKeysAsync(ctx, cancellationToken) }, cancellationToken);

        // ── Templates ───────────────────────────────────────────────────────────────

        [HttpGet("templates")]
        public Task<IActionResult> GetTemplates(CancellationToken cancellationToken) =>
            RunAsync(_ => _configuration.GetTemplatesAsync(cancellationToken), cancellationToken);

        [HttpGet("templates/{type}/{channel}")]
        public Task<IActionResult> GetTemplate(
            NotificationType type, NotificationChannel channel, CancellationToken cancellationToken) =>
            RunAsync(_ => _configuration.GetTemplateAsync(type, channel, cancellationToken), cancellationToken);

        [HttpPut("templates/{type}/{channel}")]
        [EnableRateLimiting("notificationWrite")]
        public Task<IActionResult> SaveTemplate(
            NotificationType type, NotificationChannel channel,
            [FromBody] SaveNotificationTemplateDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _configuration.SaveTemplateAsync(type, channel, dto, ctx, cancellationToken), cancellationToken);

        [HttpPost("templates/{type}/preview")]
        public Task<IActionResult> PreviewTemplate(
            NotificationType type, [FromBody] SaveNotificationTemplateDto? draft, CancellationToken cancellationToken) =>
            RunAsync(_ => _configuration.PreviewTemplateAsync(type, draft, cancellationToken), cancellationToken);

        // ── Rules ───────────────────────────────────────────────────────────────────

        [HttpGet("rules")]
        public Task<IActionResult> GetRules(CancellationToken cancellationToken) =>
            RunAsync(_ => _configuration.GetRulesAsync(cancellationToken), cancellationToken);

        [HttpPut("rules/{type}")]
        [EnableRateLimiting("notificationWrite")]
        public Task<IActionResult> SaveRule(
            NotificationType type, [FromBody] SaveNotificationRuleDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _configuration.SaveRuleAsync(type, dto, ctx, cancellationToken), cancellationToken);

        // ── Manual and scheduled sends ──────────────────────────────────────────────

        [HttpPost("compose/preview")]
        public Task<IActionResult> PreviewAudience(
            [FromBody] ComposeNotificationDto dto, CancellationToken cancellationToken) =>
            RunAsync(_ => _admin.PreviewAudienceAsync(dto, cancellationToken), cancellationToken);

        [HttpPost("compose")]
        [EnableRateLimiting("notificationSend")]
        public Task<IActionResult> Compose(
            [FromBody] ComposeNotificationDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _admin.ComposeAsync(dto, ctx, cancellationToken), cancellationToken);

        [HttpGet("jobs")]
        public Task<IActionResult> GetJobs([FromQuery] int take = 50, CancellationToken cancellationToken = default) =>
            RunAsync(_ => _admin.GetJobsAsync(take, cancellationToken), cancellationToken);

        [HttpPost("jobs/{id:int}/cancel")]
        public Task<IActionResult> CancelJob(int id, CancellationToken cancellationToken) =>
            RunAsync(ctx => _admin.CancelJobAsync(id, ctx, cancellationToken), cancellationToken);

        // ── Delivery history ────────────────────────────────────────────────────────

        [HttpGet("deliveries")]
        public Task<IActionResult> GetDeliveries(
            [FromQuery] NotificationChannel? channel,
            [FromQuery] NotificationDeliveryStatus? status,
            [FromQuery] NotificationType? type,
            [FromQuery] NotificationCategory? category,
            [FromQuery] int? recipientUserId,
            [FromQuery] int? jobId,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] bool failuresOnly = false,
            [FromQuery] string? search = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 25,
            CancellationToken cancellationToken = default) =>
            RunAsync(_ => _admin.GetDeliveryHistoryAsync(new DeliveryHistoryFilterDto
            {
                Channel = channel,
                Status = status,
                Type = type,
                Category = category,
                RecipientUserId = recipientUserId,
                JobId = jobId,
                From = from,
                To = to,
                FailuresOnly = failuresOnly,
                Search = search,
                Page = page,
                PageSize = pageSize
            }, cancellationToken), cancellationToken);

        [HttpPost("deliveries/{id:int}/retry")]
        [EnableRateLimiting("notificationWrite")]
        public Task<IActionResult> RetryDelivery(int id, CancellationToken cancellationToken) =>
            RunAsync(ctx => _admin.RetryDeliveryAsync(id, ctx, cancellationToken), cancellationToken);

        // ── Suppressed addresses ────────────────────────────────────────────────────

        [HttpGet("suppressions")]
        public Task<IActionResult> GetSuppressions(CancellationToken cancellationToken) =>
            RunAsync(_ => _admin.GetSuppressionsAsync(cancellationToken), cancellationToken);

        [HttpDelete("suppressions/{id:int}")]
        public Task<IActionResult> RemoveSuppression(int id, CancellationToken cancellationToken) =>
            RunAsync(ctx => _admin.RemoveSuppressionAsync(id, ctx, cancellationToken), cancellationToken);

        // ── Audit ───────────────────────────────────────────────────────────────────

        [HttpGet("audit")]
        public Task<IActionResult> GetAudit([FromQuery] int take = 100, CancellationToken cancellationToken = default) =>
            RunAsync(_ => _configuration.GetAuditAsync(take, cancellationToken), cancellationToken);

        // ── Maintenance ─────────────────────────────────────────────────────────────

        /// <summary>Runs the receipt reconciliation sweep on demand. Idempotent.</summary>
        [HttpPost("maintenance/reconcile-receipts")]
        [EnableRateLimiting("notificationWrite")]
        public Task<IActionResult> ReconcileReceipts(CancellationToken cancellationToken) =>
            RunAsync(async _ => new { created = await _events.ReconcilePaymentReceiptsAsync(200, cancellationToken) }, cancellationToken);

        /// <summary>Runs the installment reminder sweep on demand. Idempotent.</summary>
        [HttpPost("maintenance/installment-reminders")]
        [EnableRateLimiting("notificationWrite")]
        public Task<IActionResult> RunInstallmentReminders(CancellationToken cancellationToken) =>
            RunAsync(async _ => new { created = await _events.RunInstallmentRemindersAsync(500, cancellationToken) }, cancellationToken);

        // ── Plumbing ────────────────────────────────────────────────────────────────

        private async Task<IActionResult> RunAsync<T>(Func<NotificationUserContext, Task<T>> action, CancellationToken cancellationToken)
        {
            try
            {
                var ctx = await _resolver.ResolveAsync(User, cancellationToken);
                NotificationAccess.EnsureAdmin(ctx);
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
                NotificationAccess.EnsureAdmin(ctx);
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
