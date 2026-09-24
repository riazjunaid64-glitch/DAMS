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

            bool schemaExists;
            try
            {
                schemaExists = await WaitForIntegrationSchemaAsync(stoppingToken);
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

        /// <summary>
        /// Waits until the database answers, then reports whether the integration migration has
        /// been applied. Only a real answer of "missing" stops the worker: a check that could
        /// not reach the database is retried with a growing delay, so a database that comes up
        /// after the API does not leave this instance idle until someone restarts it.
        /// </summary>
        private async Task<bool> WaitForIntegrationSchemaAsync(CancellationToken stoppingToken)
        {
            var startupDelay = TimeSpan.FromSeconds(_options.StartupDelaySeconds);
            var maxRetryDelay = TimeSpan.FromSeconds(_options.StartupRetryMaxDelaySeconds);
            var retryDelay = TimeSpan.FromSeconds(Math.Clamp(
                _options.StartupDelaySeconds, 1, _options.StartupRetryMaxDelaySeconds));

            await Task.Delay(startupDelay, stoppingToken);

            for (var failures = 0; ; failures++)
            {
                try
                {
                    var exists = await IntegrationSchemaExistsAsync(stoppingToken);
                    if (exists && failures > 0)
                        _logger.LogInformation(
                            "Meta integration processing started after {Failures} failed database check(s).", failures);
                    else if (exists)
                        _logger.LogInformation("Meta integration processing started.");
                    return exists;
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex,
                        "Meta integration processing is waiting for the database (check {Attempt} failed). " +
                        "Retrying in {DelaySeconds} seconds.", failures + 1, retryDelay.TotalSeconds);
                    await Task.Delay(retryDelay, stoppingToken);
                    retryDelay = retryDelay * 2 < maxRetryDelay ? retryDelay * 2 : maxRetryDelay;
                }
            }
        }

        /// <summary>
        /// Refuses to run against a database that has not had the integration migration
        /// applied, rather than throwing on every tick until someone notices. Throws when the
        /// database cannot be asked at all.
        /// </summary>
        private async Task<bool> IntegrationSchemaExistsAsync(CancellationToken stoppingToken)
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
