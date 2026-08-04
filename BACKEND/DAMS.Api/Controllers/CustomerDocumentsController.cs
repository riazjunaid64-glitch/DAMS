using System.Security.Claims;
using DAMS.Application.Common;
using DAMS.Application.DTOs.CustomerDocumentDtos;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace DAMS.Api.Controllers
{
    [ApiController]
    [Route("api/customer-documents")]
    [Authorize(Roles = "Admin")]
    public class CustomerDocumentsController : ControllerBase
    {
        private readonly ICustomerDocumentService _documents;

        public CustomerDocumentsController(ICustomerDocumentService documents)
        {
            _documents = documents;
        }

        [HttpGet("categories")]
        public Task<IActionResult> GetCategories([FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default) =>
            RunAsync(() => _documents.GetCategoriesAsync(includeInactive, cancellationToken));

        [HttpPost("categories")]
        public Task<IActionResult> CreateCategory([FromBody] CreateCustomerDocumentCategoryDto dto, CancellationToken cancellationToken) =>
            RunAsync(() => _documents.CreateCategoryAsync(dto, Actor(), cancellationToken));

        [HttpPut("categories/{id:int}")]
        public Task<IActionResult> UpdateCategory(int id, [FromBody] UpdateCustomerDocumentCategoryDto dto, CancellationToken cancellationToken) =>
            RunAsync(() => _documents.UpdateCategoryAsync(id, dto, Actor(), cancellationToken));

        [HttpDelete("categories/{id:int}")]
        public async Task<IActionResult> DeleteCategory(int id, CancellationToken cancellationToken)
        {
            try
            {
                await _documents.DeleteCategoryAsync(id, Actor(), cancellationToken);
                return Ok(new { message = "Document category deleted." });
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                return Expected(ex);
            }
        }

        [HttpPost("categories/{id:int}/assign")]
        public Task<IActionResult> AssignCategory(int id, [FromBody] AssignCustomerDocumentCategoryDto dto, CancellationToken cancellationToken) =>
            RunAsync(() => _documents.AssignCategoryAsync(id, dto, Actor(), cancellationToken));

        [HttpGet("customers/{customerId:int}")]
        public Task<IActionResult> GetChecklist(int customerId, CancellationToken cancellationToken) =>
            RunAsync(() => _documents.GetChecklistAsync(customerId, cancellationToken));

        [HttpPost("customers/{customerId:int}/requirements")]
        public Task<IActionResult> AddRequirement(
            int customerId,
            [FromBody] AddCustomerDocumentRequirementDto dto,
            CancellationToken cancellationToken) =>
            RunAsync(() => _documents.AddRequirementAsync(customerId, dto, Actor(), cancellationToken));

        [HttpPost("customers/{customerId:int}/requirements/{requirementId:int}/upload")]
        [RequestSizeLimit(CustomerDocumentService.MaxRequestSize)]
        public async Task<IActionResult> Upload(
            int customerId,
            int requirementId,
            [FromForm] IFormFile? file,
            [FromForm] string concurrencyToken,
            CancellationToken cancellationToken)
        {
            if (file == null)
                return BadRequest(new { message = "Choose a document to upload." });

            try
            {
                await using var stream = file.OpenReadStream();
                var result = await _documents.UploadAsync(customerId, requirementId, concurrencyToken,
                    new CustomerDocumentUpload
                    {
                        Content = stream,
                        FileName = file.FileName,
                        Length = file.Length
                    }, Actor(), cancellationToken);
                return Ok(result);
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                return Expected(ex);
            }
        }

        [HttpPost("customers/{customerId:int}/requirements/{requirementId:int}/status")]
        public Task<IActionResult> ChangeStatus(
            int customerId,
            int requirementId,
            [FromBody] CustomerDocumentStatusChangeDto dto,
            CancellationToken cancellationToken) =>
            RunAsync(() => _documents.ChangeStatusAsync(customerId, requirementId, dto, Actor(), cancellationToken));

        [HttpPut("customers/{customerId:int}/requirements/{requirementId:int}/due-date")]
        public Task<IActionResult> ChangeDueDate(
            int customerId,
            int requirementId,
            [FromBody] CustomerDocumentDueDateDto dto,
            CancellationToken cancellationToken) =>
            RunAsync(() => _documents.ChangeDueDateAsync(customerId, requirementId, dto, Actor(), cancellationToken));

        [HttpGet("customers/{customerId:int}/requirements/{requirementId:int}/versions/{versionId:int}/file")]
        public async Task<IActionResult> Download(
            int customerId,
            int requirementId,
            int versionId,
            [FromQuery] bool download = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await _documents.DownloadAsync(customerId, requirementId, versionId, Actor(), cancellationToken);
                Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
                Response.Headers[HeaderNames.CacheControl] = "no-store, no-cache, must-revalidate";
                return download
                    ? File(result.Content, result.ContentType, result.FileName, enableRangeProcessing: true)
                    : File(result.Content, result.ContentType, enableRangeProcessing: true);
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                return Expected(ex);
            }
        }

        private async Task<IActionResult> RunAsync<T>(Func<Task<T>> action)
        {
            try { return Ok(await action()); }
            catch (Exception ex) when (IsExpected(ex)) { return Expected(ex); }
        }

        private CustomerDocumentActor Actor()
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
                throw new UnauthorizedAccessException("The signed-in account could not be verified.");
            return new CustomerDocumentActor(id,
                User.FindFirstValue(ClaimTypes.Name)
                ?? User.FindFirstValue(ClaimTypes.Email)
                ?? "Admin");
        }

        private static bool IsExpected(Exception ex) => ex is
            InvalidOperationException or KeyNotFoundException or FileNotFoundException
            or DbUpdateConcurrencyException or UnauthorizedAccessException;

        private IActionResult Expected(Exception ex) => ex switch
        {
            DbUpdateConcurrencyException => Conflict(new { message = ex.Message }),
            KeyNotFoundException or FileNotFoundException => NotFound(new { message = ex.Message }),
            UnauthorizedAccessException => Forbid(),
            _ => BadRequest(new { message = ex.Message })
        };
    }
}
