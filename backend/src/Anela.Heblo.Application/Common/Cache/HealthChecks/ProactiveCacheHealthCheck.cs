using Anela.Heblo.Application.Common.Cache.Abstractions;
using Anela.Heblo.Application.Common.Cache.Implementation;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Anela.Heblo.Application.Common.Cache.HealthChecks;

public class ProactiveCacheHealthCheck : IHealthCheck
{
    private readonly ProactiveCacheOrchestrator _orchestrator;

    public ProactiveCacheHealthCheck(ProactiveCacheOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var statuses = _orchestrator.GetCacheStatuses();
            var healthData = new Dictionary<string, object>();
            var unhealthyCaches = new List<string>();
            var staleCaches = new List<string>();

            foreach (var (cacheName, status) in statuses)
            {
                healthData[cacheName] = status;

                if (status is { } statusObj)
                {
                    var statusProperty = statusObj.GetType().GetProperty("Status");
                    var isReadyProperty = statusObj.GetType().GetProperty("IsReady");

                    if (statusProperty?.GetValue(statusObj) is CacheStatus cacheStatus)
                    {
                        switch (cacheStatus)
                        {
                            case CacheStatus.Failed:
                            case CacheStatus.NotLoaded:
                                unhealthyCaches.Add(cacheName);
                                break;
                            case CacheStatus.Stale:
                                staleCaches.Add(cacheName);
                                break;
                        }
                    }
                    else if (isReadyProperty?.GetValue(statusObj) is bool isReady && !isReady)
                    {
                        unhealthyCaches.Add(cacheName);
                    }
                }
            }

            if (unhealthyCaches.Any())
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    $"Cache services not ready: {string.Join(", ", unhealthyCaches)}",
                    data: healthData));
            }

            if (staleCaches.Any())
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"Cache services are stale: {string.Join(", ", staleCaches)}",
                    data: healthData));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                "All cache services are ready",
                data: healthData));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Failed to check cache health",
                ex));
        }
    }
}