using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DAMS.Domain.Entities;
using DAMS.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Api.Filters
{
    /// <summary>
    /// Makes one money-creating endpoint safe to retry.
    /// <para>
    /// Applied to every request that brings money into existence — a customer receipt, an expense, a
    /// revenue entry, an asset purchase, an FBR deposit, a loan or capital movement, a staff-cash
    /// transfer. The caller sends an <c>Idempotency-Key</c> header that is stable for one intent
    /// (generated when the form opens, kept across retries, replaced for a deliberate second entry).
    /// The first request reserves the key, does the work, and stores its response; a repeat of the
    /// same key is answered with that stored response instead of recording the money again.
    /// </para>
    /// <para>
    /// The failure it exists to stop is not a double click — the forms already block that. It is the
    /// request that reaches the server, commits, and loses its response on the way back: the operator
    /// sees a network error, presses Save again, and a second payment or expense appears.
    /// </para>
    /// <para>
    /// The alternative — a key column on each of the money tables — was rejected: nine migrations,
    /// nine DTO changes and nine service rewrites to protect nine paths, with anything added later
    /// starting out unprotected again. Doing it once at the boundary covers them uniformly and leaves
    /// the finance services themselves untouched. Cancellations, commissions and rebates keep their
    /// own in-table keys: those are business keys chosen per movement, and they guard the replay of a
    /// multi-step settlement rather than just the HTTP call.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class IdempotentMoneyOperationAttribute : Attribute, IFilterFactory
    {
        public bool IsReusable => false;

        public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) =>
            new IdempotentMoneyOperationFilter(
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                serviceProvider.GetRequiredService<ILogger<IdempotentMoneyOperationFilter>>());
    }

    public sealed class IdempotentMoneyOperationFilter : IAsyncActionFilter
    {
        public const string HeaderName = "Idempotency-Key";
        private const int MaxKeyLength = 120;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<IdempotentMoneyOperationFilter> _logger;

        public IdempotentMoneyOperationFilter(
            IServiceScopeFactory scopeFactory, ILogger<IdempotentMoneyOperationFilter> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var request = context.HttpContext.Request;
            var key = request.Headers[HeaderName].ToString().Trim();
            if (string.IsNullOrEmpty(key))
            {
                // Refused rather than waved through: treating an absent header as "no protection
                // wanted" would make retry safety opt-out by simply not sending a field, the same
                // mistake as accepting a missing concurrency token.
                context.Result = new BadRequestObjectResult(new
                {
                    message = $"An {HeaderName} header is required for this request. Reload the page and try again."
                });
                return;
            }
            if (key.Length > MaxKeyLength || key.Any(char.IsControl))
            {
                context.Result = new BadRequestObjectResult(new { message = $"The {HeaderName} header is not valid." });
                return;
            }

            var operation = $"{request.Method} {request.Path.Value}";
            var fingerprint = Fingerprint(context.ActionArguments);
            var userId = int.TryParse(
                context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : (int?)null;

            // A scope of its own, deliberately: the bookkeeping must not share a change tracker or a
            // transaction with the operation it guards. Otherwise a rolled-back operation would take
            // its own reservation down with it — losing the protection exactly when a retry is most
            // likely — and clearing the reservation after a failure could re-submit the pending
            // changes belonging to the operation itself.
            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var reservation = new IdempotentRequest
            {
                Key = key, Operation = operation, Fingerprint = fingerprint, UserId = userId,
                CreatedAt = DateTime.UtcNow
            };
            store.IdempotentRequests.Add(reservation);
            try
            {
                await store.SaveChangesAsync(context.HttpContext.RequestAborted);
            }
            catch (DbUpdateException)
            {
                // The key is already on file: a retry, a duplicate still in flight, or a key reused
                // for something else. The unique index decided that, not a read-then-write.
                store.ChangeTracker.Clear();
                var existing = await store.IdempotentRequests.AsNoTracking()
                    .SingleOrDefaultAsync(r => r.Key == key, context.HttpContext.RequestAborted);
                context.Result = Replay(existing, operation, fingerprint);
                return;
            }

            await next();

            var status = context.Result == null ? context.HttpContext.Response.StatusCode : StatusOf(context.Result);
            var succeeded = status is >= 200 and < 300;
            try
            {
                if (succeeded)
                {
                    reservation.IsCompleted = true;
                    reservation.CompletedAt = DateTime.UtcNow;
                    reservation.StatusCode = status;
                    reservation.ResponseBody = context.Result == null ? null : Body(context.Result);
                }
                else
                {
                    // Rejected input, or a failure: nothing was recorded, so the key has to be free
                    // again. The operator must be able to correct the amount and submit the same
                    // intent, and a transient failure has to stay genuinely retryable.
                    store.IdempotentRequests.Remove(reservation);
                }
                await store.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                // The money is committed either way; this row only lets a retry be answered from
                // memory. Losing it must not turn a successful save into an error the operator sees.
                _logger.LogError(ex, "Failed to finalise idempotency record {Key} for {Operation}.", key, operation);
            }
        }

        /// <summary>How to answer a repeat of a key that is already on file.</summary>
        private static IActionResult Replay(IdempotentRequest? existing, string operation, string fingerprint)
        {
            if (existing == null)
            {
                // Reserved and then released between the failed insert and this read: the other
                // attempt was rejected. Say so rather than invent a result.
                return new ConflictObjectResult(new
                {
                    message = "This request was already being processed. Reload the page to see the current state before trying again."
                });
            }
            if (!string.Equals(existing.Operation, operation, StringComparison.Ordinal)
                || !string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                return new ConflictObjectResult(new
                {
                    message = "This request key was already used for a different operation. Reload the page and enter the record again."
                });
            }
            if (!existing.IsCompleted)
            {
                // Still running, or the process died between committing and recording the response.
                // Both need a human to look: re-running it could double the money.
                return new ConflictObjectResult(new
                {
                    message = "An identical request is still being processed. Reload the page to check whether it was recorded before trying again."
                });
            }
            return new ContentResult
            {
                StatusCode = existing.StatusCode ?? StatusCodes.Status200OK,
                ContentType = "application/json",
                Content = existing.ResponseBody ?? "{}"
            };
        }

        private static int StatusOf(IActionResult result) => result switch
        {
            ObjectResult o => o.StatusCode ?? StatusCodes.Status200OK,
            StatusCodeResult s => s.StatusCode,
            ContentResult c => c.StatusCode ?? StatusCodes.Status200OK,
            _ => StatusCodes.Status200OK
        };

        private static string? Body(IActionResult result) => result switch
        {
            ObjectResult o when o.Value != null => JsonSerializer.Serialize(o.Value, ResponseOptions),
            ContentResult c => c.Content,
            _ => null
        };

        // Matches the API camelCase, enum-as-string output, so a replayed body is identical to the
        // one the first attempt returned.
        private static readonly JsonSerializerOptions ResponseOptions = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };

        private static readonly JsonSerializerOptions FingerprintOptions = new() { WriteIndented = false };

        /// <summary>
        /// A hash of what was asked for, so a retry that quietly changed the amount is caught rather
        /// than answered with the earlier record. Uploads and cancellation tokens are skipped: one
        /// cannot be hashed cheaply, the other is not part of the request.
        /// </summary>
        private static string Fingerprint(IDictionary<string, object?> arguments)
        {
            var parts = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var (name, value) in arguments)
            {
                if (value is IFormFile or IFormFileCollection or CancellationToken or Stream) continue;
                try
                {
                    parts[name] = value == null ? "null" : JsonSerializer.Serialize(value, FingerprintOptions);
                }
                catch (NotSupportedException)
                {
                    parts[name] = value?.GetType().FullName ?? "null";
                }
            }
            var text = string.Join("|", parts.Select(p => p.Key + "=" + p.Value));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        }
    }
}
