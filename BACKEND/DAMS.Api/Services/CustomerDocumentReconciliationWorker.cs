using DAMS.Application.Services;

namespace DAMS.Api.Services;

/// <summary>Runs advisory customer-document repair independently of customer and booking requests.</summary>
public sealed class CustomerDocumentReconciliationWorker : BackgroundService
{
    private static readonly TimeSpan SuccessInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FailureInterval = TimeSpan.FromSeconds(30);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CustomerDocumentReconciliationWorker> _logger;

    public CustomerDocumentReconciliationWorker(IServiceScopeFactory scopeFactory,
        ILogger<CustomerDocumentReconciliationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = SuccessInterval;
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<CustomerDocumentReconciliationService>()
                    .ReconcileBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                delay = FailureInterval;
                _logger.LogWarning(ex,
                    "Customer document reconciliation could not run; core customer and booking workflows remain available.");
            }

            await Task.Delay(delay, stoppingToken);
        }
    }
}
