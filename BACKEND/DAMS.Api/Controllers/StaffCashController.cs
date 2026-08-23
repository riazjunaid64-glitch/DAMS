using System.Security.Claims;
using DAMS.Api.Filters;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Api.Controllers
{
    [ApiController]
    [Route("api/finance/staff-cash")]
    [Authorize(Roles = "Admin")]
    public sealed class StaffCashController : ControllerBase
    {
        private readonly IStaffCashService _service;
        public StaffCashController(IStaffCashService service) => _service = service;

        [HttpGet]
        public Task<IActionResult> Overview(
            [FromQuery] bool includeSettled = true,
            CancellationToken cancellationToken = default) =>
            Execute(() => _service.GetOverviewAsync(includeSettled, cancellationToken));

        [HttpPost("holders")]
        public Task<IActionResult> CreateHolder(
            [FromBody] CreateStaffCashHolderDto dto,
            CancellationToken cancellationToken) =>
            Execute(() => _service.CreateHolderAsync(dto, cancellationToken));

        [HttpGet("{staffFinanceAccountId:int}")]
        public Task<IActionResult> Statement(
            int staffFinanceAccountId,
            [FromQuery] string? cursor = null,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 100,
            CancellationToken cancellationToken = default)
        {
            if (skip > 0 && string.IsNullOrWhiteSpace(cursor))
                return Task.FromResult<IActionResult>(BadRequest(new
                {
                    message = "Staff cash history now uses cursor paging. Refresh the page and try again."
                }));
            return Execute(() => _service.GetStatementAsync(
                staffFinanceAccountId, cursor, Math.Clamp(take, 1, 200), cancellationToken));
        }

        [IdempotentMoneyOperation]
        [HttpPost("{staffFinanceAccountId:int}/transfers")]
        public Task<IActionResult> RecordTransfer(
            int staffFinanceAccountId,
            [FromBody] SaveStaffCashTransferDto dto,
            CancellationToken cancellationToken) =>
            Execute(() => _service.RecordTransferAsync(staffFinanceAccountId, dto, UserId(), null, cancellationToken));

        [HttpPut("{staffFinanceAccountId:int}/transfers/{transferId:int}")]
        public Task<IActionResult> UpdateTransfer(
            int staffFinanceAccountId,
            int transferId,
            [FromBody] SaveStaffCashTransferDto dto,
            CancellationToken cancellationToken) =>
            Execute(() => _service.UpdateTransferAsync(
                staffFinanceAccountId, transferId, dto, UserId(), null, false, cancellationToken));

        [HttpDelete("{staffFinanceAccountId:int}/transfers/{transferId:int}")]
        public Task<IActionResult> DeleteTransfer(
            int staffFinanceAccountId,
            int transferId,
            [FromQuery] string concurrencyToken,
            CancellationToken cancellationToken) => Execute(async () =>
            {
                await _service.DeleteTransferAsync(
                    staffFinanceAccountId, transferId, concurrencyToken, cancellationToken);
                return new { message = "Staff cash transfer deleted." };
            });

        // The multipart twins. Handing cash to a person is the movement with the least paper trail
        // of its own, so the slip has to travel with the figures rather than in a second request
        // that can fail on its own.
        [IdempotentMoneyOperation]
        [HttpPost("{staffFinanceAccountId:int}/transfers/form")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(FinanceAttachmentFileValidator.MaxRequestSize)]
        public async Task<IActionResult> RecordTransferWithAttachment(
            int staffFinanceAccountId,
            [FromForm] SaveStaffCashTransferDto dto,
            [FromForm] IFormFile? attachment,
            CancellationToken cancellationToken)
        {
            await using var stream = attachment?.OpenReadStream();
            return await Execute(() => _service.RecordTransferAsync(
                staffFinanceAccountId, dto, UserId(), ToUpload(attachment, stream), cancellationToken));
        }

        [HttpPut("{staffFinanceAccountId:int}/transfers/{transferId:int}/form")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(FinanceAttachmentFileValidator.MaxRequestSize)]
        public async Task<IActionResult> UpdateTransferWithAttachment(
            int staffFinanceAccountId,
            int transferId,
            [FromForm] SaveStaffCashTransferDto dto,
            [FromForm] IFormFile? attachment,
            [FromForm] bool removeAttachment,
            CancellationToken cancellationToken)
        {
            await using var stream = attachment?.OpenReadStream();
            return await Execute(() => _service.UpdateTransferAsync(
                staffFinanceAccountId, transferId, dto, UserId(), ToUpload(attachment, stream), removeAttachment, cancellationToken));
        }

        [HttpGet("{staffFinanceAccountId:int}/transfers/{transferId:int}/attachment")]
        public async Task<IActionResult> GetTransferAttachment(
            int staffFinanceAccountId, int transferId, [FromQuery] bool download, CancellationToken cancellationToken)
        {
            try
            {
                var file = await _service.GetTransferAttachmentAsync(staffFinanceAccountId, transferId, cancellationToken);
                Response.Headers.CacheControl = "private, no-store";
                Response.Headers.Append("X-Content-Type-Options", "nosniff");
                Response.Headers.Append("Content-Security-Policy", "sandbox");
                return download
                    ? File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: true)
                    : File(file.Content, file.ContentType, enableRangeProcessing: true);
            }
            catch (FileNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        }

        [HttpDelete("{staffFinanceAccountId:int}/transfers/{transferId:int}/attachment")]
        public Task<IActionResult> RemoveTransferAttachment(
            int staffFinanceAccountId, int transferId, CancellationToken cancellationToken) => Execute(async () =>
            {
                await _service.RemoveTransferAttachmentAsync(staffFinanceAccountId, transferId, cancellationToken);
                return new { message = "Attachment removed." };
            });

        private async Task<IActionResult> Execute<T>(Func<Task<T>> action)
        {
            try { return Ok(await action()); }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict(new { message = "Staff cash data changed. Refresh and try again." });
            }
            catch (DbUpdateException)
            {
                return Conflict(new { message = "The staff cash entry conflicts with existing financial data." });
            }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
            catch (IOException)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "The attachment could not be stored. Nothing was saved. Please try again." });
            }
        }

        private static FinanceAttachmentUpload? ToUpload(IFormFile? file, Stream? stream) =>
            file == null || stream == null
                ? null
                : new FinanceAttachmentUpload
                {
                    Content = stream,
                    FileName = file.FileName,
                    ContentType = file.ContentType,
                    Length = file.Length
                };

        private int? UserId() => int.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    }
}
