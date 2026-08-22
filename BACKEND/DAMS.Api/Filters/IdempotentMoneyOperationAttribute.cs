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
            var fingerprint = await FingerprintAsync(context.ActionArguments, context.HttpContext.RequestAborted);
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
            catch (DbUpdateException ex) when (IsDuplicateKey(ex))
            {
                // The key is already on file: a retry, a duplicate still in flight, or a key reused
                // for something else. The unique index decided that, not a read-then-write.
                store.ChangeTracker.Clear();
                var existing = await store.IdempotentRequests.AsNoTracking()
                    .SingleOrDefaultAsync(r => r.Key == key, context.HttpContext.RequestAborted);
                context.Result = Replay(existing, operation, fingerprint);
                return;
            }

            var executed = await next();

            // The outcome is on the context next() RETURNS. The one passed in only carries a result
            // when a filter short-circuits BEFORE the action runs — MVC never copies the action's own
            // result onto it — and at this point that result has not been executed either, so
            // Response.StatusCode is still its unwritten default of 200. Reading those two therefore
            // recorded EVERY outcome as an empty success: a rejected amount marked the key done and
            // answered the operator's corrected retry with 200 {}, and a genuine save stored no body
            // at all, so the retry this whole mechanism exists for replayed {} instead of the record
            // it had just created.
            var status = OutcomeOf(executed);
            try
            {
                if (status is >= 200 and < 300)
                {
                    reservation.IsCompleted = true;
                    reservation.CompletedAt = DateTime.UtcNow;
                    reservation.StatusCode = status;
                    reservation.ResponseBody = Body(executed.Result);
                }
                else if (status < 500)
                {
                    // Understood and refused: nothing was recorded, so the key has to be free again.
                    // The operator must be able to correct the amount and submit the same intent.
                    store.IdempotentRequests.Remove(reservation);
                }
                else
                {
                    // A fault, and it is NOT known whether the money was written — the exception can
                    // just as easily have come after the service committed as before. Releasing the
                    // key here would let the retry post the same amount a second time, which is the
                    // precise failure this filter exists to prevent. So the reservation stays,
                    // uncompleted, and a retry gets the 409 that asks a human to check first.
                    reservation.StatusCode = status;
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

        /// <summary>
        /// Whether the reservation failed because <c>IX_IdempotentRequests_Key</c> rejected it, and
        /// not for some other reason.
        /// <para>
        /// Narrow on purpose. Catching every <see cref="DbUpdateException"/> meant a full log, a
        /// dropped connection or a schema problem was reported to the operator as "this request was
        /// already processed" — a message that says the opposite of what happened and invites them to
        /// stop retrying something that never ran. 2601 and 2627 are SQL Server's unique-index and
        /// unique-constraint violations; anything else propagates and is answered as the fault it is.
        /// </para>
        /// </summary>
        private static bool IsDuplicateKey(DbUpdateException ex) =>
            ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 };

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
                // Still running, or it faulted, or the process died between committing and recording
                // the response. None of the three can say whether the money was written, and all
                // three need a human to look: re-running blind could double it.
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

        /// <summary>
        /// The status the caller will actually receive. An exception the action did not handle never
        /// becomes a result at all — the exception middleware turns it into a 500 further out — so it
        /// has to be read from the exception rather than from a result that is still null here.
        /// </summary>
        private static int OutcomeOf(ActionExecutedContext executed) =>
            executed.Exception != null && !executed.ExceptionHandled
                ? StatusCodes.Status500InternalServerError
                : StatusOf(executed.Result);

        private static int StatusOf(IActionResult? result) => result switch
        {
            ObjectResult o => o.StatusCode ?? StatusCodes.Status200OK,
            StatusCodeResult s => s.StatusCode,
            ContentResult c => c.StatusCode ?? StatusCodes.Status200OK,
            _ => StatusCodes.Status200OK
        };

        private static string? Body(IActionResult? result) => result switch
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
        /// A hash of what was asked for, so a retry that quietly changed the submission is caught
        /// rather than answered with the earlier record.
        /// <para>
        /// The uploaded file is part of "what was asked for", and it used to be skipped. That made
        /// the attachment invisible to the guard: an operator who saved an expense, saw the request
        /// fail on the way back, noticed they had attached the wrong invoice, swapped the file and
        /// pressed Save again got a 200 replayed from the FIRST attempt — same amount, same key, and
        /// the original wrong attachment still on the record, with the screen reporting success. The
        /// file's identity is now inside the fingerprint, so a changed attachment reads as a
        /// different submission (409, "reload and enter it again") while a genuine retry of the
        /// exact same bytes still replays as before.
        /// </para>
        /// <para>
        /// Identity is the content hash, not the file name: two uploads of the same document under
        /// different names ARE the same submission, and the same name over different content is not.
        /// Hashing costs one pass over a payload the server has already received in full, and the
        /// upload limits cap it.
        /// </para>
        /// </summary>
        private static async Task<string> FingerprintAsync(
            IDictionary<string, object?> arguments, CancellationToken cancellationToken)
        {
            var parts = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var (name, value) in arguments)
            {
                if (value is CancellationToken or Stream) continue;
                if (value is IFormFile file)
                {
                    parts[name] = await FileIdentityAsync(file, cancellationToken);
                    continue;
                }
                if (value is IFormFileCollection files)
                {
                    var identities = new List<string>();
                    foreach (var one in files) identities.Add(await FileIdentityAsync(one, cancellationToken));
                    // Ordered as submitted: two files swapped between two fields is a different
                    // submission, and sorting here would hide that.
                    parts[name] = string.Join(",", identities);
                    continue;
                }
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

        /// <summary>
        /// One uploaded file's identity: its length and a hash of its content.
        /// <para>
        /// Read through <see cref="IFormFile.OpenReadStream"/>, which hands out a fresh reader over
        /// the buffered payload — the action's own read is unaffected, which is the whole reason this
        /// can run before the action instead of guessing from metadata.
        /// </para>
        /// </summary>
        private static async Task<string> FileIdentityAsync(IFormFile file, CancellationToken cancellationToken)
        {
            await using var stream = file.OpenReadStream();
            var hash = await SHA256.HashDataAsync(stream, cancellationToken);
            return $"{file.Length}:{Convert.ToHexString(hash)}";
        }
    }
}
