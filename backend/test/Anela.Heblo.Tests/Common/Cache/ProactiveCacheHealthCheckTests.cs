using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Common.Cache.Abstractions;
using Anela.Heblo.Application.Common.Cache.HealthChecks;
using Anela.Heblo.Application.Common.Cache.Implementation;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Common.Cache;

public class ProactiveCacheHealthCheckTests
{
    private readonly Mock<ProactiveCacheOrchestrator> _mockOrchestrator;
    private readonly ProactiveCacheHealthCheck _healthCheck;

    public ProactiveCacheHealthCheckTests()
    {
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockLogger = new Mock<ILogger<ProactiveCacheOrchestrator>>();
        
        _mockOrchestrator = new Mock<ProactiveCacheOrchestrator>(
            mockServiceProvider.Object,
            mockLogger.Object,
            TimeProvider.System);
        
        _healthCheck = new ProactiveCacheHealthCheck(_mockOrchestrator.Object);
    }

    [Fact]
    public async Task CheckHealthAsync_AllCachesReady_ReturnsHealthy()
    {
        // Arrange
        var statuses = new Dictionary<string, object>
        {
            ["cache-1"] = new { Status = CacheStatus.Ready, IsReady = true },
            ["cache-2"] = new { Status = CacheStatus.Ready, IsReady = true }
        };

        _mockOrchestrator.Setup(x => x.GetCacheStatuses())
            .Returns(statuses);

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("All cache services are ready", result.Description);
        Assert.Equal(statuses, result.Data);
    }

    [Fact]
    public async Task CheckHealthAsync_SomeCachesStale_ReturnsDegraded()
    {
        // Arrange
        var statuses = new Dictionary<string, object>
        {
            ["cache-1"] = new { Status = CacheStatus.Ready, IsReady = true },
            ["cache-2"] = new { Status = CacheStatus.Stale, IsReady = true }
        };

        _mockOrchestrator.Setup(x => x.GetCacheStatuses())
            .Returns(statuses);

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("Cache services are stale: cache-2", result.Description);
        Assert.Equal(statuses, result.Data);
    }

    [Fact]
    public async Task CheckHealthAsync_SomeCachesFailed_ReturnsUnhealthy()
    {
        // Arrange
        var statuses = new Dictionary<string, object>
        {
            ["cache-1"] = new { Status = CacheStatus.Ready, IsReady = true },
            ["cache-2"] = new { Status = CacheStatus.Failed, IsReady = false }
        };

        _mockOrchestrator.Setup(x => x.GetCacheStatuses())
            .Returns(statuses);

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("Cache services not ready: cache-2", result.Description);
        Assert.Equal(statuses, result.Data);
    }

    [Fact]
    public async Task CheckHealthAsync_SomeCachesNotLoaded_ReturnsUnhealthy()
    {
        // Arrange
        var statuses = new Dictionary<string, object>
        {
            ["cache-1"] = new { Status = CacheStatus.Ready, IsReady = true },
            ["cache-2"] = new { Status = CacheStatus.NotLoaded, IsReady = false }
        };

        _mockOrchestrator.Setup(x => x.GetCacheStatuses())
            .Returns(statuses);

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("Cache services not ready: cache-2", result.Description);
    }

    [Fact]
    public async Task CheckHealthAsync_CachesLoading_ReturnsHealthy()
    {
        // Arrange
        var statuses = new Dictionary<string, object>
        {
            ["cache-1"] = new { Status = CacheStatus.Loading, IsReady = false }
        };

        _mockOrchestrator.Setup(x => x.GetCacheStatuses())
            .Returns(statuses);

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("Cache services not ready: cache-1", result.Description);
    }

    [Fact]
    public async Task CheckHealthAsync_MixedFailuresAndStale_ReturnsUnhealthy()
    {
        // Arrange
        var statuses = new Dictionary<string, object>
        {
            ["cache-1"] = new { Status = CacheStatus.Failed, IsReady = false },
            ["cache-2"] = new { Status = CacheStatus.Stale, IsReady = true },
            ["cache-3"] = new { Status = CacheStatus.Ready, IsReady = true }
        };

        _mockOrchestrator.Setup(x => x.GetCacheStatuses())
            .Returns(statuses);

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("Cache services not ready: cache-1", result.Description);
    }

    [Fact]
    public async Task CheckHealthAsync_ExceptionThrown_ReturnsUnhealthy()
    {
        // Arrange
        var exception = new InvalidOperationException("Test exception");
        _mockOrchestrator.Setup(x => x.GetCacheStatuses())
            .Throws(exception);

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Failed to check cache health", result.Description);
        Assert.Same(exception, result.Exception);
    }

    [Fact]
    public async Task CheckHealthAsync_EmptyStatuses_ReturnsHealthy()
    {
        // Arrange
        var statuses = new Dictionary<string, object>();

        _mockOrchestrator.Setup(x => x.GetCacheStatuses())
            .Returns(statuses);

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("All cache services are ready", result.Description);
    }

    [Fact]
    public async Task CheckHealthAsync_StatusObjectWithoutExpectedProperties_HandlesGracefully()
    {
        // Arrange
        var statuses = new Dictionary<string, object>
        {
            ["cache-1"] = new { SomeOtherProperty = "value" }
        };

        _mockOrchestrator.Setup(x => x.GetCacheStatuses())
            .Returns(statuses);

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("All cache services are ready", result.Description);
    }
}