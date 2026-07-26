using System.Security.Claims;
using System.Text;
using DAMS.Application.Common;
using DAMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DAMS.Api.Controllers
{
    /// <summary>
    /// The live inbox stream, deliberately kept in its own controller.
    ///
    /// A stream is held open for minutes at a time, so it must not hold anything scarce while
    /// it waits. Nothing here touches the database — the subscriber is identified from the
    /// signed token alone, and the messages carry no notification content, only a nudge that
    /// tells the page to re-read its own inbox through the normal authorised endpoint.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/notifications")]
    public class NotificationStreamController : ControllerBase
    {
        private readonly INotificationRealtimeBroker _realtime;
        private readonly NotificationOptions _options;

        public NotificationStreamController(INotificationRealtimeBroker realtime, IOptions<NotificationOptions> options)
        {
            _realtime = realtime;
            _options = options.Value;
        }

        [HttpGet("stream")]
        public async Task Stream(CancellationToken cancellationToken)
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(claim, out var userId) || userId <= 0)
            {
                Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            Response.Headers.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache, no-store";
            // Reverse proxies buffer by default, which would defeat the point of a stream.
            Response.Headers["X-Accel-Buffering"] = "no";
            HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

            await WriteAsync(": connected\n\n", cancellationToken);

            var heartbeat = TimeSpan.FromSeconds(Math.Clamp(_options.StreamHeartbeatSeconds, 5, 120));

            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // A bounded lifetime keeps connections recycling, which also means the client
            // reconnects with a fresh access token rather than holding one open indefinitely.
            lifetime.CancelAfter(TimeSpan.FromMinutes(10));

            var messages = _realtime.SubscribeAsync(userId, lifetime.Token).GetAsyncEnumerator(lifetime.Token);

            // One read is kept in flight across heartbeats. Starting a fresh MoveNextAsync on
            // every tick would leave several outstanding on one enumerator, which is both a
            // second reader on a single-reader channel and — because an async iterator cannot
            // be disposed while a read is pending — an error on the way out.
            Task<bool>? pendingRead = null;

            try
            {
                while (!lifetime.IsCancellationRequested)
                {
                    pendingRead ??= messages.MoveNextAsync().AsTask();
                    var tick = Task.Delay(heartbeat, lifetime.Token);

                    if (await Task.WhenAny(pendingRead, tick) == pendingRead)
                    {
                        var hasMessage = await pendingRead;
                        pendingRead = null;

                        if (!hasMessage)
                            break;

                        await WriteAsync(messages.Current, lifetime.Token);
                    }
                    else
                    {
                        // The keep-alive doubles as the client's cue to reconcile, so a signal
                        // lost to a restart or to a second instance costs one heartbeat rather
                        // than a notification.
                        await WriteAsync(": ping\n\n", lifetime.Token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // The client went away, or the stream reached its lifetime. Both are normal.
            }
            catch (Exception)
            {
                // The response has already started, so there is no status code left to set and
                // nothing useful to say to the client; ending the stream is the only answer.
            }
            finally
            {
                if (pendingRead != null)
                {
                    // Let the outstanding read finish (it faults on cancellation) before the
                    // enumerator is disposed.
                    try { await pendingRead; } catch { /* expected on cancellation */ }
                }

                try { await messages.DisposeAsync(); } catch { /* nothing left to clean up */ }
            }
        }

        private async Task WriteAsync(string payload, CancellationToken cancellationToken)
        {
            await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(payload), cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }
}
