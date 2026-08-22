using DAMS.Application.Common;
using DAMS.Application.DTOs.IntegrationDtos;
using DAMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DAMS.Api.Controllers
{
    /// <summary>
    /// Admin control panel for connected Meta accounts.
    ///
    /// No response from this controller ever carries a token, and none of its DTOs has a field
    /// capable of holding one.
    /// </summary>
    [Route("api/integrations/meta")]
    [Authorize(Roles = LeadRoles.Admin)]
    public class MetaIntegrationController : LeadControllerBase
    {
        private readonly IMetaIntegrationService _integration;
        private readonly IMetaResourceSyncService _sync;

        public MetaIntegrationController(
            ILeadUserContextResolver resolver,
            IMetaIntegrationService integration,
            IMetaResourceSyncService sync)
            : base(resolver)
        {
            _integration = integration;
            _sync = sync;
        }

        /// <summary>
        /// Returns the Meta consent URL for the browser to navigate to. It is returned as JSON
        /// rather than as a redirect because a 302 from an authenticated XHR cannot carry the
        /// browser to another origin.
        /// </summary>
        [HttpPost("connect")]
        public Task<IActionResult> Connect([FromBody] StartMetaConnectDto? dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _integration.StartConnectAsync(ctx, dto?.ReturnPath, cancellationToken), cancellationToken);

        /// <summary>
        /// Where Meta sends the admin's browser back to.
        ///
        /// Anonymous by necessity — this is a top-level browser navigation from Meta, carrying
        /// no Authorization header — and safe because the one-time state is what authenticates
        /// it. Who performed the action is read from the stored state, never from the request.
        /// </summary>
        [HttpGet("callback")]
        [AllowAnonymous]
        public async Task<IActionResult> Callback(
            [FromQuery] string? code,
            [FromQuery] string? state,
            [FromQuery] string? error,
            CancellationToken cancellationToken)
        {
            var target = await _integration.CompleteCallbackAsync(code, state, error, cancellationToken);
            return Redirect(target);
        }

        [HttpGet("connections")]
        public Task<IActionResult> Connections(CancellationToken cancellationToken) =>
            RunAsync(_ => _integration.GetConnectionsAsync(cancellationToken), cancellationToken);

        [HttpGet("connections/{id:int}/resources")]
        public Task<IActionResult> Resources(int id, CancellationToken cancellationToken) =>
            RunAsync(_ => _integration.GetResourcesAsync(id, cancellationToken), cancellationToken);

        [HttpPatch("connections/{id:int}/resources/{resourceId:int}")]
        public Task<IActionResult> SetResourceEnabled(
            int id, int resourceId, [FromBody] SetMetaResourceEnabledDto dto, CancellationToken cancellationToken) =>
            RunAsync(_ => _integration.SetResourceEnabledAsync(id, resourceId, dto.IsEnabled, cancellationToken), cancellationToken);

        [HttpPost("connections/{id:int}/sync")]
        public Task<IActionResult> Sync(int id, CancellationToken cancellationToken) =>
            RunAsync(_ => _sync.SyncNowAsync(id, cancellationToken), cancellationToken);

        [HttpGet("connections/{id:int}/events")]
        public Task<IActionResult> Events(int id, [FromQuery] int take = 25, CancellationToken cancellationToken = default) =>
            RunAsync(_ => _integration.GetEventsAsync(id, take, cancellationToken), cancellationToken);

        [HttpPost("connections/{id:int}/disconnect")]
        public Task<IActionResult> Disconnect(int id, CancellationToken cancellationToken) =>
            RunAsync(ctx => _integration.DisconnectAsync(id, ctx, cancellationToken), cancellationToken);
    }
}
