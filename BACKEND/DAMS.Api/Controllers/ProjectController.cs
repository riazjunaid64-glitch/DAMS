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

        public ProjectController(IProjectService projectService)
        {
            _projectService = projectService;
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
    }
}
