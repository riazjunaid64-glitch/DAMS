using DAMS.Application.DTOs.ExpenseDtos;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Interfaces;
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

        // Summary cards (totals only). Table rows are fetched separately and paged.
        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary(
            [FromQuery] int? projectId,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to)
        {
            var result = await _financeService.GetSummaryAsync(projectId, from, to);
            return Ok(result);
        }

        // One page of table rows for the given view (infinite scroll).
        [HttpGet("rows")]
        public async Task<IActionResult> GetRows(
            [FromQuery] string view,
            [FromQuery] int? projectId,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 100)
        {
            if (skip < 0) skip = 0;
            take = Math.Clamp(take, 1, 200);

            return (view?.ToLowerInvariant()) switch
            {
                "revenue" => Ok(await _financeService.GetRevenuePageAsync(projectId, from, to, skip, take)),
                "expense" => Ok(await _financeService.GetExpensePageAsync(projectId, from, to, skip, take)),
                "outstanding" => Ok(await _financeService.GetOutstandingPageAsync(projectId, skip, take)),
                "overdue" => Ok(await _financeService.GetOverduePageAsync(projectId, skip, take)),
                "netprofit" => Ok(await _financeService.GetNetProfitPageAsync(projectId, from, to, skip, take)),
                _ => BadRequest(new { message = "Unknown view. Use revenue, expense, outstanding, overdue or netProfit." })
            };
        }

        // ── Manual revenue ──
        [HttpPost("revenue")]
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

        [HttpPut("revenue/{id:int}")]
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

        [HttpPut("expenses/{id:int}")]
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

        private int? GetUserId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(claim, out var id) ? id : null;
        }
    }
}
