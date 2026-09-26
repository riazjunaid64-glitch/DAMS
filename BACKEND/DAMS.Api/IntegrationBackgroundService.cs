using DAMS.Application.Common;
using DAMS.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace DAMS.Api
{
    /// <summary>
    /// Drives everything the Meta integration does outside a request: draining the webhook
    /// inbox, refreshing discovered resources, alerting Admins when lead capture needs them,
    /// and clearing expired OAuth states.
    ///
    /// One service rather than three, because the notification worker already established
    /// cadence gating for exactly this, and because resource sync and event processing must
    /// not run concurrently for the same connection.
    ///
    /// Every claim is leased and every sweep is idempotent, so several instances can run at
    /// once and an instance killed mid-fetch is picked up by whichever runs next.
    /// </summary>
    public sealed class IntegrationBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly MetaIntegrationOptions _options;
        private readonly ILogger<IntegrationBackgroundService> _logger;

        public IntegrationBackgroundService(
            IServiceScopeFactory scopeFactory,
            IOptions<MetaIntegrationOptions> options,
            ILogger<IntegrationBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_options.EventIntervalSeconds <= 0)
            {
                _logger.LogWarning(
                    "Meta integration processing is disabled (MetaIntegration:EventIntervalSeconds <= 0). " +
                    "Webhook events will be recorded but no leads will be created from them.");
                return;
            }

            // An unconfigured integration is a normal state, not a fault: DAMS runs perfectly
            // well without Meta, so say so once and stop rather than logging every tick.
            if (!_options.IsConfigured)
            {
                _logger.LogInformation(
                    "The Meta integration is not configured, so its background worker is idle. " +
                    "Supply MetaIntegration settings to enable it.");
                return;
            }

            var interval = TimeSpan.FromSeconds(_options.EventIntervalSeconds);

            bool schemaExists;
            try
            {
                schemaExists = await WorkerSchemaReadiness.WaitForTablesAsync(
                    _scopeFactory, _logger, "Meta integration processing",
                    ["ExternalIntegrationConnections", "ExternalIntegrationResources", "ExternalIntegrationEvents"],
                    _options.StartupDelaySeconds, _options.StartupRetryMaxDelaySeconds, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            if (!schemaExists)
            {
                _logger.LogWarning(
                    "Meta integration processing is paused because its database tables are missing. " +
                    "Apply the AddExternalIntegrations migration to enable it.");
                return;
            }

            using var timer = new PeriodicTimer(interval);
            var tick = 0L;

            do
            {
                tick++;

                await SafelyAsync("events", async scope =>
                {
                    var processor = scope.GetRequiredService<IMetaLeadEventProcessor>();
                    var processed = await processor.ProcessPendingEventsAsync(_options.EventBatchSize, stoppingToken);
                    if (processed > 0)
                        _logger.LogInformation("Turned {Count} Meta lead event(s) into leads.", processed);
                }, stoppingToken);

                // Every tick, not gated behind ResourceSyncIntervalSeconds: the six-hour cadence
                // is already enforced by SyncDueConnectionsAsync's own "is this connection due"
                // query, which is cheap to run and returns immediately when nothing needs it.
                // Gating the call itself at the outer scheduler previously meant a freshly
                // connected account — or a service that had just restarted — could wait up to
                // six hours for its very first sync regardless of how "never synced" was scored.
                await SafelyAsync("sync", async scope =>
                {
                    var sync = scope.GetRequiredService<IMetaResourceSyncService>();
                    var synced = await sync.SyncDueConnectionsAsync(stoppingToken);
                    if (synced > 0)
                        _logger.LogInformation("Synced resources for {Count} Meta connection(s).", synced);
                }, stoppingToken);

                // After sync, so a connection this tick's sync just flagged is reported in the same
                // pass. Once a minute is plenty for an outage that was otherwise silent for days.
                if (ShouldRun(tick, 60, interval))
                {
                    await SafelyAsync("alerts", async scope =>
                    {
                        var alerts = scope.GetRequiredService<IMetaIntegrationAlertService>();
                        var raised = await alerts.RaiseAlertsAsync(stoppingToken);
                        if (raised > 0)
                            _logger.LogWarning("Raised {Count} Meta integration alert(s) for Admins.", raised);
                    }, stoppingToken);
                }

                if (ShouldRun(tick, 3600, interval))
                {
                    await SafelyAsync("prune", async scope =>
                    {
                        var processor = scope.GetRequiredService<IMetaLeadEventProcessor>();
                        await processor.PruneOAuthStatesAsync(stoppingToken);
                        // A no-op until MetaIntegration:EventRetentionDays is set — events are
                        // kept forever by default.
                        await processor.PruneOldEventsAsync(stoppingToken);
                    }, stoppingToken);
                }
            }
            while (await WaitAsync(timer, stoppingToken));
        }

        /// <summary>
        /// True on the ticks lining up with a slower cadence. A freshly connected account is
        /// still discovered promptly, because "never synced" counts as due regardless of this.
        /// </summary>
        private static bool ShouldRun(long tick, int everySeconds, TimeSpan interval)
        {
            if (everySeconds <= 0)
                return false;

            var every = Math.Max(1, (int)Math.Round(everySeconds / interval.TotalSeconds));
            return tick % every == 0;
        }

        private async Task SafelyAsync(string name, Func<IServiceProvider, Task> action, CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await action(scope.ServiceProvider);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A failed sweep must never take the host down; the next tick retries, and
                // every row it was working on is protected by its lease.
                _logger.LogError(ex, "The Meta integration {Sweep} sweep failed. It will run again shortly.", name);
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
