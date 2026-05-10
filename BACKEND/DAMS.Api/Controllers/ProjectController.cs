using DAMS.Application.DTOs.ProjectDtos;
using DAMS.Application.DTOs.MediaDtos;
using DAMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DAMS.Api.Controllers
{

  // In future I will made some changes but as I have to make speed for university so for now it is ok

    [Route("api/[controller]")]
    [ApiController]
    public class ProjectController : ControllerBase
    {
        private readonly IProjectService _projectService;
        private readonly IMediaService _mediaService;

        public ProjectController(IProjectService projectService, IMediaService mediaService)
        {
            _projectService = projectService;
            _mediaService = mediaService;
        }

        // CREATE PROJECT (Admin only)
        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> CreateProject(CreateProjectDto dto)
        {
            var adminId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var result = await _projectService.CreateProjectAsync(dto, adminId);

            return Ok(result);
        }

        // UPDATE PROJECT (Admin only)
        [Authorize(Roles = "Admin")]
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateProject(int id, UpdateProjectDto dto)
        {
            var result = await _projectService.UpdateProjectAsync(id, dto);

            return Ok(result);
        }

        // GET ALL PROJECTS (Public)
        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> GetAllProjects()
        {
            var result = await _projectService.GetAllProjectsAsync();
            return Ok(result);
        }

        // GET PROJECT BY ID (Public)
        [AllowAnonymous]
        [HttpGet("{id}")]
        public async Task<IActionResult> GetProjectById(int id)
        {
            var result = await _projectService.GetProjectByIdAsync(id);

            if (result == null)
                return NotFound();

            return Ok(result);
        }

        [AllowAnonymous]
        [HttpGet("{projectId:int}/media")]
        public async Task<IActionResult> GetProjectMedia(int projectId)
        {
            var result = await _mediaService.GetProjectMediaAsync(projectId);
            return Ok(result);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("{projectId:int}/media")]
        public async Task<IActionResult> UploadProjectMedia(int projectId, IFormFile file, [FromForm] UploadMediaDto? uploadDto = null)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("Please provide a media file.");
            }

            await using var stream = file.OpenReadStream();
            var result = await _mediaService.UploadProjectMediaAsync(projectId, stream, file.FileName, file.ContentType, uploadDto);
            return Ok(result);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("{projectId:int}/media/bulk")]
        public async Task<IActionResult> UploadProjectMediaBulk(int projectId, List<IFormFile> files, [FromForm] string? category = null, [FromForm] bool isCover = false, [FromForm] string? altText = null, [FromForm] string? description = null)
        {
            if (files == null || files.Count == 0)
            {
                return BadRequest("Please provide at least one media file.");
            }

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

            var result = await _mediaService.UploadProjectMediaBulkAsync(projectId, uploadFiles);
            return Ok(result);
        }

        [Authorize(Roles = "Admin")]
        [HttpPut("{projectId:int}/media/{mediaId:int}")]
        public async Task<IActionResult> UpdateProjectMedia(int projectId, int mediaId, UpdateMediaDto updateDto)
        {
            var result = await _mediaService.UpdateProjectMediaAsync(mediaId, updateDto);
            return Ok(result);
        }

        [Authorize(Roles = "Admin")]
        [HttpDelete("{projectId:int}/media/{mediaId:int}")]
        public async Task<IActionResult> DeleteProjectMedia(int projectId, int mediaId)
        {
            await _mediaService.DeleteProjectMediaAsync(mediaId);
            return Ok("Project media deleted successfully.");
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("{projectId:int}/media/reorder")]
        public async Task<IActionResult> ReorderProjectMedia(int projectId, [FromBody] List<int> mediaIds)
        {
            await _mediaService.ReorderProjectMediaAsync(projectId, mediaIds);
            return Ok("Project media reordered successfully.");
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("{projectId:int}/media/{mediaId:int}/set-cover")]
        public async Task<IActionResult> SetProjectCoverMedia(int projectId, int mediaId)
        {
            await _mediaService.SetProjectCoverMediaAsync(projectId, mediaId);
            return Ok("Cover media set successfully.");
        }
    }
}
