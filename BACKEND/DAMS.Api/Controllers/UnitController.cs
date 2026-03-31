using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using DAMS.Application.Interfaces;
using DAMS.Application.DTOs.UnitDtos;

namespace DAMS.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UnitController : ControllerBase
    {
        private readonly IUnitService _unitService;

        public UnitController(IUnitService unitService)
        {
            _unitService = unitService;
        }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> Create(CreateUnitDto dto)
    {
        var result = await _unitService.CreateUnitAsync(dto);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("project/{projectId}")]
    public async Task<IActionResult> GetByProject(int projectId)
    {
        var result = await _unitService.GetUnitsByProjectIdAsync(projectId);
        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateUnitDto dto)
    {
        var result = await _unitService.UpdateUnitAsync(id, dto);
        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _unitService.DeleteUnitAsync(id);
        return Ok("Unit deleted successfully");
    }
    }
}