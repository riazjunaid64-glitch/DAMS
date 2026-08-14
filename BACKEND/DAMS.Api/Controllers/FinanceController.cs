using DAMS.Application.DTOs.ExpenseDtos;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DAMS.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class FinanceController : ControllerBase
    {
        private readonly IFinanceService _financeService;

        public FinanceController(IFinanceService financeService)
        {
            _financeService = financeService;
        }

        [HttpGet("profit-and-loss")]
        public Task<IActionResult> GetProfitAndLoss([FromQuery] int? projectId, [FromQuery] DateTime? from,
            [FromQuery] DateTime? to, CancellationToken cancellationToken) =>
            Report(() => _financeService.GetProfitAndLossAsync(projectId, from, to, cancellationToken));

        [HttpGet("profit-and-loss/export")]
        public Task<IActionResult> ExportProfitAndLoss([FromQuery] int? projectId, [FromQuery] DateTime? from,
            [FromQuery] DateTime? to, [FromQuery] string format = "xlsx", CancellationToken cancellationToken = default) =>
            Export(format, () => _financeService.ExportProfitAndLossAsync(projectId, from, to, cancellationToken));

        [HttpGet("trial-balance")]
        public Task<IActionResult> GetTrialBalance([FromQuery] int? projectId, [FromQuery] DateTime? asAt,
            [FromQuery] int monthsBack = 12, CancellationToken cancellationToken = default) =>
            Report(() => _financeService.GetTrialBalanceAsync(projectId, asAt ?? default, monthsBack, cancellationToken));

        [HttpGet("trial-balance/export")]
        public Task<IActionResult> ExportTrialBalance([FromQuery] int? projectId, [FromQuery] DateTime? asAt,
            [FromQuery] int monthsBack = 12, [FromQuery] string format = "xlsx", CancellationToken cancellationToken = default) =>
            Export(format, () => _financeService.ExportTrialBalanceAsync(projectId, asAt ?? default, monthsBack, cancellationToken));

        [HttpGet("balance-sheet")]
        public Task<IActionResult> GetBalanceSheet([FromQuery] int? projectId, [FromQuery] DateTime? asAt,
            CancellationToken cancellationToken = default) =>
            Report(() => _financeService.GetBalanceSheetAsync(projectId, asAt ?? default, cancellationToken));

        [HttpGet("balance-sheet/export")]
        public Task<IActionResult> ExportBalanceSheet([FromQuery] int? projectId, [FromQuery] DateTime? asAt,
            [FromQuery] string format = "xlsx", CancellationToken cancellationToken = default) =>
            Export(format, () => _financeService.ExportBalanceSheetAsync(projectId, asAt ?? default, cancellationToken));

        // Summary cards (totals only). Table rows are fetched separately and paged.
        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary(
            [FromQuery] int? projectId,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] string? account)
        {
            if (!TryParseAccount(account, out var accountId, out var unassigned)) return BadRequest(new { message = "Invalid account filter." });
            var result = await _financeService.GetSummaryAsync(projectId, from, to, accountId, unassigned);
            return Ok(result);
        }

        // One page of table rows for the given view (infinite scroll).
        [HttpGet("rows")]
        public async Task<IActionResult> GetRows(
            [FromQuery] string view,
            [FromQuery] int? projectId,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] string? account,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 100)
        {
            if (skip < 0) skip = 0;
            take = Math.Clamp(take, 1, 200);
            if (!TryParseAccount(account, out var accountId, out var unassigned)) return BadRequest(new { message = "Invalid account filter." });

            return (view?.ToLowerInvariant()) switch
            {
                "revenue" => Ok(await _financeService.GetRevenuePageAsync(projectId, from, to, skip, take, accountId, unassigned)),
                "expense" => Ok(await _financeService.GetExpensePageAsync(projectId, from, to, skip, take, accountId, unassigned)),
                "outstanding" when accountId.HasValue || unassigned => Ok(new PagedResult<OutstandingLineDto>()),
                "outstanding" => Ok(await _financeService.GetOutstandingPageAsync(projectId, skip, take)),
                "overdue" when accountId.HasValue || unassigned => Ok(new PagedResult<OverdueLineDto>()),
                "overdue" => Ok(await _financeService.GetOverduePageAsync(projectId, skip, take)),
                "netprofit" => Ok(await _financeService.GetNetProfitPageAsync(projectId, from, to, skip, take, accountId, unassigned)),
                "assetpurchase" => Ok(await _financeService.GetAssetPurchasePageAsync(projectId, from, to, skip, take, null, accountId, unassigned)),
                _ => BadRequest(new { message = "Unknown view. Use revenue, expense, assetPurchase, outstanding, overdue or netProfit." })
            };
        }

        // ── Manual revenue ──
        [HttpPost("revenue")]
        [Consumes("application/json")]
        public async Task<IActionResult> CreateRevenue([FromBody] CreateManualRevenueDto dto)
        {
            try
            {
                var result = await _financeService.CreateManualRevenueAsync(dto, GetUserId());
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private static bool TryParseAccount(string? value, out int? accountId, out bool unassigned)
        {
            accountId = null; unassigned = false;
            if (string.IsNullOrWhiteSpace(value) || value.Equals("all", StringComparison.OrdinalIgnoreCase)) return true;
            if (value.Equals("unassigned", StringComparison.OrdinalIgnoreCase)) { unassigned = true; return true; }
            if (int.TryParse(value, out var id) && id > 0) { accountId = id; return true; }
            return false;
        }

        [HttpPost("revenue/form")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(FinanceAttachmentFileValidator.MaxRequestSize)]
        public async Task<IActionResult> CreateRevenueWithAttachment(
            [FromForm] CreateManualRevenueDto dto,
            [FromForm] IFormFile? attachment,
            CancellationToken cancellationToken)
        {
            try
            {
                await using var stream = attachment?.OpenReadStream();
                var result = await _financeService.CreateManualRevenueAsync(
                    dto, GetUserId(), ToUpload(attachment, stream), cancellationToken);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (IOException)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "The attachment could not be stored. The revenue record was not created. Please try again." });
            }
        }

        [HttpPut("revenue/{id:int}")]
        [Consumes("application/json")]
        public async Task<IActionResult> UpdateRevenue(int id, [FromBody] UpdateManualRevenueDto dto)
        {
            try
            {
                var result = await _financeService.UpdateManualRevenueAsync(id, dto);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("revenue/{id:int}/form")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(FinanceAttachmentFileValidator.MaxRequestSize)]
        public async Task<IActionResult> UpdateRevenueWithAttachment(
            int id,
            [FromForm] UpdateManualRevenueDto dto,
            [FromForm] IFormFile? attachment,
            [FromForm] bool removeAttachment,
            CancellationToken cancellationToken)
        {
            try
            {
                await using var stream = attachment?.OpenReadStream();
                var result = await _financeService.UpdateManualRevenueAsync(
                    id, dto, ToUpload(attachment, stream), removeAttachment, cancellationToken);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (IOException)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "The replacement attachment could not be stored. No changes were saved. Please try again." });
            }
        }

        [HttpDelete("revenue/{id:int}")]
        public async Task<IActionResult> DeleteRevenue(int id)
        {
            try
            {
                await _financeService.DeleteManualRevenueAsync(id);
                return Ok(new { message = "Manual revenue entry deleted." });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        // ── Expenses ──
        [HttpPost("expenses")]
        [Consumes("application/json")]
        public async Task<IActionResult> CreateExpense([FromBody] CreateExpenseDto dto)
        {
            try
            {
                var result = await _financeService.CreateExpenseAsync(dto, GetUserId());
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("expenses/form")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(FinanceAttachmentFileValidator.MaxRequestSize)]
        public async Task<IActionResult> CreateExpenseWithAttachment(
            [FromForm] CreateExpenseDto dto,
            [FromForm] IFormFile? attachment,
            CancellationToken cancellationToken)
        {
            try
            {
                await using var stream = attachment?.OpenReadStream();
                var result = await _financeService.CreateExpenseAsync(
                    dto, GetUserId(), ToUpload(attachment, stream), cancellationToken);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (IOException)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "The attachment could not be stored. The expense was not created. Please try again." });
            }
        }

        [HttpPut("expenses/{id:int}")]
        [Consumes("application/json")]
        public async Task<IActionResult> UpdateExpense(int id, [FromBody] UpdateExpenseDto dto)
        {
            try
            {
                var result = await _financeService.UpdateExpenseAsync(id, dto);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("expenses/{id:int}/form")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(FinanceAttachmentFileValidator.MaxRequestSize)]
        public async Task<IActionResult> UpdateExpenseWithAttachment(
            int id,
            [FromForm] UpdateExpenseDto dto,
            [FromForm] IFormFile? attachment,
            [FromForm] bool removeAttachment,
            CancellationToken cancellationToken)
        {
            try
            {
                await using var stream = attachment?.OpenReadStream();
                var result = await _financeService.UpdateExpenseAsync(
                    id, dto, ToUpload(attachment, stream), removeAttachment, cancellationToken);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (IOException)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "The replacement attachment could not be stored. No changes were saved. Please try again." });
            }
        }

        [HttpDelete("expenses/{id:int}")]
        public async Task<IActionResult> DeleteExpense(int id)
        {
            try
            {
                await _financeService.DeleteExpenseAsync(id);
                return Ok(new { message = "Expense deleted." });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        // ── Fixed-asset purchases ──
        [HttpPost("asset-purchases")]
        [Consumes("application/json")]
        public async Task<IActionResult> CreateAssetPurchase([FromBody] CreateAssetPurchaseDto dto, CancellationToken cancellationToken)
        {
            try { return Ok(await _financeService.CreateAssetPurchaseAsync(dto, GetUserId(), null, cancellationToken)); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }

        [HttpPost("asset-purchases/form")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(FinanceAttachmentFileValidator.MaxRequestSize)]
        public async Task<IActionResult> CreateAssetPurchaseWithAttachment(
            [FromForm] CreateAssetPurchaseDto dto,
            [FromForm] IFormFile? attachment,
            CancellationToken cancellationToken)
        {
            try
            {
                await using var stream = attachment?.OpenReadStream();
                return Ok(await _financeService.CreateAssetPurchaseAsync(
                    dto, GetUserId(), ToUpload(attachment, stream), cancellationToken));
            }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }

        [HttpPut("asset-purchases/{id:int}")]
        [Consumes("application/json")]
        public async Task<IActionResult> UpdateAssetPurchase(int id, [FromBody] UpdateAssetPurchaseDto dto, CancellationToken cancellationToken)
        {
            try { return Ok(await _financeService.UpdateAssetPurchaseAsync(id, dto, null, false, cancellationToken)); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }

        [HttpPut("asset-purchases/{id:int}/form")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(FinanceAttachmentFileValidator.MaxRequestSize)]
        public async Task<IActionResult> UpdateAssetPurchaseWithAttachment(
            int id,
            [FromForm] UpdateAssetPurchaseDto dto,
            [FromForm] IFormFile? attachment,
            [FromForm] bool removeAttachment,
            CancellationToken cancellationToken)
        {
            try
            {
                await using var stream = attachment?.OpenReadStream();
                return Ok(await _financeService.UpdateAssetPurchaseAsync(
                    id, dto, ToUpload(attachment, stream), removeAttachment, cancellationToken));
            }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }

        [HttpDelete("asset-purchases/{id:int}")]
        public async Task<IActionResult> DeleteAssetPurchase(int id, CancellationToken cancellationToken)
        {
            try
            {
                await _financeService.DeleteAssetPurchaseAsync(id, cancellationToken);
                return Ok(new { message = "Asset purchase deleted." });
            }
            catch (InvalidOperationException ex) { return NotFound(new { message = ex.Message }); }
        }

        [HttpGet("revenue/{id:int}/attachment")]
        public Task<IActionResult> GetRevenueAttachment(int id, [FromQuery] bool download, CancellationToken cancellationToken) =>
            GetAttachment(FinanceRecordKind.Revenue, id, download, cancellationToken);

        [HttpGet("expenses/{id:int}/attachment")]
        public Task<IActionResult> GetExpenseAttachment(int id, [FromQuery] bool download, CancellationToken cancellationToken) =>
            GetAttachment(FinanceRecordKind.Expense, id, download, cancellationToken);

        [HttpGet("asset-purchases/{id:int}/attachment")]
        public Task<IActionResult> GetAssetPurchaseAttachment(int id, [FromQuery] bool download, CancellationToken cancellationToken) =>
            GetAttachment(FinanceRecordKind.AssetPurchase, id, download, cancellationToken);

        [HttpDelete("asset-purchases/{id:int}/attachment")]
        public Task<IActionResult> RemoveAssetPurchaseAttachment(int id, CancellationToken cancellationToken) =>
            RemoveAttachment(FinanceRecordKind.AssetPurchase, id, cancellationToken);

        [HttpDelete("revenue/{id:int}/attachment")]
        public Task<IActionResult> RemoveRevenueAttachment(int id, CancellationToken cancellationToken) =>
            RemoveAttachment(FinanceRecordKind.Revenue, id, cancellationToken);

        [HttpDelete("expenses/{id:int}/attachment")]
        public Task<IActionResult> RemoveExpenseAttachment(int id, CancellationToken cancellationToken) =>
            RemoveAttachment(FinanceRecordKind.Expense, id, cancellationToken);

        private async Task<IActionResult> GetAttachment(
            FinanceRecordKind kind,
            int id,
            bool download,
            CancellationToken cancellationToken)
        {
            try
            {
                var attachment = await _financeService.GetAttachmentAsync(kind, id, cancellationToken);
                Response.Headers.CacheControl = "private, no-store";
                Response.Headers.Append("X-Content-Type-Options", "nosniff");
                Response.Headers.Append("Content-Security-Policy", "sandbox");
                return download
                    ? File(attachment.Content, attachment.ContentType, attachment.FileName, enableRangeProcessing: true)
                    : File(attachment.Content, attachment.ContentType, enableRangeProcessing: true);
            }
            catch (FileNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        private async Task<IActionResult> RemoveAttachment(
            FinanceRecordKind kind,
            int id,
            CancellationToken cancellationToken)
        {
            try
            {
                await _financeService.RemoveAttachmentAsync(kind, id, cancellationToken);
                return Ok(new { message = "Attachment removed." });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
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

        private int? GetUserId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(claim, out var id) ? id : null;
        }

        private async Task<IActionResult> Report<T>(Func<Task<T>> action)
        {
            try { return Ok(await action()); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }

        private async Task<IActionResult> Export(string format, Func<Task<FinanceExportDto>> action)
        {
            if (!string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "Only xlsx export is supported." });
            try
            {
                var file = await action();
                return File(file.Content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.FileName);
            }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }
    }
}
