using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using DAMS.Application.Interfaces;

namespace DAMS.Application.Services.Notifications
{
    /// <summary>
    /// Fans new-notification signals out to the streams online users hold open.
    ///
    /// Two deliberate limits. The signal carries no notification content — only "something
    /// changed" — so the stream can never become a way to read data the recipient's own inbox
    /// query would not return. And it is in-process: on a multi-instance deployment a user
    /// connected to instance A will not get a live nudge from instance B, but their stream's
    /// periodic reconciliation still picks the notification up, because the permanent inbox,
    /// not this broker, is the source of truth.
    /// </summary>
    public sealed class NotificationRealtimeBroker : INotificationRealtimeBroker
    {
        private readonly ConcurrentDictionary<int, ConcurrentDictionary<Guid, Channel<string>>> _subscribers = new();

        public int ConnectionCount => _subscribers.Values.Sum(c => c.Count);

        public async IAsyncEnumerable<string> SubscribeAsync(
            int userId, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var id = Guid.NewGuid();
            // Bounded and drop-oldest: a client that stops reading slows nothing down, and the
            // signal it misses is recovered by the stream's own reconciliation tick.
            var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(16)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });

            var connections = _subscribers.GetOrAdd(userId, _ => new ConcurrentDictionary<Guid, Channel<string>>());
            connections[id] = channel;

            try
            {
                await foreach (var message in channel.Reader.ReadAllAsync(cancellationToken))
                    yield return message;
            }
            finally
            {
                connections.TryRemove(id, out _);
                if (connections.IsEmpty)
                    _subscribers.TryRemove(userId, out _);
            }
        }

        public Task PublishAsync(int userId, string eventName, object payload)
        {
            if (!_subscribers.TryGetValue(userId, out var connections) || connections.IsEmpty)
                return Task.CompletedTask;

            var message = $"event: {eventName}\ndata: {JsonSerializer.Serialize(payload)}\n\n";

            foreach (var channel in connections.Values)
                channel.Writer.TryWrite(message);

            return Task.CompletedTask;
        }
    }
}
