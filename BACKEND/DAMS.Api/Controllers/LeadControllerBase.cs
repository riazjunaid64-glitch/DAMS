using DAMS.Application.Common;
using DAMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DAMS.Api.Controllers
{
    /// <summary>
    /// Shared plumbing for the lead workspace: resolves who is calling and turns the
    /// domain's three failure kinds into the right status codes, so individual actions stay
    /// free of repeated try/catch blocks.
    /// </summary>
    [ApiController]
    [Authorize(Roles = LeadRoles.Staff)]
    public abstract class LeadControllerBase : ControllerBase
    {
        private readonly ILeadUserContextResolver _resolver;

        protected LeadControllerBase(ILeadUserContextResolver resolver)
        {
            _resolver = resolver;
        }

        protected Task<LeadUserContext> CurrentAsync(CancellationToken cancellationToken) =>
            _resolver.ResolveAsync(User, cancellationToken);

        protected async Task<IActionResult> RunAsync<T>(Func<LeadUserContext, Task<T>> action, CancellationToken cancellationToken)
        {
            try
            {
                var ctx = await CurrentAsync(cancellationToken);
                return Ok(await action(ctx));
            }
            catch (LeadNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (LeadAuthorizationException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        protected async Task<IActionResult> RunAsync(Func<LeadUserContext, Task> action, CancellationToken cancellationToken)
        {
            try
            {
                var ctx = await CurrentAsync(cancellationToken);
                await action(ctx);
                return NoContent();
            }
            catch (LeadNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (LeadAuthorizationException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
