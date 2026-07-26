using DAMS.Application.Common;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using DAMS.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace DAMS.Api.Controllers
{
    /// <summary>
    /// Day-to-day work on a lead: communication, internal collaboration, follow-ups, site
    /// visits and documents.
    /// </summary>
    [Route("api/leads")]
    public class LeadEngagementController : LeadControllerBase
    {
        private readonly ILeadCommunicationService _communications;
        private readonly ILeadFollowUpService _followUps;
        private readonly ILeadSiteVisitService _siteVisits;
        private readonly ILeadDocumentService _documents;

        public LeadEngagementController(
            ILeadUserContextResolver resolver,
            ILeadCommunicationService communications,
            ILeadFollowUpService followUps,
            ILeadSiteVisitService siteVisits,
            ILeadDocumentService documents)
            : base(resolver)
        {
            _communications = communications;
            _followUps = followUps;
            _siteVisits = siteVisits;
            _documents = documents;
        }

        // ── Communication ───────────────────────────────────────────────────────────

        [HttpPost("{id:int}/communications")]
        public Task<IActionResult> RecordCommunication(int id, [FromBody] RecordLeadCommunicationDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _communications.RecordAsync(id, dto, ctx, cancellationToken), cancellationToken);

        [HttpGet("{id:int}/communications")]
        public Task<IActionResult> GetCommunications(int id, CancellationToken cancellationToken) =>
            RunAsync(ctx => _communications.GetForLeadAsync(id, ctx, cancellationToken), cancellationToken);

        // ── Internal collaboration (never customer-facing) ──────────────────────────

        [HttpPost("{id:int}/comments")]
        public Task<IActionResult> AddComment(int id, [FromBody] CreateLeadCommentDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _communications.AddCommentAsync(id, dto, ctx, cancellationToken), cancellationToken);

        [HttpGet("{id:int}/comments")]
        public Task<IActionResult> GetComments(int id, CancellationToken cancellationToken) =>
            RunAsync(ctx => _communications.GetCommentsAsync(id, ctx, cancellationToken), cancellationToken);

        // ── Follow-ups and tasks ────────────────────────────────────────────────────

        [HttpPost("{id:int}/follow-ups")]
        public Task<IActionResult> CreateFollowUp(int id, [FromBody] CreateLeadFollowUpDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _followUps.CreateAsync(id, dto, ctx, cancellationToken), cancellationToken);

        [HttpGet("{id:int}/follow-ups")]
        public Task<IActionResult> GetFollowUps(int id, CancellationToken cancellationToken) =>
            RunAsync(ctx => _followUps.GetForLeadAsync(id, ctx, cancellationToken), cancellationToken);

        [HttpPost("follow-ups/{followUpId:int}/complete")]
        public Task<IActionResult> CompleteFollowUp(int followUpId, [FromBody] CompleteLeadFollowUpDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _followUps.CompleteAsync(followUpId, dto, ctx, cancellationToken), cancellationToken);

        [HttpPost("follow-ups/{followUpId:int}/reschedule")]
        public Task<IActionResult> RescheduleFollowUp(int followUpId, [FromBody] RescheduleLeadFollowUpDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _followUps.RescheduleAsync(followUpId, dto, ctx, cancellationToken), cancellationToken);

        [HttpPost("follow-ups/{followUpId:int}/cancel")]
        public Task<IActionResult> CancelFollowUp(int followUpId, [FromBody] CloseSiteVisitDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _followUps.CancelAsync(followUpId, dto.Reason, ctx, cancellationToken), cancellationToken);

        [HttpGet("my/follow-ups")]
        public Task<IActionResult> GetMyFollowUps([FromQuery] bool overdueOnly, CancellationToken cancellationToken) =>
            RunAsync(ctx => _followUps.GetMyFollowUpsAsync(ctx, overdueOnly, cancellationToken), cancellationToken);

        // ── Site visits ─────────────────────────────────────────────────────────────

        [HttpPost("{id:int}/site-visits")]
        public Task<IActionResult> ScheduleSiteVisit(int id, [FromBody] ScheduleSiteVisitDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _siteVisits.ScheduleAsync(id, dto, ctx, cancellationToken), cancellationToken);

        [HttpGet("{id:int}/site-visits")]
        public Task<IActionResult> GetSiteVisits(int id, CancellationToken cancellationToken) =>
            RunAsync(ctx => _siteVisits.GetForLeadAsync(id, ctx, cancellationToken), cancellationToken);

        [HttpPost("site-visits/{visitId:int}/reschedule")]
        public Task<IActionResult> RescheduleSiteVisit(int visitId, [FromBody] RescheduleSiteVisitDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _siteVisits.RescheduleAsync(visitId, dto, ctx, cancellationToken), cancellationToken);

        [HttpPost("site-visits/{visitId:int}/complete")]
        public Task<IActionResult> CompleteSiteVisit(int visitId, [FromBody] CompleteSiteVisitDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _siteVisits.CompleteAsync(visitId, dto, ctx, cancellationToken), cancellationToken);

        [HttpPost("site-visits/{visitId:int}/cancel")]
        public Task<IActionResult> CancelSiteVisit(int visitId, [FromBody] CloseSiteVisitDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _siteVisits.CancelAsync(visitId, dto, ctx, cancellationToken), cancellationToken);

        [HttpPost("site-visits/{visitId:int}/missed")]
        public Task<IActionResult> MarkSiteVisitMissed(int visitId, [FromBody] CloseSiteVisitDto dto, CancellationToken cancellationToken) =>
            RunAsync(ctx => _siteVisits.MarkMissedAsync(visitId, dto, ctx, cancellationToken), cancellationToken);

        [HttpGet("my/site-visits")]
        public Task<IActionResult> GetUpcomingSiteVisits([FromQuery] int days = 7, CancellationToken cancellationToken = default) =>
            RunAsync(ctx => _siteVisits.GetUpcomingAsync(ctx, days, cancellationToken), cancellationToken);

        // ── Documents ───────────────────────────────────────────────────────────────

        [HttpPost("{id:int}/documents")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(LeadDocumentService.MaxRequestSize)]
        public async Task<IActionResult> UploadDocument(
            int id,
            IFormFile file,
            [FromForm] LeadDocumentCategory category,
            [FromForm] string? description,
            [FromForm] int? communicationId,
            CancellationToken cancellationToken)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "Choose a file to upload." });

            try
            {
                var ctx = await CurrentAsync(cancellationToken);
                await using var stream = file.OpenReadStream();
                var upload = new LeadDocumentUpload
                {
                    Content = stream,
                    FileName = file.FileName,
                    ContentType = file.ContentType,
                    Length = file.Length
                };

                var result = await _documents.UploadAsync(id, upload, category, description, communicationId, ctx, cancellationToken);
                return Ok(result);
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
            catch (IOException)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "The document could not be stored. Nothing was saved; please try again." });
            }
        }

        [HttpGet("{id:int}/documents")]
        public Task<IActionResult> GetDocuments(int id, CancellationToken cancellationToken) =>
            RunAsync(ctx => _documents.GetForLeadAsync(id, ctx, cancellationToken), cancellationToken);

        /// <summary>
        /// Streams a lead document. The bytes live outside the web root, so this endpoint is
        /// the only way to read one and the lead's access rules always apply.
        /// </summary>
        [HttpGet("documents/{documentId:int}/download")]
        public async Task<IActionResult> DownloadDocument(int documentId, [FromQuery] bool download, CancellationToken cancellationToken)
        {
            try
            {
                var ctx = await CurrentAsync(cancellationToken);
                var file = await _documents.DownloadAsync(documentId, ctx, cancellationToken);
                return download
                    ? File(file.Content, file.ContentType, file.FileName)
                    : File(file.Content, file.ContentType);
            }
            catch (LeadNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (LeadAuthorizationException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
            }
            catch (FileNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        [HttpDelete("documents/{documentId:int}")]
        public Task<IActionResult> DeleteDocument(int documentId, CancellationToken cancellationToken) =>
            RunAsync(ctx => _documents.DeleteAsync(documentId, ctx, cancellationToken), cancellationToken);
    }
}
