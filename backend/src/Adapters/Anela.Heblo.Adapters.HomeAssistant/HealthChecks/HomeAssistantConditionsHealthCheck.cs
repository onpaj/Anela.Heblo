using Anela.Heblo.Adapters.HomeAssistant.Caching;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.HomeAssistant.HealthChecks;

public sealed class HomeAssistantConditionsHealthCheck : IHealthCheck
{
    private readonly HomeAssistantSnapshotCoordinator _coordinator;
    private readonly HomeAssistantSettings _settings;
    private readonly TimeProvider _timeProvider;

    public HomeAssistantConditionsHealthCheck(
        HomeAssistantSnapshotCoordinator coordinator,
        IOptions<HomeAssistantSettings> options,
        TimeProvider timeProvider)
    {
        _coordinator = coordinator;
        _settings = options.Value;
        _timeProvider = timeProvider;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var observed = _coordinator.LastObservedSnapshot;

        if (observed is null)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "No HomeAssistant snapshot has been observed yet."));
        }

        var age = _timeProvider.GetUtcNow().UtcDateTime - observed.RecordedAt;
        var data = new Dictionary<string, object>
        {
            ["source"] = observed.Source.ToString(),
            ["recordedAt"] = observed.RecordedAt,
            ["ageSeconds"] = age.TotalSeconds,
        };

        return Task.FromResult(observed.Source switch
        {
            ConditionsReadingSource.Live when age <= TimeSpan.FromMinutes(_settings.LiveSnapshotMaxAgeMinutes)
                => HealthCheckResult.Healthy("HomeAssistant snapshot is Live and fresh.", data),
            ConditionsReadingSource.Live
                => HealthCheckResult.Degraded("Last Live snapshot is older than LiveSnapshotMaxAgeMinutes.", data: data),
            ConditionsReadingSource.Partial or ConditionsReadingSource.Stale
                => HealthCheckResult.Degraded($"HomeAssistant snapshot is {observed.Source}.", data: data),
            ConditionsReadingSource.Unavailable
                => HealthCheckResult.Unhealthy("HomeAssistant snapshot is Unavailable.", data: data),
            _ => HealthCheckResult.Unhealthy("Unknown snapshot source.", data: data),
        });
    }
}
