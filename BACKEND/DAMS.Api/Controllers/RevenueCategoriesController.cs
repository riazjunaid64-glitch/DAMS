using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DAMS.Api.Controllers
{
    [ApiController]
    [Route("api/finance/revenue-categories")]
    [Authorize(Roles = "Admin")]
    public sealed class RevenueCategoriesController : ControllerBase
    {
        private readonly IRevenueCategoryService _service;
        public RevenueCategoriesController(IRevenueCategoryService service) => _service = service;

        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default) =>
            Ok(await _service.GetAllAsync(includeInactive, cancellationToken));

        [HttpPost]
        public Task<IActionResult> Create(SaveRevenueCategoryDto dto, CancellationToken cancellationToken) =>
            Execute(() => _service.CreateAsync(dto, UserId(), cancellationToken));

        [HttpPut("{id:int}")]
        public Task<IActionResult> Update(int id, SaveRevenueCategoryDto dto, CancellationToken cancellationToken) =>
            Execute(() => _service.UpdateAsync(id, dto, cancellationToken));

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
        {
            try
            {
                var row = await _service.DeleteAsync(id, cancellationToken);
                return Ok(new { message = "Revenue category retired.", category = row });
            }
            catch (DbUpdateException) { return Conflict(new { message = "The revenue category changed while it was being retired. Refresh and try again." }); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }

        private async Task<IActionResult> Execute<T>(Func<Task<T>> action)
        {
            try { return Ok(await action()); }
            catch (DbUpdateConcurrencyException) { return Conflict(new { message = "This category changed. Refresh and try again." }); }
            catch (DbUpdateException) { return Conflict(new { message = "The category could not be saved because its name or code is already in use." }); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }
        private int? UserId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    }
}
