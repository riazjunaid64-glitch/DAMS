using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using DAMS.Application.Interfaces;
using DAMS.Application.DTOs.UnitDtos;
using DAMS.Application.DTOs.MediaDtos;

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
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var result = await _unitService.GetUnitByIdAsync(id);
        if (result == null) return NotFound();
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

    // --- MEDIA ENDPOINTS ---
    [AllowAnonymous]
    [HttpGet("{unitId:int}/media")]
    public async Task<IActionResult> GetUnitMedia(int unitId)
    {
        var result = await _mediaService.GetUnitMediaAsync(unitId);
        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{unitId:int}/media")]
    public async Task<IActionResult> UploadUnitMedia(int unitId, IFormFile file, [FromForm] UploadMediaDto? uploadDto = null)
    {
        if (file == null || file.Length == 0) return BadRequest("Please provide a media file.");
        await using var stream = file.OpenReadStream();
        var result = await _mediaService.UploadUnitMediaAsync(unitId, stream, file.FileName, file.ContentType, uploadDto);
        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{unitId:int}/media/bulk")]
    public async Task<IActionResult> UploadUnitMediaBulk(int unitId, List<IFormFile> files, [FromForm] string? category = null, [FromForm] bool isCover = false, [FromForm] string? altText = null, [FromForm] string? description = null)
    {
        if (files == null || files.Count == 0) return BadRequest("Please provide at least one media file.");

        var uploadFiles = new List<(Stream, string, string, UploadMediaDto?)>();
        var uploadDto = new UploadMediaDto
        {
            Category = category != null ? Enum.Parse<Domain.Enums.MediaCategory>(category) : Domain.Enums.MediaCategory.Gallery,
            IsCover = isCover,
            AltText = altText,
            Description = description
        };

        foreach (var file in files)
        {
            if (file != null && file.Length > 0)
            {
                uploadFiles.Add((file.OpenReadStream(), file.FileName, file.ContentType, uploadDto));
            }
        }

        var result = await _mediaService.UploadUnitMediaBulkAsync(unitId, uploadFiles);
        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{unitId:int}/media/{mediaId:int}")]
    public async Task<IActionResult> UpdateUnitMedia(int unitId, int mediaId, UpdateMediaDto updateDto)
    {
        var result = await _mediaService.UpdateUnitMediaAsync(unitId, mediaId, updateDto);
        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{unitId:int}/media/{mediaId:int}")]
    public async Task<IActionResult> DeleteUnitMedia(int unitId, int mediaId)
    {
        await _mediaService.DeleteUnitMediaAsync(unitId, mediaId);
        return Ok("Unit media deleted successfully.");
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{unitId:int}/media/reorder")]
    public async Task<IActionResult> ReorderUnitMedia(int unitId, [FromBody] List<int> mediaIds)
    {
        await _mediaService.ReorderUnitMediaAsync(unitId, mediaIds);
        return Ok("Unit media reordered successfully.");
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{unitId:int}/media/{mediaId:int}/set-cover")]
    public async Task<IActionResult> SetUnitCoverMedia(int unitId, int mediaId)
    {
        await _mediaService.SetUnitCoverMediaAsync(unitId, mediaId);
        return Ok("Cover media set successfully.");
    }
    }
}