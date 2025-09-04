using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Anela.Heblo.Application.Common;

public class BackgroundServicesReadyHealthCheck : IHealthCheck
{
    private readonly IBackgroundServiceReadinessTracker _readinessTracker;

    public BackgroundServicesReadyHealthCheck(IBackgroundServiceReadinessTracker readinessTracker)
    {
        _readinessTracker = readinessTracker;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (_readinessTracker.AreAllServicesReady())
        {
            var statuses = _readinessTracker.GetServiceStatuses();
            var data = new Dictionary<string, object>();
            
            foreach (var status in statuses)
            {
                data[status.Key] = status.Value ? "Ready" : "NotReady";
            }

            return Task.FromResult(
                HealthCheckResult.Healthy("All background services have completed initial load", data));
        }

        var serviceStatuses = _readinessTracker.GetServiceStatuses();
        var notReadyServices = serviceStatuses.Where(s => !s.Value).Select(s => s.Key).ToList();
        
        var statusData = new Dictionary<string, object>();
        foreach (var status in serviceStatuses)
        {
            statusData[status.Key] = status.Value ? "Ready" : "NotReady";
        }

        var description = notReadyServices.Any() 
            ? $"Waiting for services: {string.Join(", ", notReadyServices)}"
            : "Background services initialization pending";

        return Task.FromResult(
            HealthCheckResult.Unhealthy(description, data: statusData));
    }
}