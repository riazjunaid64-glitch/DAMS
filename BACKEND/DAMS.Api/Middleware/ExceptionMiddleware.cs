using System.Net;
using System.Text.Json;

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
            _logger.LogError(ex, "Unhandled Exception");

            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";

            var message = ex switch
            {
                Microsoft.Data.SqlClient.SqlException sqlEx when sqlEx.Number == -2 =>
                    "Database connection timed out. Ensure SQL Server (SQLEXPRESS) is running and responsive, then retry.",
                Microsoft.Data.SqlClient.SqlException =>
                    "Database error. Check that migrations are applied and SQL Server is available.",
                InvalidOperationException opEx => opEx.Message,
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
