using DAMS.Application.Common;
using DAMS.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace DAMS.Api
{
    /// <summary>
    /// Drives the notification platform's background work: sending queued deliveries,
    /// releasing scheduled sends, raising installment reminders, closing the gap between a
    /// committed payment and its receipt, and pruning old inbox rows.
    ///
    /// Every sweep is idempotent and every claim is leased, so running this on several
    /// instances at once is safe, and an instance that is killed mid-send is picked up by
    /// whichever instance runs next.
    /// </summary>
    public sealed class NotificationBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly NotificationOptions _options;
        private readonly ILogger<NotificationBackgroundService> _logger;

        public NotificationBackgroundService(
            IServiceScopeFactory scopeFactory,
            IOptions<NotificationOptions> options,
            ILogger<NotificationBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_options.DeliveryIntervalSeconds <= 0)
            {
                _logger.LogWarning(
                    "Notification delivery is disabled (Notifications:DeliveryIntervalSeconds <= 0). " +
                    "Notifications will be recorded but nothing will be emailed or pushed.");
                return;
            }

            var interval = TimeSpan.FromSeconds(_options.DeliveryIntervalSeconds);

            // Let the application finish starting before the first sweep.
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            using var timer = new PeriodicTimer(interval);
            var tick = 0L;

            do
            {
                tick++;

                await SafelyAsync("delivery", async processor =>
                {
                    var sent = await processor.ProcessDueDeliveriesAsync(_options.DeliveryBatchSize, stoppingToken);
                    if (sent > 0)
                        _logger.LogInformation("Processed {Count} notification delivery attempt(s).", sent);
                }, stoppingToken);

                if (ShouldRun(tick, _options.ScheduleIntervalSeconds, interval))
                {
                    await SafelyAsync("schedule", async processor =>
                    {
                        var jobs = await processor.ProcessScheduledJobsAsync(_options.JobBatchSize, stoppingToken);
                        if (jobs > 0)
                            _logger.LogInformation("Released {Count} scheduled notification job(s).", jobs);
                    }, stoppingToken);
                }

                // Reconciliation and reminders are cheap but not urgent; roughly every five
                // minutes is enough to close the payment/notification gap without churn.
                if (ShouldRun(tick, 300, interval))
                {
                    await SafelyEventsAsync(async events =>
                    {
                        var recovered = await events.ReconcilePaymentReceiptsAsync(200, stoppingToken);
                        if (recovered > 0)
                            _logger.LogWarning(
                                "Recovered {Count} payment receipt notification(s) that were not raised at the time of payment.",
                                recovered);

                        var businessEvents = await events.ReconcileBusinessEventsAsync(300, stoppingToken);
                        if (businessEvents > 0)
                            _logger.LogWarning(
                                "Recovered {Count} booking, enquiry or employee-task notification event(s).",
                                businessEvents);

                        var reminders = await events.RunInstallmentRemindersAsync(500, stoppingToken);
                        if (reminders > 0)
                            _logger.LogInformation("Raised {Count} installment reminder(s).", reminders);
                    }, stoppingToken);
                }

                // Housekeeping once an hour.
                if (ShouldRun(tick, 3600, interval))
                {
                    await SafelyAsync("prune", async processor =>
                    {
                        var pruned = await processor.PruneAsync(stoppingToken);
                        if (pruned > 0)
                            _logger.LogInformation("Pruned {Count} expired or long-read notification(s).", pruned);
                    }, stoppingToken);
                }
            }
            while (await WaitAsync(timer, stoppingToken));
        }

        /// <summary>True on the ticks that line up with a slower cadence.</summary>
        private static bool ShouldRun(long tick, int everySeconds, TimeSpan interval)
        {
            if (everySeconds <= 0)
                return false;

            var every = Math.Max(1, (int)Math.Round(everySeconds / interval.TotalSeconds));
            return tick % every == 0;
        }

        private async Task SafelyAsync(string name, Func<INotificationDeliveryProcessor, Task> action, CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await action(scope.ServiceProvider.GetRequiredService<INotificationDeliveryProcessor>());
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A failed sweep must never take the host down; the next tick retries, and
                // every row it was working on is protected by its lease.
                _logger.LogError(ex, "The notification {Sweep} sweep failed. It will run again shortly.", name);
            }
        }

        private async Task SafelyEventsAsync(Func<INotificationEventService, Task> action, CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await action(scope.ServiceProvider.GetRequiredService<INotificationEventService>());
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The notification reconciliation sweep failed. It will run again shortly.");
            }
        }

        private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
        {
            try
            {
                return await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }
}
