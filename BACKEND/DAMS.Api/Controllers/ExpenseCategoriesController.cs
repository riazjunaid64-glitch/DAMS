using DAMS.Application.DTOs.WhtDtos;
using DAMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DAMS.Api.Controllers
{
    /// <summary>
    /// The expense heads and their withholding rates. Admin-only, because changing a rate changes
    /// how much tax is deducted from every payment made afterwards.
    /// </summary>
    [Route("api/finance/expense-categories")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class ExpenseCategoriesController : ControllerBase
    {
        private readonly IExpenseCategoryService _categories;

        public ExpenseCategoriesController(IExpenseCategoryService categories) => _categories = categories;

        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default) =>
            Ok(await _categories.GetAllAsync(includeInactive, cancellationToken));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
        {
            try { return Ok(await _categories.GetByIdAsync(id, cancellationToken)); }
            catch (InvalidOperationException ex) { return NotFound(new { message = ex.Message }); }
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SaveExpenseCategoryDto dto, CancellationToken cancellationToken)
        {
            try { return Ok(await _categories.CreateAsync(dto, GetUserId(), cancellationToken)); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException)
            {
                return Conflict(new { message = "The category could not be saved because its name or code is already in use." });
            }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SaveExpenseCategoryDto dto, CancellationToken cancellationToken)
        {
            try { return Ok(await _categories.UpdateAsync(id, dto, cancellationToken)); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
            catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
            {
                return Conflict(new { message = "This category was changed by someone else. Refresh and try again." });
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException)
            {
                return Conflict(new { message = "The category could not be saved because its name or code is already in use." });
            }
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
        {
            try
            {
                var retired = await _categories.DeleteAsync(id, cancellationToken);
                return Ok(new
                {
                    message = retired == null
                        ? "Expense category deleted."
                        : "Expense category has recorded expenses, so it was retired instead of deleted.",
                    category = retired
                });
            }
            catch (InvalidOperationException ex) { return NotFound(new { message = ex.Message }); }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException)
            {
                // The service only hard-deletes heads nothing referenced, but an expense or asset
                // purchase filed against this head between that check and the delete is refused by
                // the Restrict foreign key. Say so, rather than letting it surface as a 500.
                return Conflict(new { message = "Something was recorded against this category. Refresh and try again." });
            }
        }

        private int? GetUserId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(claim, out var id) ? id : null;
        }
    }
}
