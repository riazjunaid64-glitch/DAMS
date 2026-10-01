using System.Security.Claims;
using DAMS.Application.Common;
using DAMS.Application.DTOs.CustomerDocumentDtos;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using DAMS.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace DAMS.Api.Controllers
{
    [ApiController]
    [Route("api/customer-documents")]
    [Authorize(Policy = DamsPolicies.Customers)]
    public class CustomerDocumentsController : ControllerBase
    {
        private readonly ICustomerDocumentService _documents;

        public CustomerDocumentsController(ICustomerDocumentService documents)
        {
            _documents = documents;
        }

        [HttpGet("categories")]
        public Task<IActionResult> GetSetup(CancellationToken cancellationToken = default) =>
            RunAsync(() => _documents.GetSetupAsync(cancellationToken));

        [HttpPost("categories")]
        public Task<IActionResult> CreateDocument([FromBody] SaveDocumentNameDto dto, CancellationToken cancellationToken) =>
            RunAsync(() => _documents.CreateDocumentAsync(dto, Actor(), cancellationToken));

        [HttpPut("categories/{id:int}")]
        public Task<IActionResult> RenameDocument(int id, [FromBody] SaveDocumentNameDto dto, CancellationToken cancellationToken) =>
            RunAsync(() => _documents.RenameDocumentAsync(id, dto, Actor(), cancellationToken));

        [HttpDelete("categories/{id:int}")]
        public async Task<IActionResult> RemoveDocument(int id, CancellationToken cancellationToken)
        {
            try
            {
                await _documents.RemoveDocumentAsync(id, Actor(), cancellationToken);
                return Ok(new { message = "Document removed." });
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                return Expected(ex);
            }
        }

        [HttpPut("categories/{id:int}/ask-every-customer")]
        public Task<IActionResult> SetAsksEveryCustomer(int id, [FromBody] AskEveryCustomerDto dto, CancellationToken cancellationToken) =>
            RunAsync(() => _documents.SetAsksEveryCustomerAsync(id, dto.AsksEveryCustomer, Actor(), cancellationToken));

        [HttpGet("customers/{customerId:int}")]
        public Task<IActionResult> GetChecklist(int customerId, CancellationToken cancellationToken) =>
            RunAsync(() => _documents.GetChecklistAsync(customerId, cancellationToken));

        [HttpGet("customers/{customerId:int}/history")]
        public Task<IActionResult> GetHistory(int customerId,
            [FromQuery] int? beforeId = null, [FromQuery] int take = 50, CancellationToken cancellationToken = default) =>
            RunAsync(() => _documents.GetHistoryAsync(customerId, beforeId, take, cancellationToken));

        [HttpGet("customers/{customerId:int}/requirements/{requirementId:int}/versions")]
        public Task<IActionResult> GetVersions(int customerId, int requirementId,
            [FromQuery] int? beforeVersionNumber = null, [FromQuery] int take = 20,
            CancellationToken cancellationToken = default) =>
            RunAsync(() => _documents.GetVersionsAsync(customerId, requirementId, beforeVersionNumber, take,
                cancellationToken));

        [HttpPost("customers/{customerId:int}/documents")]
        [RequestSizeLimit(CustomerDocumentService.MaxRequestSize)]
        public async Task<IActionResult> AddDocument(
            int customerId,
            [FromForm] IFormFile? file,
            [FromForm] int? categoryId,
            [FromForm] string? name,
            CancellationToken cancellationToken)
        {
            if (file == null)
                return BadRequest(new { message = "Choose a document to upload." });

            try
            {
                await using var stream = file.OpenReadStream();
                var result = await _documents.AddDocumentAsync(customerId, categoryId, name,
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

        [HttpPost("customers/{customerId:int}/requirements/{requirementId:int}/not-needed")]
        public Task<IActionResult> MarkNotNeeded(
            int customerId,
            int requirementId,
            [FromBody] NotNeededDocumentDto dto,
            CancellationToken cancellationToken) =>
            RunAsync(() => _documents.MarkNotNeededAsync(customerId, requirementId, dto, Actor(), cancellationToken));

        [HttpGet("customers/{customerId:int}/requirements/{requirementId:int}/versions/{versionId:int}/view")]
        public async Task<IActionResult> View(
            int customerId,
            int requirementId,
            int versionId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await _documents.ViewAsync(customerId, requirementId, versionId, Actor(), cancellationToken);
                AddPrivateFileHeaders();
                return File(result.Content, result.ContentType, enableRangeProcessing: true);
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                return Expected(ex);
            }
        }

        [HttpGet("customers/{customerId:int}/requirements/{requirementId:int}/versions/{versionId:int}/file")]
        public async Task<IActionResult> Download(
            int customerId,
            int requirementId,
            int versionId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await _documents.DownloadAsync(customerId, requirementId, versionId, Actor(), cancellationToken);
                AddPrivateFileHeaders();
                // Always an attachment, so a validated-but-hostile file can never render inline in an
                // authenticated admin session on direct navigation.
                return File(result.Content, result.ContentType, result.FileName, enableRangeProcessing: true);
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                return Expected(ex);
            }
        }

        private void AddPrivateFileHeaders()
        {
            Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
            Response.Headers[HeaderNames.CacheControl] = "no-store, no-cache, must-revalidate";
            Response.Headers[HeaderNames.ContentSecurityPolicy] = "default-src 'none'; sandbox";
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
            DbUpdateConcurrencyException or CustomerDocumentConflictException => Conflict(new { message = ex.Message }),
            KeyNotFoundException or FileNotFoundException => NotFound(new { message = ex.Message }),
            UnauthorizedAccessException => Forbid(),
            _ => BadRequest(new { message = ex.Message })
        };
    }
}
