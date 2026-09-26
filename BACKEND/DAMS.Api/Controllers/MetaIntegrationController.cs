using DAMS.Application.Common;
using DAMS.Application.DTOs.IntegrationDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Enums;
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
        private readonly IMetaLeadBackfillService _backfill;

        public MetaIntegrationController(
            ILeadUserContextResolver resolver,
            IMetaIntegrationService integration,
            IMetaResourceSyncService sync,
            IMetaLeadBackfillService backfill)
            : base(resolver)
        {
            _integration = integration;
            _sync = sync;
            _backfill = backfill;
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

        /// <summary>
        /// Keyed by the form's own Meta id rather than a connection, so the mapping survives a
        /// reconnect or a resync.
        /// </summary>
        [HttpGet("lead-forms/{formId}/mapping")]
        public Task<IActionResult> LeadFormMapping(string formId, CancellationToken cancellationToken) =>
            RunAsync(_ => _integration.GetLeadFormMappingAsync(formId, cancellationToken), cancellationToken);

        [HttpPut("lead-forms/{formId}/mapping")]
        public Task<IActionResult> SaveLeadFormMapping(
            string formId, [FromBody] SaveLeadFormMappingDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _integration.SaveLeadFormMappingAsync(formId, dto, ctx, cancellationToken), cancellationToken);

        [HttpPost("connections/{id:int}/sync")]
        public Task<IActionResult> Sync(int id, CancellationToken cancellationToken) =>
            RunAsync(_ => _sync.SyncNowAsync(id, cancellationToken), cancellationToken);

        /// <summary>
        /// Recovers a Page's or one form's leads from Meta, at most 90 days back. Safe to repeat:
        /// a lead already in DAMS is counted, never queued again.
        /// </summary>
        [HttpPost("connections/{id:int}/import")]
        public Task<IActionResult> ImportLeads(int id, [FromBody] ImportMetaLeadsDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _backfill.ImportAsync(id, dto, ctx, cancellationToken), cancellationToken);

        [HttpGet("connections/{id:int}/events")]
        public Task<IActionResult> Events(
            int id, [FromQuery] int take = 25, [FromQuery] ExternalIntegrationEventStatus? status = null,
            CancellationToken cancellationToken = default) =>
            RunAsync(_ => _integration.GetEventsAsync(id, take, status, cancellationToken), cancellationToken);

        [HttpPost("connections/{id:int}/events/{eventId:int}/retry")]
        public Task<IActionResult> RetryEvent(int id, int eventId, CancellationToken cancellationToken) =>
            RunAsync(ctx => _integration.RetryEventAsync(id, eventId, ctx, cancellationToken), cancellationToken);

        [HttpPost("connections/{id:int}/disconnect")]
        public Task<IActionResult> Disconnect(int id, CancellationToken cancellationToken) =>
            RunAsync(ctx => _integration.DisconnectAsync(id, ctx, cancellationToken), cancellationToken);
    }
}
