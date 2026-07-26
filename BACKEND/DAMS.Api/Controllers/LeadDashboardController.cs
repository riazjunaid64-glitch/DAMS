using DAMS.Application.Common;
using DAMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DAMS.Api.Controllers
{
    [Route("api/lead-dashboard")]
    public class LeadDashboardController : LeadControllerBase
    {
        private readonly ILeadReportingService _reporting;
        private readonly ILeadNotificationService _notifications;

        public LeadDashboardController(
            ILeadUserContextResolver resolver,
            ILeadReportingService reporting,
            ILeadNotificationService notifications)
            : base(resolver)
        {
            _reporting = reporting;
            _notifications = notifications;
        }

        /// <summary>The signed-in employee's own workload.</summary>
        [HttpGet("me")]
        public Task<IActionResult> GetMine(CancellationToken cancellationToken) =>
            RunAsync(ctx => _reporting.GetEmployeeDashboardAsync(ctx, cancellationToken), cancellationToken);

        /// <summary>Team view: unassigned queue, overdue work, per-employee performance.</summary>
        [HttpGet("team")]
        [Authorize(Roles = LeadRoles.AdminOrManager)]
        public Task<IActionResult> GetTeam(CancellationToken cancellationToken) =>
            RunAsync(ctx => _reporting.GetManagerDashboardAsync(ctx, cancellationToken), cancellationToken);

        /// <summary>Organisation view: source and campaign attribution, cycle times, outcomes.</summary>
        [HttpGet("organisation")]
        [Authorize(Roles = LeadRoles.Admin)]
        public Task<IActionResult> GetOrganisation(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken cancellationToken) =>
            RunAsync(ctx => _reporting.GetAdminDashboardAsync(ctx, from, to, cancellationToken), cancellationToken);

        [HttpGet("notifications")]
        public Task<IActionResult> GetNotifications(
            [FromQuery] bool unreadOnly = false, [FromQuery] int take = 50, CancellationToken cancellationToken = default) =>
            RunAsync(ctx => _notifications.GetMyNotificationsAsync(ctx, unreadOnly, take, cancellationToken), cancellationToken);

        [HttpGet("notifications/unread-count")]
        public Task<IActionResult> GetUnreadCount(CancellationToken cancellationToken) =>
            RunAsync(async ctx => new { count = await _notifications.GetUnreadCountAsync(ctx, cancellationToken) }, cancellationToken);

        [HttpPost("notifications/{id:int}/read")]
        public Task<IActionResult> MarkRead(int id, CancellationToken cancellationToken) =>
            RunAsync(ctx => _notifications.MarkReadAsync(id, ctx, cancellationToken), cancellationToken);

        [HttpPost("notifications/read-all")]
        public Task<IActionResult> MarkAllRead(CancellationToken cancellationToken) =>
            RunAsync(ctx => _notifications.MarkAllReadAsync(ctx, cancellationToken), cancellationToken);
    }
}
