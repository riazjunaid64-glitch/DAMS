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
        private readonly IMediaService _mediaService;

        public UnitController(IUnitService unitService, IMediaService mediaService)
        {
            _unitService = unitService;
            _mediaService = mediaService;
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

    [AllowAnonymous]
    [HttpGet("{unitId:int}/media")]
    public async Task<IActionResult> GetUnitMedia(int unitId)
    {
        var result = await _mediaService.GetUnitMediaAsync(unitId);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("project/{projectId:int}/media")]
    public async Task<IActionResult> GetUnitMediaByProject(int projectId)
    {
        var result = await _mediaService.GetUnitMediaByProjectAsync(projectId);
        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{unitId:int}/media")]
    public async Task<IActionResult> UploadUnitMedia(int unitId, IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("Please provide a media file.");
        }

        await using var stream = file.OpenReadStream();
        var result = await _mediaService.UploadUnitMediaAsync(unitId, stream, file.FileName, file.ContentType);
        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{unitId:int}/media/{mediaId:int}")]
    public async Task<IActionResult> DeleteUnitMedia(int unitId, int mediaId)
    {
        await _mediaService.DeleteUnitMediaAsync(mediaId);
        return Ok("Unit media deleted successfully.");
    }
    }
}
