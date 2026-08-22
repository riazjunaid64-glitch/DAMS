using DAMS.Api.Filters;
using DAMS.Application.DTOs.WhtDtos;
using DAMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DAMS.Api.Controllers
{
    /// <summary>
    /// Withholding tax: the live figure behind the expense form, the finance settings, the
    /// s.165 reporting surface, and the record of what has been deposited with FBR.
    /// </summary>
    [Route("api/finance/wht")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class WhtController : ControllerBase
    {
        private readonly IWhtService _wht;

        public WhtController(IWhtService wht) => _wht = wht;

        /// <summary>Preview only — nothing is saved. Called as the operator changes category,
        /// vendor or amount so the tax is visible before the expense is committed.</summary>
        [HttpPost("calculate")]
        public async Task<IActionResult> Calculate([FromBody] WhtCalculationRequestDto request, CancellationToken cancellationToken)
        {
            try { return Ok(await _wht.CalculateAsync(request, cancellationToken)); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }

        [HttpGet("settings")]
        public async Task<IActionResult> GetSettings(CancellationToken cancellationToken) =>
            Ok(await _wht.GetSettingsAsync(cancellationToken));

        [HttpPut("settings")]
        public async Task<IActionResult> UpdateSettings([FromBody] SaveFinanceSettingsDto dto, CancellationToken cancellationToken)
        {
            try { return Ok(await _wht.UpdateSettingsAsync(dto, User.FindFirstValue(ClaimTypes.Email), cancellationToken)); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
            catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
            {
                return Conflict(new { message = "Finance settings were changed by someone else. Refresh and try again." });
            }
        }

        [HttpGet("payable-summary")]
        public async Task<IActionResult> GetPayableSummary(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken cancellationToken) =>
            Ok(await _wht.GetPayableSummaryAsync(from, to, cancellationToken));

        /// <summary>Per-vendor, per-section totals — the shape of the s.165 withholding statement
        /// and of the certificates vendors need in order to claim the credit.</summary>
        [HttpGet("by-vendor")]
        public async Task<IActionResult> GetByVendor(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken cancellationToken) =>
            Ok(await _wht.GetByVendorAsync(from, to, cancellationToken));

        [HttpGet("export")]
        public async Task<IActionResult> Export(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken cancellationToken)
        {
            var (fileName, content) = await _wht.ExportAsync(from, to, cancellationToken);
            Response.Headers.CacheControl = "private, no-store";
            Response.Headers.Append("X-Content-Type-Options", "nosniff");
            return File(content, "text/csv", fileName);
        }

        [HttpGet("deposits")]
        public async Task<IActionResult> GetDeposits(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken cancellationToken) =>
            Ok(await _wht.GetDepositsAsync(from, to, cancellationToken));

        [IdempotentMoneyOperation]
        [HttpPost("deposits")]
        public async Task<IActionResult> CreateDeposit([FromBody] SaveWhtDepositDto dto, CancellationToken cancellationToken)
        {
            try { return Ok(await _wht.CreateDepositAsync(dto, GetUserId(), cancellationToken)); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }

        [HttpPut("deposits/{id:int}")]
        public async Task<IActionResult> UpdateDeposit(int id, [FromBody] SaveWhtDepositDto dto, CancellationToken cancellationToken)
        {
            try { return Ok(await _wht.UpdateDepositAsync(id, dto, cancellationToken)); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
            catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
            {
                return Conflict(new { message = "This deposit was changed by someone else. Refresh and try again." });
            }
        }

        [HttpDelete("deposits/{id:int}")]
        public async Task<IActionResult> DeleteDeposit(
            int id, [FromQuery] string? concurrencyToken, CancellationToken cancellationToken)
        {
            try
            {
                await _wht.DeleteDepositAsync(id, concurrencyToken, cancellationToken);
                return Ok(new { message = "WHT deposit deleted." });
            }
            // A missing or malformed token is a stale client, not a missing record — the version
            // guard reports it the same way editing does rather than as a 404.
            catch (InvalidOperationException ex) when (ex.Message.Contains("record version"))
            {
                return Conflict(new { message = ex.Message });
            }
            catch (InvalidOperationException ex) { return NotFound(new { message = ex.Message }); }
            catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
            {
                return Conflict(new { message = "This deposit was changed by someone else. Refresh and try again." });
            }
        }

        private int? GetUserId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(claim, out var id) ? id : null;
        }
    }
}
