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
            Execute(() => _service.RecordTransactionAsync(id, dto, UserId(), cancellationToken));

        [HttpPut("{id:int}/transactions/{transactionId:int}")]
        public Task<IActionResult> Correct(int id, int transactionId, [FromBody] SaveLoanTransactionDto dto,
            CancellationToken cancellationToken) =>
            Execute(() => _service.UpdateTransactionAsync(id, transactionId, dto, UserId(), cancellationToken));

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
        }

        private int? UserId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    }
}
