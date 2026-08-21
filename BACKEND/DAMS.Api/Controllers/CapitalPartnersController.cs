using DAMS.Api.Filters;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Interfaces;
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
            Execute(() => _service.RecordTransactionAsync(id, dto, UserId(), cancellationToken));

        private async Task<IActionResult> Execute<T>(Func<Task<T>> action)
        {
            try { return Ok(await action()); }
            catch (DbUpdateConcurrencyException) { return Conflict(new { message = "Partner data changed. Refresh and try again." }); }
            catch (DbUpdateException) { return Conflict(new { message = "Partner data conflicts with an existing name, account link, or financial constraint." }); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }
        private int? UserId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    }
}
