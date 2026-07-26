using DAMS.Application.Common;
using DAMS.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace DAMS.Api
{
    /// <summary>
    /// Drives the lead alert scan on a timer. The scan itself is idempotent, so a restart
    /// mid-cycle, an overlapping manual run, or a machine that was switched off all
    /// converge to the same result once it next runs.
    /// </summary>
    public sealed class LeadAlertBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly LeadAlertOptions _options;
        private readonly ILogger<LeadAlertBackgroundService> _logger;

        public LeadAlertBackgroundService(
            IServiceScopeFactory scopeFactory,
            IOptions<LeadAlertOptions> options,
            ILogger<LeadAlertBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_options.ScanIntervalMinutes <= 0)
            {
                _logger.LogInformation("Lead alert scanning is disabled (LeadAlerts:ScanIntervalMinutes <= 0).");
                return;
            }

            var interval = TimeSpan.FromMinutes(_options.ScanIntervalMinutes);
            using var timer = new PeriodicTimer(interval);

            // Give the application a moment to finish starting before the first scan.
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            do
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var alerts = scope.ServiceProvider.GetRequiredService<ILeadAlertService>();
                    var result = await alerts.RunScanAsync(stoppingToken);

                    if (result.NotificationsCreated > 0 || result.FollowUpsMarkedMissed > 0 || result.SiteVisitsMarkedMissed > 0)
                        _logger.LogInformation(
                            "Lead alert scan: {Notifications} notification(s), {MissedFollowUps} missed follow-up(s), {MissedVisits} missed visit(s), {Escalations} escalation(s).",
                            result.NotificationsCreated, result.FollowUpsMarkedMissed, result.SiteVisitsMarkedMissed, result.EscalationsRaised);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // A failed scan must never take the host down; the next tick retries.
                    _logger.LogError(ex, "The lead alert scan failed. It will run again in {Interval}.", interval);
                }
            }
            while (await SafeWaitAsync(timer, stoppingToken));
        }

        private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
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
