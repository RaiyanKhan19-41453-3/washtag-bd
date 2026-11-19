using Washtag.Application;
using Washtag.Application.Interfaces;

namespace Washtag.Api.Workers;

/// <summary>Runs <see cref="IMaintenanceService"/> on a fixed interval inside the API process.</summary>
public sealed class MaintenanceWorker : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(20);

    private readonly IServiceScopeFactory _scopes;
    private readonly MaintenanceOptions _options;
    private readonly ILogger<MaintenanceWorker> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MaintenanceWorker(IServiceScopeFactory scopes, MaintenanceOptions options,
        ILogger<MaintenanceWorker> logger)
    {
        _scopes = scopes;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(InitialDelay, stoppingToken);
        if (_options.RunOnStartup)
            await RunOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunOnceAsync(stoppingToken);
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken)) return;
        try
        {
            using var scope = _scopes.CreateScope();
            var maintenance = scope.ServiceProvider.GetRequiredService<IMaintenanceService>();
            var result = await maintenance.RunAsync(cancellationToken);
            _logger.LogInformation(
                "Maintenance complete: {Invoices} invoices overdue, {Expired} subscriptions expired, " +
                "{PastDue} shops flagged PastDue, {Messages} messages pruned, {Shops} shops reindexed, {Errors} errors.",
                result.OverdueInvoices, result.ExpiredSubscriptions, result.ShopsMarkedPastDue,
                result.PrunedMessages, result.ReindexedShops, result.Errors.Count);
            foreach (var error in result.Errors) _logger.LogWarning("Maintenance step failed: {Error}", error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down — nothing to report.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled maintenance failed; the next run will retry.");
        }
        finally
        {
            _gate.Release();
        }
    }
}
