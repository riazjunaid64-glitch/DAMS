using System.Security.Claims;
using DAMS.Application.Common;
using DAMS.Application.DTOs.CommissionRebateDtos;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using DAMS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace DAMS.Api.Controllers
{
    [ApiController]
    [Route("api/finance/commissions-rebates")]
    [Authorize(Roles = "Admin")]
    public sealed class CommissionRebatesController : ControllerBase
    {
        private readonly ICommissionRebateService _service;
        public CommissionRebatesController(ICommissionRebateService service) => _service = service;

        [HttpGet("summary")]
        public Task<IActionResult> Summary(CancellationToken cancellationToken) =>
            Run(() => _service.GetSummaryAsync(cancellationToken));

        [HttpGet("partners")]
        public Task<IActionResult> Partners([FromQuery] string? search, [FromQuery] bool? isActive,
            [FromQuery] int skip = 0, [FromQuery] int take = 25, CancellationToken cancellationToken = default) =>
            Run(() => _service.GetPartnersAsync(search, isActive, skip, take, cancellationToken));

        [HttpPost("partners")]
        public Task<IActionResult> CreatePartner([FromBody] SaveThirdPartyPartnerDto dto, CancellationToken cancellationToken) =>
            Run(() => _service.CreatePartnerAsync(dto, Actor(), cancellationToken));

        [HttpPut("partners/{id:int}")]
        public Task<IActionResult> UpdatePartner(int id, [FromBody] SaveThirdPartyPartnerDto dto, CancellationToken cancellationToken) =>
            Run(() => _service.UpdatePartnerAsync(id, dto, Actor(), cancellationToken));

        [HttpPatch("partners/{id:int}/status")]
        public Task<IActionResult> PartnerStatus(int id, [FromBody] SetPartnerStatusDto dto, CancellationToken cancellationToken) =>
            Run(() => _service.SetPartnerStatusAsync(id, dto, Actor(), cancellationToken));

        [HttpPost("attributions")]
        public Task<IActionResult> CreateAttribution([FromBody] SaveThirdPartyAttributionDto dto, CancellationToken cancellationToken) =>
            Run(() => _service.SaveAttributionAsync(null, dto, Actor(), cancellationToken));

        [HttpPut("attributions/{id:int}")]
        public Task<IActionResult> UpdateAttribution(int id, [FromBody] SaveThirdPartyAttributionDto dto,
            CancellationToken cancellationToken) => Run(() => _service.SaveAttributionAsync(id, dto, Actor(), cancellationToken));

        [HttpGet("rules")]
        public Task<IActionResult> Rules([FromQuery] bool? isActive, CancellationToken cancellationToken) =>
            Run(() => _service.GetRulesAsync(isActive, cancellationToken));

        [HttpPost("rules")]
        public Task<IActionResult> CreateRule([FromBody] SaveCommissionRuleDto dto, CancellationToken cancellationToken) =>
            Run(() => _service.CreateRuleAsync(dto, Actor(), cancellationToken));

        [HttpPut("rules/{id:int}")]
        public Task<IActionResult> UpdateRule(int id, [FromBody] SaveCommissionRuleDto dto, CancellationToken cancellationToken) =>
            Run(() => _service.UpdateRuleAsync(id, dto, Actor(), cancellationToken));

        [HttpGet("commissions")]
        public Task<IActionResult> Commissions([FromQuery] BookingCommissionStatus? status, [FromQuery] int? partnerId,
            [FromQuery] int? projectId, CancellationToken cancellationToken) =>
            Run(() => _service.GetCommissionsAsync(status, partnerId, projectId, cancellationToken));

        [HttpGet("rebates")]
        public Task<IActionResult> Rebates([FromQuery] CustomerRebateStatus? status, [FromQuery] int? projectId,
            CancellationToken cancellationToken) => Run(() => _service.GetRebatesAsync(status, projectId, cancellationToken));

        [HttpGet("bookings/{bookingId:int}")]
        public Task<IActionResult> BookingWorkspace(int bookingId, CancellationToken cancellationToken) =>
            Run(() => _service.GetBookingWorkspaceAsync(bookingId, cancellationToken));

        [HttpPost("bookings/{bookingId:int}/commissions")]
        public Task<IActionResult> CreateCommission(int bookingId, [FromBody] CreateBookingCommissionDto dto,
            CancellationToken cancellationToken) => Run(() => _service.CreateCommissionAsync(bookingId, dto, Actor(), cancellationToken));

        [HttpPost("bookings/{bookingId:int}/commissions/{commissionId:int}/status")]
        public Task<IActionResult> CommissionStatus(int bookingId, int commissionId, [FromBody] CommissionStatusChangeDto dto,
            CancellationToken cancellationToken) =>
            Run(() => _service.ChangeCommissionStatusAsync(bookingId, commissionId, dto, Actor(), cancellationToken));

        [HttpPost("bookings/{bookingId:int}/commissions/{commissionId:int}/payouts")]
        public Task<IActionResult> RecordPayout(int bookingId, int commissionId, [FromBody] RecordCommissionPayoutDto dto,
            CancellationToken cancellationToken) =>
            Run(() => _service.RecordPayoutAsync(bookingId, commissionId, dto, Actor(), cancellationToken));

        [HttpPost("bookings/{bookingId:int}/commissions/{commissionId:int}/payouts/{payoutId:int}/reversals")]
        public Task<IActionResult> ReversePayout(int bookingId, int commissionId, int payoutId,
            [FromBody] ReverseMoneyMovementDto dto, CancellationToken cancellationToken) =>
            Run(() => _service.ReversePayoutAsync(bookingId, commissionId, payoutId, dto, Actor(), cancellationToken));

        [HttpPost("bookings/{bookingId:int}/rebates")]
        public Task<IActionResult> CreateRebate(int bookingId, [FromBody] CreateCustomerRebateDto dto,
            CancellationToken cancellationToken) => Run(() => _service.CreateRebateAsync(bookingId, dto, Actor(), cancellationToken));

        [HttpPost("bookings/{bookingId:int}/rebates/{rebateId:int}/status")]
        public Task<IActionResult> RebateStatus(int bookingId, int rebateId, [FromBody] RebateStatusChangeDto dto,
            CancellationToken cancellationToken) =>
            Run(() => _service.ChangeRebateStatusAsync(bookingId, rebateId, dto, Actor(), cancellationToken));

        [HttpPost("bookings/{bookingId:int}/rebates/{rebateId:int}/disbursements")]
        public Task<IActionResult> RecordRebate(int bookingId, int rebateId, [FromBody] RecordRebateDisbursementDto dto,
            CancellationToken cancellationToken) =>
            Run(() => _service.RecordRebateDisbursementAsync(bookingId, rebateId, dto, Actor(), cancellationToken));

        [HttpPost("bookings/{bookingId:int}/rebates/{rebateId:int}/disbursements/{disbursementId:int}/reversals")]
        public Task<IActionResult> ReverseRebate(int bookingId, int rebateId, int disbursementId,
            [FromBody] ReverseMoneyMovementDto dto, CancellationToken cancellationToken) =>
            Run(() => _service.ReverseRebateDisbursementAsync(bookingId, rebateId, disbursementId, dto, Actor(), cancellationToken));

        [HttpPost("evidence/{ownerType}/{ownerId:int}")]
        [RequestSizeLimit(FinanceAttachmentFileValidator.MaxRequestSize)]
        public async Task<IActionResult> UploadEvidence(FinancialEvidenceOwnerType ownerType, int ownerId,
            [FromForm] IFormFile? file, CancellationToken cancellationToken)
        {
            if (file == null) return BadRequest(new { message = "Choose an evidence file." });
            try
            {
                await using var stream = file.OpenReadStream();
                return Ok(await _service.UploadEvidenceAsync(ownerType, ownerId, new FinancialEvidenceUpload
                {
                    Content = stream, FileName = file.FileName, Length = file.Length
                }, Actor(), cancellationToken));
            }
            catch (Exception ex) when (Expected(ex)) { return Failure(ex); }
        }

        [HttpGet("evidence/{evidenceId:int}/file")]
        public async Task<IActionResult> DownloadEvidence(int evidenceId, [FromQuery] bool download = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await _service.DownloadEvidenceAsync(evidenceId, Actor(), cancellationToken);
                Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
                Response.Headers[HeaderNames.CacheControl] = "no-store, no-cache, must-revalidate";
                return download ? File(result.Content, result.ContentType, result.FileName, enableRangeProcessing: true)
                    : File(result.Content, result.ContentType, enableRangeProcessing: true);
            }
            catch (Exception ex) when (Expected(ex)) { return Failure(ex); }
        }

        private async Task<IActionResult> Run<T>(Func<Task<T>> operation)
        {
            try { return Ok(await operation()); }
            catch (Exception ex) when (Expected(ex)) { return Failure(ex); }
        }

        private FinancialWorkflowActor Actor()
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
                throw new UnauthorizedAccessException("The signed-in account could not be verified.");
            return new FinancialWorkflowActor(id, User.FindFirstValue(ClaimTypes.Name)
                ?? User.FindFirstValue(ClaimTypes.Email) ?? "Admin");
        }

        private static bool Expected(Exception ex) => ex is InvalidOperationException or KeyNotFoundException
            or FileNotFoundException or DbUpdateConcurrencyException or DbUpdateException or UnauthorizedAccessException;

        private IActionResult Failure(Exception ex) => ex switch
        {
            DbUpdateConcurrencyException => Conflict(new { message = "This financial record changed. Refresh and try again." }),
            DbUpdateException => Conflict(new { message = "The record conflicts with existing financial data. Refresh and try again." }),
            KeyNotFoundException or FileNotFoundException => NotFound(new { message = ex.Message }),
            UnauthorizedAccessException => Forbid(),
            _ => BadRequest(new { message = ex.Message })
        };
    }
}
