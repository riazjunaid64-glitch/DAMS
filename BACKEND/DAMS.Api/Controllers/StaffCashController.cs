using System.Security.Claims;
using DAMS.Api.Filters;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Interfaces;
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
            Execute(() => _service.RecordTransferAsync(staffFinanceAccountId, dto, UserId(), cancellationToken));

        [HttpPut("{staffFinanceAccountId:int}/transfers/{transferId:int}")]
        public Task<IActionResult> UpdateTransfer(
            int staffFinanceAccountId,
            int transferId,
            [FromBody] SaveStaffCashTransferDto dto,
            CancellationToken cancellationToken) =>
            Execute(() => _service.UpdateTransferAsync(
                staffFinanceAccountId, transferId, dto, UserId(), cancellationToken));

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
        }

        private int? UserId() => int.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    }
}
