using DAMS.Application.DTOs.ProjectDtos;
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
        public async Task<IActionResult> UploadProjectMedia(int projectId, IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("Please provide a media file.");
            }

            await using var stream = file.OpenReadStream();
            var result = await _mediaService.UploadProjectMediaAsync(projectId, stream, file.FileName, file.ContentType);
            return Ok(result);
        }

        [Authorize(Roles = "Admin")]
        [HttpDelete("{projectId:int}/media/{mediaId:int}")]
        public async Task<IActionResult> DeleteProjectMedia(int projectId, int mediaId)
        {
            await _mediaService.DeleteProjectMediaAsync(mediaId);
            return Ok("Project media deleted successfully.");
        }
    }
}
