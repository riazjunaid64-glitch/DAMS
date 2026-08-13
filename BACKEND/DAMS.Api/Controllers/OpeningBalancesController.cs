using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DAMS.Api.Controllers
{
    [ApiController]
    [Route("api/finance/opening-balances")]
    [Authorize(Roles = "Admin")]
    public sealed class OpeningBalancesController : ControllerBase
    {
        private readonly IOpeningBalanceService _service;
        public OpeningBalancesController(IOpeningBalanceService service) => _service = service;
        [HttpGet("current")]
        public async Task<IActionResult> Current(CancellationToken cancellationToken) =>
            Ok(await _service.GetCurrentAsync(cancellationToken));
        [HttpPost]
        public Task<IActionResult> Create(CreateOpeningBalanceSetDto dto, CancellationToken cancellationToken) =>
            Execute(() => _service.CreateAsync(dto.AsAtDate, UserId(), cancellationToken));
        [HttpPut("{id:int}")]
        public Task<IActionResult> Save(int id, SaveOpeningBalanceSetDto dto, CancellationToken cancellationToken) =>
            Execute(() => _service.SaveAsync(id, dto, UserId(), cancellationToken));
        [HttpPost("{id:int}/commit")]
        public Task<IActionResult> Commit(int id, [FromBody] ConcurrencyDto dto, CancellationToken cancellationToken) =>
            Execute(() => _service.CommitAsync(id, dto.ConcurrencyToken, UserId(), cancellationToken));
        [HttpPost("{id:int}/reopen")]
        public Task<IActionResult> Reopen(int id, ReopenOpeningBalanceSetDto dto, CancellationToken cancellationToken) =>
            Execute(() => _service.ReopenAsync(id, dto, UserId(), cancellationToken));

        private async Task<IActionResult> Execute<T>(Func<Task<T>> action)
        {
            try { return Ok(await action()); }
            catch (DbUpdateConcurrencyException) { return Conflict(new { message = "Opening balances changed. Refresh and try again." }); }
            catch (DbUpdateException) { return Conflict(new { message = "Opening balances conflict with current account data. Refresh and try again." }); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }
        private int? UserId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
        public sealed class ConcurrencyDto { public string ConcurrencyToken { get; set; } = string.Empty; }
    }
}
