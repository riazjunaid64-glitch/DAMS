using DAMS.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace DAMS.Api.Controllers
{
    /// <summary>503 with Retry-After for a lead write that could not take its contact lock in time.</summary>
    internal static class BusyResponse
    {
        public static IActionResult From(ControllerBase controller, LeadIntakeBusyException ex)
        {
            controller.Response.Headers.RetryAfter = LeadIntakeBusyException.RetryAfterSeconds.ToString();
            return controller.StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
    }
}
