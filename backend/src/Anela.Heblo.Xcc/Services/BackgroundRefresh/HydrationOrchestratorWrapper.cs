using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Xcc.Services.BackgroundRefresh;

public class HydrationOrchestratorWrapper : BackgroundService
{
    private readonly TierBasedHydrationOrchestrator _orchestrator;
    private readonly IBackgroundServiceReadinessTracker _readinessTracker;
    private readonly ILogger<HydrationOrchestratorWrapper> _logger;

    public HydrationOrchestratorWrapper(
        TierBasedHydrationOrchestrator orchestrator,
        IBackgroundServiceReadinessTracker readinessTracker,
        ILogger<HydrationOrchestratorWrapper> logger)
    {
        _orchestrator = orchestrator;
        _readinessTracker = readinessTracker;
        _logger = logger;
    }

    public Task WaitForHydrationCompletionAsync() => _orchestrator.WaitForHydrationCompletionAsync();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _readinessTracker.ReportHydrationStarted();
            _logger.LogDebug("HydrationOrchestratorWrapper: Hydration started");

            // Report ready once the readiness tier(s) complete; higher tiers keep hydrating in the background.
            await _orchestrator.WaitForReadinessAsync();

            _readinessTracker.ReportHydrationCompleted();
            _logger.LogDebug("HydrationOrchestratorWrapper: Readiness reached");
        }
        catch (Exception ex)
        {
            _readinessTracker.ReportHydrationFailed(ex.Message);
            _logger.LogError(ex, "HydrationOrchestratorWrapper: Hydration failed");
            throw;
        }
    }
}