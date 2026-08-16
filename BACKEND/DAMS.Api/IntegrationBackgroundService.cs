using DAMS.Application.Common;
using DAMS.Application.Interfaces;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DAMS.Api
{
    /// <summary>
    /// Drives everything the Meta integration does outside a request: draining the webhook
    /// inbox, refreshing discovered resources, and clearing expired OAuth states.
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

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!await IntegrationSchemaExistsAsync(stoppingToken))
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

                // Resource discovery is what makes a newly created ad or form show up without
                // anyone reconnecting. It is slow and rarely urgent, so it runs far less often.
                if (ShouldRun(tick, _options.ResourceSyncIntervalSeconds, interval))
                {
                    await SafelyAsync("sync", async scope =>
                    {
                        var sync = scope.GetRequiredService<IMetaResourceSyncService>();
                        var synced = await sync.SyncDueConnectionsAsync(stoppingToken);
                        if (synced > 0)
                            _logger.LogInformation("Synced resources for {Count} Meta connection(s).", synced);
                    }, stoppingToken);
                }

                if (ShouldRun(tick, 3600, interval))
                {
                    await SafelyAsync("prune", async scope =>
                    {
                        var processor = scope.GetRequiredService<IMetaLeadEventProcessor>();
                        await processor.PruneOAuthStatesAsync(stoppingToken);
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

        /// <summary>
        /// Refuses to run against a database that has not had the integration migration
        /// applied, rather than throwing on every tick until someone notices.
        /// </summary>
        private async Task<bool> IntegrationSchemaExistsAsync(CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var connection = db.Database.GetDbConnection();

                await db.Database.OpenConnectionAsync(stoppingToken);
                try
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = """
                        SELECT CASE WHEN
                            OBJECT_ID(N'[dbo].[ExternalIntegrationConnections]', N'U') IS NOT NULL AND
                            OBJECT_ID(N'[dbo].[ExternalIntegrationResources]', N'U') IS NOT NULL AND
                            OBJECT_ID(N'[dbo].[ExternalIntegrationEvents]', N'U') IS NOT NULL
                        THEN 1 ELSE 0 END
                        """;

                    var result = await command.ExecuteScalarAsync(stoppingToken);
                    return Convert.ToInt32(result) == 1;
                }
                finally
                {
                    await db.Database.CloseConnectionAsync();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Meta integration processing is paused because its schema check failed.");
                return false;
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
