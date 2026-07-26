using DAMS.Application.Common;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DAMS.Api.Controllers
{
    /// <summary>
    /// Lead sources, closure reasons and teams. Any staff member can read them (the lead
    /// forms need them); only an admin may change them.
    /// </summary>
    [Route("api/lead-config")]
    public class LeadConfigurationController : LeadControllerBase
    {
        private readonly ILeadConfigurationService _configuration;

        public LeadConfigurationController(ILeadUserContextResolver resolver, ILeadConfigurationService configuration)
            : base(resolver)
        {
            _configuration = configuration;
        }

        [HttpGet("sources")]
        public Task<IActionResult> GetSources([FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default) =>
            RunAsync(_ => _configuration.GetSourcesAsync(includeInactive, cancellationToken), cancellationToken);

        [HttpPost("sources")]
        [Authorize(Roles = LeadRoles.Admin)]
        public Task<IActionResult> CreateSource([FromBody] CreateLeadSourceDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _configuration.CreateSourceAsync(dto, ctx, cancellationToken), cancellationToken);

        [HttpPut("sources/{id:int}")]
        [Authorize(Roles = LeadRoles.Admin)]
        public Task<IActionResult> UpdateSource(int id, [FromBody] UpdateLeadSourceDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _configuration.UpdateSourceAsync(id, dto, ctx, cancellationToken), cancellationToken);

        [HttpGet("closure-reasons")]
        public Task<IActionResult> GetClosureReasons(
            [FromQuery] LeadClosureReasonKind? kind,
            [FromQuery] bool includeInactive = false,
            CancellationToken cancellationToken = default) =>
            RunAsync(_ => _configuration.GetClosureReasonsAsync(includeInactive, kind, cancellationToken), cancellationToken);

        [HttpPost("closure-reasons")]
        [Authorize(Roles = LeadRoles.Admin)]
        public Task<IActionResult> CreateClosureReason([FromBody] CreateLeadClosureReasonDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _configuration.CreateClosureReasonAsync(dto, ctx, cancellationToken), cancellationToken);

        [HttpPut("closure-reasons/{id:int}")]
        [Authorize(Roles = LeadRoles.Admin)]
        public Task<IActionResult> UpdateClosureReason(int id, [FromBody] UpdateLeadClosureReasonDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _configuration.UpdateClosureReasonAsync(id, dto, ctx, cancellationToken), cancellationToken);

        [HttpGet("teams")]
        public Task<IActionResult> GetTeams(CancellationToken cancellationToken) =>
            RunAsync(_ => _configuration.GetTeamsAsync(cancellationToken), cancellationToken);

        [HttpPost("teams")]
        [Authorize(Roles = LeadRoles.Admin)]
        public Task<IActionResult> CreateTeam([FromBody] SaveTeamDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _configuration.CreateTeamAsync(dto, ctx, cancellationToken), cancellationToken);

        [HttpPut("teams/{id:int}")]
        [Authorize(Roles = LeadRoles.Admin)]
        public Task<IActionResult> UpdateTeam(int id, [FromBody] SaveTeamDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _configuration.UpdateTeamAsync(id, dto, ctx, cancellationToken), cancellationToken);
    }
}
