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
        var hydrationDetails = _readinessTracker.GetHydrationDetails() ?? new Dictionary<string, object>();
        var data = new Dictionary<string, object>(hydrationDetails);

        if (_readinessTracker.AreAllServicesReady())
        {
            var statuses = _readinessTracker.GetServiceStatuses();
            foreach (var status in statuses)
            {
                data[$"Service_{status.Key}"] = status.Value ? "Ready" : "NotReady";
            }

            return Task.FromResult(
                HealthCheckResult.Healthy("Tier-based hydration completed - all background refresh tasks ready", data));
        }

        var serviceStatuses = _readinessTracker.GetServiceStatuses();
        var notReadyServices = serviceStatuses.Where(s => !s.Value).Select(s => s.Key).ToList();

        foreach (var status in serviceStatuses)
        {
            data[$"Service_{status.Key}"] = status.Value ? "Ready" : "NotReady";
        }

        var description = notReadyServices.Any()
            ? $"Waiting for background services: {string.Join(", ", notReadyServices)}"
            : "Tier-based hydration in progress";

        // If hydration failed, return degraded instead of unhealthy
        if (hydrationDetails.ContainsKey("FailureReason"))
        {
            return Task.FromResult(
                HealthCheckResult.Degraded($"Tier-based hydration failed: {hydrationDetails["FailureReason"]}", data: data));
        }

        return Task.FromResult(
            HealthCheckResult.Unhealthy(description, data: data));
    }
}