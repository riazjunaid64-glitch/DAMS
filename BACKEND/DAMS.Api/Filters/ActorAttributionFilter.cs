using System.Security.Claims;
using DAMS.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DAMS.Api.Filters
{
    /// <summary>
    /// Tells the DbContext whose request it is serving, so the finance correction trail can name the
    /// admin who changed a figure. Applied to every action rather than to the finance ones, because
    /// an editable money record can be reached from more than one controller.
    /// <para>
    /// The actor is cleared again when the action finishes. Contexts come from a pool, and a pool does
    /// not reset a property it does not know about — leaving the value behind would let a later
    /// borrower attribute a change to whoever happened to be served before it. No actor is a correct
    /// answer for background work; the wrong actor never is.
    /// </para>
    /// </summary>
    public sealed class ActorAttributionFilter : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var db = context.HttpContext.RequestServices.GetService<AppDbContext>();
            if (db == null)
            {
                await next();
                return;
            }
            db.ActorUserId = int.TryParse(
                context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
            try
            {
                await next();
            }
            finally
            {
                db.ActorUserId = null;
            }
        }
    }
}
