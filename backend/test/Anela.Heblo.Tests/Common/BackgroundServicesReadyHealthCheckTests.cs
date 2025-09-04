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
    public async Task CheckHealthAsync_ShouldReturnHealthy_WhenAllServicesReady()
    {
        // Arrange
        _readinessTrackerMock.Setup(x => x.AreAllServicesReady()).Returns(true);
        _readinessTrackerMock.Setup(x => x.GetServiceStatuses()).Returns(new Dictionary<string, bool>
        {
            { "CatalogRefreshBackgroundService", true },
            { "FinancialAnalysisBackgroundService", true }
        });

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("All background services have completed initial load", result.Description);
        Assert.Contains("CatalogRefreshBackgroundService", result.Data.Keys);
        Assert.Contains("FinancialAnalysisBackgroundService", result.Data.Keys);
        Assert.Equal("Ready", result.Data["CatalogRefreshBackgroundService"]);
        Assert.Equal("Ready", result.Data["FinancialAnalysisBackgroundService"]);
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldReturnUnhealthy_WhenSomeServicesNotReady()
    {
        // Arrange
        _readinessTrackerMock.Setup(x => x.AreAllServicesReady()).Returns(false);
        _readinessTrackerMock.Setup(x => x.GetServiceStatuses()).Returns(new Dictionary<string, bool>
        {
            { "CatalogRefreshBackgroundService", true },
            { "FinancialAnalysisBackgroundService", false }
        });

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Waiting for services: FinancialAnalysisBackgroundService", result.Description);
        Assert.Contains("CatalogRefreshBackgroundService", result.Data.Keys);
        Assert.Contains("FinancialAnalysisBackgroundService", result.Data.Keys);
        Assert.Equal("Ready", result.Data["CatalogRefreshBackgroundService"]);
        Assert.Equal("NotReady", result.Data["FinancialAnalysisBackgroundService"]);
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
    public async Task CheckHealthAsync_ShouldReturnUnhealthy_WithMultipleNotReadyServices()
    {
        // Arrange
        _readinessTrackerMock.Setup(x => x.AreAllServicesReady()).Returns(false);
        _readinessTrackerMock.Setup(x => x.GetServiceStatuses()).Returns(new Dictionary<string, bool>
        {
            { "CatalogRefreshBackgroundService", false },
            { "FinancialAnalysisBackgroundService", false }
        });

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("Waiting for services:", result.Description);
        Assert.Contains("CatalogRefreshBackgroundService", result.Description);
        Assert.Contains("FinancialAnalysisBackgroundService", result.Description);
    }
}