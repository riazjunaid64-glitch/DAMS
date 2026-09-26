using DAMS.Application.Common;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DAMS.Api.Controllers
{
    [Route("api/leads")]
    public class LeadsController : LeadControllerBase
    {
        private readonly ILeadService _leads;
        private readonly ILeadAlertService _alerts;

        public LeadsController(ILeadUserContextResolver resolver, ILeadService leads, ILeadAlertService alerts)
            : base(resolver)
        {
            _leads = leads;
            _alerts = alerts;
        }

        /// <summary>
        /// Manual lead entry. Goes through the same ingestion pipeline as every other
        /// channel, so duplicate detection applies here too: a matching open lead comes back
        /// as a duplicate result unless <c>allowDuplicate</c> is set.
        /// </summary>
        [HttpPost]
        public Task<IActionResult> Create([FromBody] LeadIntakeDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _leads.IngestAsync(dto, ctx, cancellationToken: cancellationToken), cancellationToken);

        [HttpGet]
        public Task<IActionResult> GetAll(
            [FromQuery] LeadStage? stage,
            [FromQuery] LeadAssignmentState? assignmentState,
            [FromQuery] LeadQualification? qualification,
            [FromQuery] int? sourceId,
            [FromQuery] int? employeeId,
            [FromQuery] int? teamId,
            [FromQuery] int? projectId,
            [FromQuery] LeadPaymentPreference? paymentPreference,
            [FromQuery] int? unitId,
            [FromQuery] string? campaign,
            [FromQuery] bool? unassigned,
            [FromQuery] bool? overdue,
            [FromQuery] bool? inactive,
            [FromQuery] DateTime? createdFrom,
            [FromQuery] DateTime? createdTo,
            [FromQuery] string? search,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string sortBy = "createdat",
            [FromQuery] bool sortDesc = true,
            CancellationToken cancellationToken = default) =>
            RunAsync(ctx => _leads.GetLeadsAsync(new LeadFilterDto
            {
                Stage = stage,
                AssignmentState = assignmentState,
                Qualification = qualification,
                LeadSourceId = sourceId,
                AssignedEmployeeId = employeeId,
                AssignedTeamId = teamId,
                ProjectId = projectId,
                PaymentPreference = paymentPreference,
                UnitId = unitId,
                CampaignName = campaign,
                Unassigned = unassigned,
                OverdueOnly = overdue,
                InactiveOnly = inactive,
                CreatedFrom = createdFrom,
                CreatedTo = createdTo,
                SearchTerm = search,
                Page = page,
                PageSize = pageSize,
                SortBy = sortBy,
                SortDescending = sortDesc
            }, ctx, cancellationToken), cancellationToken);

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
        {
            try
            {
                var ctx = await CurrentAsync(cancellationToken);
                var lead = await _leads.GetByIdAsync(id, ctx, cancellationToken);
                return lead == null
                    ? NotFound(new { message = "Lead not found or you do not have access to it." })
                    : Ok(lead);
            }
            catch (LeadAuthorizationException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
            }
        }

        [HttpPut("{id:int}")]
        public Task<IActionResult> Update(int id, [FromBody] UpdateLeadDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _leads.UpdateAsync(id, dto, ctx, cancellationToken), cancellationToken);

        [HttpGet("{id:int}/external-submissions")]
        public Task<IActionResult> GetExternalSubmissions(int id, CancellationToken cancellationToken) =>
            RunAsync(ctx => _leads.GetExternalSubmissionsAsync(id, ctx, cancellationToken), cancellationToken);

        [HttpGet("{id:int}/external-submissions/{submissionId:int}/raw")]
        [Authorize(Roles = LeadRoles.AdminOrManager)]
        public Task<IActionResult> GetExternalSubmissionRaw(int id, int submissionId, CancellationToken cancellationToken) =>
            RunAsync(ctx => _leads.GetExternalSubmissionRawAsync(id, submissionId, ctx, cancellationToken), cancellationToken);

        [HttpGet("{id:int}/timeline")]
        public Task<IActionResult> GetTimeline(int id, CancellationToken cancellationToken) =>
            RunAsync(ctx => _leads.GetTimelineAsync(id, ctx, cancellationToken), cancellationToken);

        [HttpGet("{id:int}/assignment-history")]
        public Task<IActionResult> GetAssignmentHistory(int id, CancellationToken cancellationToken) =>
            RunAsync(ctx => _leads.GetAssignmentHistoryAsync(id, ctx, cancellationToken), cancellationToken);

        [HttpPost("{id:int}/assign")]
        [Authorize(Roles = LeadRoles.AdminOrManager)]
        public Task<IActionResult> Assign(int id, [FromBody] AssignLeadDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _leads.AssignAsync(id, dto, ctx, cancellationToken), cancellationToken);

        [HttpPost("{id:int}/stage")]
        public Task<IActionResult> ChangeStage(int id, [FromBody] ChangeLeadStageDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _leads.ChangeStageAsync(id, dto, ctx, cancellationToken), cancellationToken);

        [HttpPost("{id:int}/qualification")]
        public Task<IActionResult> UpdateQualification(int id, [FromBody] UpdateLeadQualificationDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _leads.UpdateQualificationAsync(id, dto, ctx, cancellationToken), cancellationToken);

        /// <summary>Marks a lead Lost. A configured reason is mandatory.</summary>
        [HttpPost("{id:int}/lost")]
        public Task<IActionResult> MarkLost(int id, [FromBody] CloseLeadDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _leads.CloseAsync(id, dormant: false, dto, ctx, cancellationToken), cancellationToken);

        /// <summary>Marks a lead Dormant, optionally with a date to revisit it.</summary>
        [HttpPost("{id:int}/dormant")]
        public Task<IActionResult> MarkDormant(int id, [FromBody] CloseLeadDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _leads.CloseAsync(id, dormant: true, dto, ctx, cancellationToken), cancellationToken);

        [HttpPost("{id:int}/reopen")]
        [Authorize(Roles = LeadRoles.AdminOrManager)]
        public Task<IActionResult> Reopen(int id, [FromBody] ReopenLeadDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _leads.ReopenAsync(id, dto, ctx, cancellationToken), cancellationToken);

        /// <summary>Converts the lead into a customer and booking. Idempotent.</summary>
        [HttpPost("{id:int}/convert")]
        [Authorize(Roles = LeadRoles.AdminOrManager)]
        public Task<IActionResult> Convert(int id, [FromBody] ConvertLeadDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _leads.ConvertAsync(id, dto, ctx, cancellationToken), cancellationToken);

        /// <summary>External enquiries whose details match more than one open lead, waiting for a decision.</summary>
        [HttpGet("held-enquiries")]
        [Authorize(Roles = LeadRoles.Admin)]
        public Task<IActionResult> GetHeldEnquiries(CancellationToken cancellationToken) =>
            RunAsync(ctx => _leads.GetIntakeHoldsAsync(ctx, cancellationToken), cancellationToken);

        /// <summary>Adds a held enquiry to the chosen lead, or dismisses it.</summary>
        [HttpPost("held-enquiries/{id:int}/resolve")]
        [Authorize(Roles = LeadRoles.Admin)]
        public Task<IActionResult> ResolveHeldEnquiry(int id, [FromBody] ResolveLeadIntakeHoldDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _leads.ResolveIntakeHoldAsync(id, dto, ctx, cancellationToken), cancellationToken);

        /// <summary>
        /// Creates leads for website booking requests submitted before lead management.
        /// Repeatable — already-linked requests are skipped.
        /// </summary>
        [HttpPost("maintenance/backfill-booking-requests")]
        [Authorize(Roles = LeadRoles.Admin)]
        public Task<IActionResult> BackfillBookingRequests(CancellationToken cancellationToken) =>
            RunAsync(ctx => _leads.BackfillFromBookingRequestsAsync(ctx, cancellationToken), cancellationToken);

        /// <summary>Runs the overdue/inactivity/escalation scan on demand.</summary>
        [HttpPost("maintenance/run-alert-scan")]
        [Authorize(Roles = LeadRoles.Admin)]
        public Task<IActionResult> RunAlertScan(CancellationToken cancellationToken) =>
            RunAsync(_ => _alerts.RunScanAsync(cancellationToken), cancellationToken);
    }
}
