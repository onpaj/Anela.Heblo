using Anela.Heblo.Application.Common;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Common;

public class BackgroundServicesReadyHealthCheckTests
{
    private readonly Mock<IBackgroundServiceReadinessTracker> _readinessTrackerMock;
    private readonly BackgroundServicesReadyHealthCheck _healthCheck;

    public BackgroundServicesReadyHealthCheckTests()
    {
        _readinessTrackerMock = new Mock<IBackgroundServiceReadinessTracker>();
        _healthCheck = new BackgroundServicesReadyHealthCheck(_readinessTrackerMock.Object);
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldReturnHealthy_WhenUsingRefreshTaskSystem()
    {
        // Arrange
        _readinessTrackerMock.Setup(x => x.AreAllServicesReady()).Returns(true);
        _readinessTrackerMock.Setup(x => x.GetServiceStatuses()).Returns(new Dictionary<string, bool>());

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("All background services have completed initial load", result.Description);
        Assert.Empty(result.Data); // No individual services tracked with refresh task system
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldReturnUnhealthy_WhenRefreshTaskSystemNotReady()
    {
        // Arrange
        _readinessTrackerMock.Setup(x => x.AreAllServicesReady()).Returns(false);
        _readinessTrackerMock.Setup(x => x.GetServiceStatuses()).Returns(new Dictionary<string, bool>());

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Background services initialization pending", result.Description);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldReturnUnhealthy_WhenNoServicesReported()
    {
        // Arrange
        _readinessTrackerMock.Setup(x => x.AreAllServicesReady()).Returns(false);
        _readinessTrackerMock.Setup(x => x.GetServiceStatuses()).Returns(new Dictionary<string, bool>());

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Background services initialization pending", result.Description);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldReturnHealthy_WithRefreshTaskSystemReplacingOldServices()
    {
        // Arrange - This test verifies the transition to refresh task system
        _readinessTrackerMock.Setup(x => x.AreAllServicesReady()).Returns(true);
        _readinessTrackerMock.Setup(x => x.GetServiceStatuses()).Returns(new Dictionary<string, bool>());

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("All background services have completed initial load", result.Description);
        Assert.Empty(result.Data); // Refresh task system doesn't track individual services
    }
}