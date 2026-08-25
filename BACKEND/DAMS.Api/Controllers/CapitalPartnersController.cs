using DAMS.Api.Filters;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DAMS.Api.Controllers
{
    [ApiController]
    [Route("api/finance/partners")]
    [Authorize(Roles = "Admin")]
    public sealed class CapitalPartnersController : ControllerBase
    {
        private readonly ICapitalPartnerService _service;
        public CapitalPartnersController(ICapitalPartnerService service) => _service = service;
        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default) =>
            Ok(await _service.GetAllAsync(includeInactive, cancellationToken));
        [HttpPost]
        public Task<IActionResult> Create(SaveCapitalPartnerDto dto, CancellationToken cancellationToken) =>
            Execute(() => _service.CreateAsync(dto, cancellationToken));
        [HttpPut("{id:int}")]
        public Task<IActionResult> Update(int id, SaveCapitalPartnerDto dto, CancellationToken cancellationToken) =>
            Execute(() => _service.UpdateAsync(id, dto, cancellationToken));
        [HttpPut("shares")]
        public Task<IActionResult> UpdateShares(SaveCapitalPartnerSharesDto dto, CancellationToken cancellationToken) =>
            Execute(() => _service.UpdateSharesAsync(dto, cancellationToken));
        [HttpGet("{id:int}/statement")]
        public Task<IActionResult> Statement(int id, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken cancellationToken) =>
            Execute(() => _service.GetStatementAsync(id, from, to, cancellationToken));
        [IdempotentMoneyOperation]
        [HttpPost("{id:int}/transactions")]
        public Task<IActionResult> Transaction(int id, SaveCapitalTransactionDto dto, CancellationToken cancellationToken) =>
            Execute(() => _service.RecordTransactionAsync(id, dto, UserId(), null, cancellationToken));

        // The multipart twin: money a partner puts in or takes out carries a bank slip like any
        // other movement, and sending it with the figures keeps the record and its evidence in one
        // request that either succeeds or fails whole.
        [IdempotentMoneyOperation]
        [HttpPost("{id:int}/transactions/form")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(FinanceAttachmentFileValidator.MaxRequestSize)]
        public async Task<IActionResult> TransactionWithAttachment(int id, [FromForm] SaveCapitalTransactionDto dto,
            [FromForm] IFormFile? attachment, CancellationToken cancellationToken)
        {
            await using var stream = attachment?.OpenReadStream();
            return await Execute(() => _service.RecordTransactionAsync(id, dto, UserId(), ToUpload(attachment, stream), cancellationToken));
        }

        [HttpGet("{id:int}/transactions/{transactionId:int}/attachment")]
        public async Task<IActionResult> GetTransactionAttachment(int id, int transactionId,
            [FromQuery] bool download, CancellationToken cancellationToken)
        {
            try
            {
                var file = await _service.GetTransactionAttachmentAsync(id, transactionId, cancellationToken);
                Response.Headers.CacheControl = "private, no-store";
                Response.Headers.Append("X-Content-Type-Options", "nosniff");
                Response.Headers.Append("Content-Security-Policy", "sandbox");
                return download
                    ? File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: true)
                    : File(file.Content, file.ContentType, enableRangeProcessing: true);
            }
            catch (FileNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        }

        [HttpDelete("{id:int}/transactions/{transactionId:int}/attachment")]
        public Task<IActionResult> RemoveTransactionAttachment(int id, int transactionId,
            CancellationToken cancellationToken) => Execute(async () =>
            {
                await _service.RemoveTransactionAttachmentAsync(id, transactionId, cancellationToken);
                return new { message = "Attachment removed." };
            });

        private async Task<IActionResult> Execute<T>(Func<Task<T>> action)
        {
            try { return Ok(await action()); }
            catch (DbUpdateConcurrencyException) { return Conflict(new { message = "Partner data changed. Refresh and try again." }); }
            catch (DbUpdateException) { return Conflict(new { message = "Partner data conflicts with an existing name, account link, or financial constraint." }); }
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
        private int? UserId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    }
}
