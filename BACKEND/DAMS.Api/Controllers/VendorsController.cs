using DAMS.Application.DTOs.WhtDtos;
using DAMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DAMS.Api.Controllers
{
    /// <summary>Suppliers and contractors. Their filer status is what picks the withholding rate.</summary>
    [Route("api/finance/vendors")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class VendorsController : ControllerBase
    {
        private readonly IVendorService _vendors;

        public VendorsController(IVendorService vendors) => _vendors = vendors;

        [HttpGet]
        public async Task<IActionResult> GetPage(
            [FromQuery] string? search,
            [FromQuery] bool activeOnly = false,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 100,
            CancellationToken cancellationToken = default)
        {
            if (skip < 0) skip = 0;
            take = Math.Clamp(take, 1, 200);
            return Ok(await _vendors.GetPageAsync(search, activeOnly, skip, take, cancellationToken));
        }

        [HttpGet("options")]
        public async Task<IActionResult> GetOptions([FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default) =>
            Ok(await _vendors.GetOptionsAsync(includeInactive, cancellationToken));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
        {
            try { return Ok(await _vendors.GetByIdAsync(id, cancellationToken)); }
            catch (InvalidOperationException ex) { return NotFound(new { message = ex.Message }); }
        }

        [HttpGet("{id:int}/ytd-summary")]
        public async Task<IActionResult> GetYearToDate(int id, [FromQuery] DateTime? asOf, CancellationToken cancellationToken)
        {
            try { return Ok(await _vendors.GetYearToDateAsync(id, asOf, cancellationToken)); }
            catch (InvalidOperationException ex) { return NotFound(new { message = ex.Message }); }
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SaveVendorDto dto, CancellationToken cancellationToken)
        {
            try { return Ok(await _vendors.CreateAsync(dto, GetUserId(), cancellationToken)); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SaveVendorDto dto, CancellationToken cancellationToken)
        {
            try { return Ok(await _vendors.UpdateAsync(id, dto, cancellationToken)); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
            catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
            {
                return Conflict(new { message = "This vendor was changed by someone else. Refresh and try again." });
            }
        }

        private int? GetUserId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(claim, out var id) ? id : null;
        }
    }
}
