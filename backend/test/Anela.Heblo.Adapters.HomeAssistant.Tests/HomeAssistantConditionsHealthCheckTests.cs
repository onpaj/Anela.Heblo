using Anela.Heblo.Adapters.HomeAssistant.Caching;
using Anela.Heblo.Adapters.HomeAssistant.HealthChecks;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.HomeAssistant.Tests;

public class HomeAssistantConditionsHealthCheckTests
{
    private readonly HomeAssistantSettings _settings = new()
    {
        BaseUrl = "http://ha.test",
        AccessToken = "tok",
        InnerTemperatureEntityId = "i_t",
        InnerHumidityEntityId = "i_h",
        OuterTemperatureEntityId = "o_t",
        OuterHumidityEntityId = "o_h",
        LiveSnapshotMaxAgeMinutes = 15,
    };

    private HomeAssistantConditionsHealthCheck CreateCheck(HomeAssistantSnapshotCoordinator coordinator) =>
        new(coordinator, Options.Create(_settings), TimeProvider.System);

    private static ConditionsSnapshot Snap(ConditionsReadingSource source, DateTime recordedAt) =>
        new(21m, 55m, 18m, 72m, recordedAt, source);

    [Fact]
    public async Task CheckHealthAsync_NoSnapshot_ReturnsUnhealthy()
    {
        var c = new HomeAssistantSnapshotCoordinator();
        var result = await CreateCheck(c).CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealthAsync_FreshLive_ReturnsHealthy()
    {
        var c = new HomeAssistantSnapshotCoordinator();
        c.RecordLive(Snap(ConditionsReadingSource.Live, DateTime.UtcNow));
        var result = await CreateCheck(c).CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_OldLive_ReturnsDegraded()
    {
        var c = new HomeAssistantSnapshotCoordinator();
        c.RecordLive(Snap(ConditionsReadingSource.Live, DateTime.UtcNow.AddMinutes(-30)));
        var result = await CreateCheck(c).CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_LastObservedPartial_ReturnsDegraded()
    {
        var c = new HomeAssistantSnapshotCoordinator();
        c.RecordObserved(Snap(ConditionsReadingSource.Partial, DateTime.UtcNow));
        var result = await CreateCheck(c).CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_LastObservedStale_ReturnsDegraded()
    {
        var c = new HomeAssistantSnapshotCoordinator();
        c.RecordObserved(Snap(ConditionsReadingSource.Stale, DateTime.UtcNow.AddMinutes(-3)));
        var result = await CreateCheck(c).CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_LastObservedUnavailable_ReturnsUnhealthy()
    {
        var c = new HomeAssistantSnapshotCoordinator();
        c.RecordObserved(Snap(ConditionsReadingSource.Unavailable, DateTime.UtcNow));
        var result = await CreateCheck(c).CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Unhealthy);
    }
}
