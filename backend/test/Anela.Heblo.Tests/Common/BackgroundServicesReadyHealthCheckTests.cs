using Anela.Heblo.Application.Common;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using Xunit;

namespace Anela.Heblo.Tests.Common;

public class BackgroundServicesReadyHealthCheckTests
{
    private readonly IBackgroundServiceReadinessTracker _readinessTracker;
    private readonly BackgroundServicesReadyHealthCheck _healthCheck;

    public BackgroundServicesReadyHealthCheckTests()
    {
        _readinessTracker = Substitute.For<IBackgroundServiceReadinessTracker>();
        _healthCheck = new BackgroundServicesReadyHealthCheck(_readinessTracker);
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldReturnHealthy_WhenAllServicesReady()
    {
        // Arrange
        _readinessTracker.AreAllServicesReady().Returns(true);
        _readinessTracker.GetServiceStatuses().Returns(new Dictionary<string, bool>
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
        _readinessTracker.AreAllServicesReady().Returns(false);
        _readinessTracker.GetServiceStatuses().Returns(new Dictionary<string, bool>
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
        _readinessTracker.AreAllServicesReady().Returns(false);
        _readinessTracker.GetServiceStatuses().Returns(new Dictionary<string, bool>());

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
        _readinessTracker.AreAllServicesReady().Returns(false);
        _readinessTracker.GetServiceStatuses().Returns(new Dictionary<string, bool>
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