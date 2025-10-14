using System.Collections.Concurrent;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.FinancialOverview.Services;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Common;

public class BackgroundServiceReadinessTracker : IBackgroundServiceReadinessTracker
{
    private readonly ConcurrentDictionary<string, bool> _serviceStatuses = new();
    private readonly ILogger<BackgroundServiceReadinessTracker> _logger;
    private volatile bool _hydrationCompleted = false;
    private DateTime? _hydrationStartedAt;
    private DateTime? _hydrationCompletedAt;
    private string? _hydrationFailureReason;

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

    public void ReportHydrationStarted()
    {
        _hydrationStartedAt = DateTime.UtcNow;
        _hydrationCompleted = false;
        _hydrationFailureReason = null;
        _logger.LogInformation("Tier-based hydration started");
    }

    public void ReportHydrationCompleted()
    {
        _hydrationCompleted = true;
        _hydrationCompletedAt = DateTime.UtcNow;
        _hydrationFailureReason = null;
        _logger.LogInformation("Tier-based hydration completed - all background refresh tasks ready");
    }

    public void ReportHydrationFailed(string? reason = null)
    {
        _hydrationCompleted = false;
        _hydrationFailureReason = reason;
        _logger.LogError("Tier-based hydration failed - background refresh tasks not ready. Reason: {Reason}", reason ?? "Unknown");
    }

    public bool AreAllServicesReady()
    {
        return _hydrationCompleted;
    }

    public IReadOnlyDictionary<string, bool> GetServiceStatuses()
    {
        var statuses = new Dictionary<string, bool>(_serviceStatuses);

        // Add tier-based hydration status
        statuses["TierBasedHydration"] = _hydrationCompleted;

        return statuses;
    }

    public IReadOnlyDictionary<string, object> GetHydrationDetails()
    {
        var details = new Dictionary<string, object>
        {
            ["IsCompleted"] = _hydrationCompleted,
            ["StartedAt"] = _hydrationStartedAt?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") ?? "Not started",
            ["CompletedAt"] = _hydrationCompletedAt?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") ?? "Not completed"
        };

        if (_hydrationStartedAt.HasValue && _hydrationCompletedAt.HasValue)
        {
            details["Duration"] = $"{(_hydrationCompletedAt.Value - _hydrationStartedAt.Value).TotalMilliseconds:F0}ms";
        }

        if (!string.IsNullOrEmpty(_hydrationFailureReason))
        {
            details["FailureReason"] = _hydrationFailureReason;
        }

        return details;
    }
}