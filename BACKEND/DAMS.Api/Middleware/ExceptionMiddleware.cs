using System.Net;
using System.Text.Json;
using DAMS.Application.Common;

namespace DAMS.Api.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public ExceptionMiddleware(RequestDelegate next,
                               ILogger<ExceptionMiddleware> logger,
                               IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            // A streamed response (the notification event stream) has already sent its
            // headers, so there is no status code left to set and no body shape to honour.
            // Log it and let the connection end rather than throwing a second, more confusing
            // exception on top of the first.
            if (context.Response.HasStarted)
            {
                _logger.LogWarning(ex, "An error occurred after the response had started; the connection was closed.");
                return;
            }

            // Access decisions are expected outcomes, not faults: log them quietly and answer
            // with the right status instead of a 500.
            if (ex is LeadAuthorizationException or LeadNotFoundException)
            {
                _logger.LogInformation("Lead access denied or not found: {Message}", ex.Message);
                context.Response.StatusCode = ex is LeadAuthorizationException
                    ? (int)HttpStatusCode.Forbidden
                    : (int)HttpStatusCode.NotFound;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(
                    JsonSerializer.Serialize(new { success = false, message = ex.Message }));
                return;
            }

            _logger.LogError(ex, "Unhandled Exception");

            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";

            var message = ex switch
            {
                Microsoft.Data.SqlClient.SqlException sqlEx when sqlEx.Number == -2 =>
                    "Database connection timed out. Ensure SQL Server (SQLEXPRESS) is running and responsive, then retry.",
                Microsoft.Data.SqlClient.SqlException =>
                    "Database error. Check that migrations are applied and SQL Server is available.",
                InvalidOperationException opEx => _env.IsDevelopment() ? opEx.Message : "The operation could not be completed.",
                _ => _env.IsDevelopment() ? ex.Message : "An unexpected error occurred."
            };

            var response = new
            {
                success = false,
                message
            };

            await context.Response.WriteAsync(
                JsonSerializer.Serialize(response));
        }
    }
}
