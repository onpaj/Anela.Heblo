using System.Collections.Concurrent;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.FinancialOverview.Services;
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

    public void ReportInitialLoadCompleted<TService>() where TService : class
    {
        var serviceName = typeof(TService).Name;
        _serviceStatuses.AddOrUpdate(serviceName, true, (key, oldValue) => true);
        _logger.LogInformation("Background service '{ServiceType}' reported initial load completion", typeof(TService).Name);
    }

    public bool IsServiceReady<TService>() where TService : class
    {
        var serviceName = typeof(TService).Name;
        return _serviceStatuses.TryGetValue(serviceName, out var isReady) && isReady;
    }

    public bool AreAllServicesReady()
    {
        // Check required services by their types
        return IsServiceReady<CatalogRefreshBackgroundService>() &&
               IsServiceReady<FinancialAnalysisBackgroundService>();
    }

    public IReadOnlyDictionary<string, bool> GetServiceStatuses()
    {
        return _serviceStatuses;
    }
}