using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Common;

public class BackgroundServiceReadinessTracker : IBackgroundServiceReadinessTracker
{
    private readonly ConcurrentDictionary<string, bool> _serviceStatuses = new();
    private readonly ILogger<BackgroundServiceReadinessTracker> _logger;

    public BackgroundServiceReadinessTracker(ILogger<BackgroundServiceReadinessTracker> logger)
    {
        _logger = logger;
    }

    public void ReportInitialLoadCompleted(string serviceName)
    {
        _serviceStatuses.AddOrUpdate(serviceName, true, (key, oldValue) => true);
        _logger.LogInformation("Background service '{ServiceName}' reported initial load completion", serviceName);
    }

    public bool IsServiceReady(string serviceName)
    {
        return _serviceStatuses.TryGetValue(serviceName, out var isReady) && isReady;
    }

    public bool AreAllServicesReady()
    {
        // Services that must be ready for the application to be considered ready
        var requiredServices = new[]
        {
            "CatalogRefreshBackgroundService",
            "FinancialAnalysisBackgroundService"
        };

        foreach (var service in requiredServices)
        {
            if (!IsServiceReady(service))
            {
                _logger.LogDebug("Service '{ServiceName}' is not ready yet", service);
                return false;
            }
        }

        return true;
    }

    public IReadOnlyDictionary<string, bool> GetServiceStatuses()
    {
        return _serviceStatuses;
    }
}