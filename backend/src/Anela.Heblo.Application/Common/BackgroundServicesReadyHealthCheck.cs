using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Common;

public class BackgroundServicesReadyHealthCheck : IHealthCheck
{
    private readonly IBackgroundServiceReadinessTracker _readinessTracker;
    private readonly BackgroundServicesOptions _options;

    public BackgroundServicesReadyHealthCheck(
        IBackgroundServiceReadinessTracker readinessTracker,
        IOptions<BackgroundServicesOptions> options)
    {
        _readinessTracker = readinessTracker;
        _options = options.Value;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Hydration disabled (e.g. Development/Test): nothing to wait for, so the app is ready.
        if (!_options.EnableHydration)
        {
            return Task.FromResult(
                HealthCheckResult.Healthy("Hydration disabled - no background services to wait for"));
        }

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