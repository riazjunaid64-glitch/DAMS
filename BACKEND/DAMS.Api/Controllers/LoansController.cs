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
    [Route("api/finance/loans")]
    [Authorize(Roles = "Admin")]
    public sealed class LoansController : ControllerBase
    {
        private readonly ILoanService _service;
        public LoansController(ILoanService service) => _service = service;

        [HttpGet]
        public Task<IActionResult> Get([FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default) =>
            Execute(() => _service.GetAllAsync(includeInactive, cancellationToken));

        [HttpGet("account-options")]
        public Task<IActionResult> AccountOptions([FromQuery] bool includeInactive = true, CancellationToken cancellationToken = default) =>
            Execute(() => _service.GetAccountOptionsAsync(includeInactive, cancellationToken));

        [HttpPost]
        public Task<IActionResult> Create([FromBody] SaveLoanDto dto, CancellationToken cancellationToken) =>
            Execute(() => _service.CreateAsync(dto, cancellationToken));

        [HttpPut("{id:int}")]
        public Task<IActionResult> Update(int id, [FromBody] SaveLoanDto dto, CancellationToken cancellationToken) =>
            Execute(() => _service.UpdateAsync(id, dto, cancellationToken));

        [HttpGet("{id:int}/statement")]
        public Task<IActionResult> Statement(int id, [FromQuery] int skip = 0, [FromQuery] int take = 100,
            CancellationToken cancellationToken = default) =>
            Execute(() => _service.GetStatementAsync(id, skip, take, cancellationToken));

        [IdempotentMoneyOperation]
        [HttpPost("{id:int}/transactions")]
        public Task<IActionResult> Record(int id, [FromBody] SaveLoanTransactionDto dto, CancellationToken cancellationToken) =>
            Execute(() => _service.RecordTransactionAsync(id, dto, UserId(), null, cancellationToken));

        [HttpPut("{id:int}/transactions/{transactionId:int}")]
        public Task<IActionResult> Correct(int id, int transactionId, [FromBody] SaveLoanTransactionDto dto,
            CancellationToken cancellationToken) =>
            Execute(() => _service.UpdateTransactionAsync(id, transactionId, dto, UserId(), null, false, cancellationToken));

        // The multipart twins of the two routes above. Money moving on a loan carries a bank slip
        // exactly as an expense does, and a form post is the only way to send the file with the
        // figures in one request — so the movement and its evidence are saved or rejected together
        // rather than in two steps that can half-succeed.
        [IdempotentMoneyOperation]
        [HttpPost("{id:int}/transactions/form")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(FinanceAttachmentFileValidator.MaxRequestSize)]
        public async Task<IActionResult> RecordWithAttachment(int id, [FromForm] SaveLoanTransactionDto dto,
            [FromForm] IFormFile? attachment, CancellationToken cancellationToken)
        {
            await using var stream = attachment?.OpenReadStream();
            return await Execute(() => _service.RecordTransactionAsync(id, dto, UserId(), ToUpload(attachment, stream), cancellationToken));
        }

        [HttpPut("{id:int}/transactions/{transactionId:int}/form")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(FinanceAttachmentFileValidator.MaxRequestSize)]
        public async Task<IActionResult> CorrectWithAttachment(int id, int transactionId, [FromForm] SaveLoanTransactionDto dto,
            [FromForm] IFormFile? attachment, [FromForm] bool removeAttachment, CancellationToken cancellationToken)
        {
            await using var stream = attachment?.OpenReadStream();
            return await Execute(() => _service.UpdateTransactionAsync(
                id, transactionId, dto, UserId(), ToUpload(attachment, stream), removeAttachment, cancellationToken));
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

        [HttpDelete("{id:int}/transactions/{transactionId:int}")]
        public Task<IActionResult> Delete(int id, int transactionId, [FromQuery] string concurrencyToken,
            CancellationToken cancellationToken) => Execute(async () =>
            {
                await _service.DeleteTransactionAsync(id, transactionId, concurrencyToken, cancellationToken);
                return new { message = "Loan transaction deleted." };
            });

        private async Task<IActionResult> Execute<T>(Func<Task<T>> action)
        {
            try { return Ok(await action()); }
            catch (DbUpdateConcurrencyException) { return Conflict(new { message = "Loan data changed. Refresh and try again." }); }
            catch (DbUpdateException) { return Conflict(new { message = "Loan data conflicts with an existing account link or financial constraint." }); }
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
