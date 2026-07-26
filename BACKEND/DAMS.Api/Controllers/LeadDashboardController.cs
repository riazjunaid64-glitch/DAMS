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

        public LeadDashboardController(ILeadUserContextResolver resolver, ILeadReportingService reporting)
            : base(resolver)
        {
            _reporting = reporting;
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

        // Lead alerts now live in the central notification inbox: /api/notifications, with
        // the lead categories available as filters. There is no lead-only inbox to keep in
        // step with it.
    }
}
