using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Api.Controllers
{
    [ApiController]
    [Route("api/finance/accounts")]
    [Authorize(Roles = "Admin")]
    public sealed class FinanceAccountsController : ControllerBase
    {
        private readonly IFinanceAccountService _service;
        public FinanceAccountsController(IFinanceAccountService service) => _service = service;

        [HttpGet]
        public async Task<IActionResult> GetPage([FromQuery] string? search, [FromQuery] FinanceAccountType? type,
            [FromQuery] string? holder, [FromQuery] bool? isActive, [FromQuery] int skip = 0, [FromQuery] int take = 50,
            CancellationToken cancellationToken = default) =>
            Ok(await _service.GetPageAsync(search, type, holder, isActive, Math.Max(0, skip), Math.Clamp(take, 1, 200), cancellationToken));

        [HttpGet("options")]
        public async Task<IActionResult> GetOptions([FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default) =>
            Ok(await _service.GetOptionsAsync(includeInactive, cancellationToken));

        [HttpGet("overview")]
        public async Task<IActionResult> GetOverview(CancellationToken cancellationToken) => Ok(await _service.GetOverviewAsync(cancellationToken));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken) =>
            await Execute(() => _service.GetByIdAsync(id, cancellationToken), notFound: true);

        [HttpGet("{id:int}/transactions")]
        public async Task<IActionResult> GetTransactions(int id, [FromQuery] int skip = 0, [FromQuery] int take = 100, CancellationToken cancellationToken = default) =>
            await Execute(() => _service.GetTransactionsAsync(id, Math.Max(0, skip), Math.Clamp(take, 1, 200), cancellationToken), notFound: true);

        [HttpPost]
        public async Task<IActionResult> Create(CreateFinanceAccountDto dto, CancellationToken cancellationToken) =>
            await Execute(() => _service.CreateAsync(dto, cancellationToken));

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, UpdateFinanceAccountDto dto, CancellationToken cancellationToken) =>
            await Execute(() => _service.UpdateAsync(id, dto, cancellationToken));

        [HttpPatch("{id:int}/status")]
        public async Task<IActionResult> SetStatus(int id, [FromBody] ChangeStatusDto dto, CancellationToken cancellationToken) =>
            await Execute(() => _service.SetActiveAsync(id, dto.IsActive, dto.ConcurrencyToken, cancellationToken));

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteUnused(int id, CancellationToken cancellationToken)
        {
            try { await _service.DeleteUnusedAsync(id, cancellationToken); return Ok(new { message = "Finance account deleted." }); }
            catch (DbUpdateException) { return Conflict(new { message = "This account is now in use and cannot be deleted. Make it inactive instead." }); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }

        private async Task<IActionResult> Execute<T>(Func<Task<T>> action, bool notFound = false)
        {
            try { return Ok(await action()); }
            catch (DbUpdateConcurrencyException) { return Conflict(new { message = "This account was changed by another user. Refresh and try again." }); }
            catch (DbUpdateException) { return Conflict(new { message = "The account could not be saved because its name is already in use or related data changed. Refresh and try again." }); }
            catch (InvalidOperationException ex) { return notFound ? NotFound(new { message = ex.Message }) : BadRequest(new { message = ex.Message }); }
        }

        public sealed class ChangeStatusDto
        {
            public bool IsActive { get; set; }
            public string ConcurrencyToken { get; set; } = string.Empty;
        }
    }
}
