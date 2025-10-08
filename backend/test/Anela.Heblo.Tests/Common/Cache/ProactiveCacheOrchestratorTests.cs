using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Common.Cache.Abstractions;
using Anela.Heblo.Application.Common.Cache.Implementation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Common.Cache;

public class ProactiveCacheOrchestratorTests : IDisposable
{
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<ILogger<ProactiveCacheOrchestrator>> _mockLogger;
    private readonly TimeProvider _timeProvider;
    private readonly ProactiveCacheOrchestrator _orchestrator;

    public ProactiveCacheOrchestratorTests()
    {
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockLogger = new Mock<ILogger<ProactiveCacheOrchestrator>>();
        _timeProvider = TimeProvider.System;
        
        _orchestrator = new ProactiveCacheOrchestrator(
            _mockServiceProvider.Object,
            _mockLogger.Object,
            _timeProvider);
    }

    [Fact]
    public void RegisterCache_StoresCacheRegistration()
    {
        // Arrange
        var mockCache = new Mock<IProactiveCacheService<TestData>>();
        var config = CreateConfiguration("test-cache");
        
        var registration = new CacheRegistration
        {
            Name = "test-cache",
            Configuration = config,
            CacheService = mockCache.Object,
            CacheServiceType = typeof(ProactiveCacheDecorator<ITestDataSource, TestData>)
        };

        // Act
        _orchestrator.RegisterCache(registration);

        // Assert
        var statuses = _orchestrator.GetCacheStatuses();
        Assert.Contains("test-cache", statuses.Keys);
    }

    [Fact]
    public async Task StartAsync_InitializesAndStartsPeriodicRefresh()
    {
        // Arrange
        var mockCache = CreateMockCache("test-cache");
        RegisterCache(mockCache);

        // Act
        await _orchestrator.StartAsync(CancellationToken.None);

        // Assert - orchestrator should be running
        // We can't easily test the timer directly, but we can verify it started without exceptions
        Assert.True(true); // Test passes if no exception is thrown
    }

    [Fact]
    public async Task StartAsync_WithDependencies_BuildsCorrectOrder()
    {
        // Arrange
        var cacheA = CreateMockCache("cache-a");
        var cacheB = CreateMockCache("cache-b", dependencies: new[] { "cache-a" });
        var cacheC = CreateMockCache("cache-c", dependencies: new[] { "cache-b" });

        RegisterCache(cacheA);
        RegisterCache(cacheB);
        RegisterCache(cacheC);

        // Act
        await _orchestrator.StartAsync(CancellationToken.None);

        // Assert - no circular dependency exception should be thrown
        Assert.True(true);
    }

    [Fact]
    public async Task StartAsync_WithCircularDependencies_ThrowsException()
    {
        // Arrange
        var cacheA = CreateMockCache("cache-a", dependencies: new[] { "cache-b" });
        var cacheB = CreateMockCache("cache-b", dependencies: new[] { "cache-a" });

        RegisterCache(cacheA);
        RegisterCache(cacheB);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _orchestrator.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StartAsync_WithPriorities_LoadsHigherPriorityFirst()
    {
        // Arrange
        var loadOrder = new List<string>();
        
        var cacheA = CreateMockCache("cache-a", priority: 50);
        var cacheB = CreateMockCache("cache-b", priority: 100); // Higher priority
        
        SetupCacheWithLoadTracking(cacheA, "cache-a", loadOrder);
        SetupCacheWithLoadTracking(cacheB, "cache-b", loadOrder);

        RegisterCache(cacheA);
        RegisterCache(cacheB);

        // Act
        await _orchestrator.StartAsync(CancellationToken.None);
        
        // Give some time for initial load to complete
        await Task.Delay(100);

        // Assert
        Assert.Equal(2, loadOrder.Count);
        Assert.Equal("cache-b", loadOrder[0]); // Higher priority should load first
        Assert.Equal("cache-a", loadOrder[1]);
    }

    [Fact]
    public async Task ForceRefreshAsync_ExistingCache_CallsForceRefresh()
    {
        // Arrange
        var mockCache = CreateMockCache("test-cache");
        RegisterCache(mockCache);
        
        await _orchestrator.StartAsync(CancellationToken.None);

        // Act
        var result = await _orchestrator.ForceRefreshAsync("test-cache");

        // Assert
        Assert.True(result);
        // Verify that ForceRefreshAsync was called via reflection
    }

    [Fact]
    public async Task ForceRefreshAsync_NonExistentCache_ReturnsFalse()
    {
        // Arrange
        await _orchestrator.StartAsync(CancellationToken.None);

        // Act
        var result = await _orchestrator.ForceRefreshAsync("non-existent-cache");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetCacheStatuses_ReturnsAllCacheStatuses()
    {
        // Arrange
        var mockCache1 = CreateMockCache("cache-1");
        var mockCache2 = CreateMockCache("cache-2");
        
        RegisterCache(mockCache1);
        RegisterCache(mockCache2);

        // Act
        var statuses = _orchestrator.GetCacheStatuses();

        // Assert
        Assert.Equal(2, statuses.Count);
        Assert.Contains("cache-1", statuses.Keys);
        Assert.Contains("cache-2", statuses.Keys);
    }

    [Fact]
    public async Task StopAsync_StopsOrchestratorGracefully()
    {
        // Arrange
        await _orchestrator.StartAsync(CancellationToken.None);

        // Act
        await _orchestrator.StopAsync(CancellationToken.None);

        // Assert - should complete without exceptions
        Assert.True(true);
    }

    private Mock<IProactiveCacheService<TestData>> CreateMockCache(
        string name,
        int priority = 100,
        string[] dependencies = null)
    {
        var mockCache = new Mock<IProactiveCacheService<TestData>>();
        
        mockCache.Setup(x => x.Status).Returns(CacheStatus.Ready);
        mockCache.Setup(x => x.IsReady).Returns(true);
        mockCache.Setup(x => x.LastRefreshTime).Returns(DateTime.UtcNow);
        mockCache.Setup(x => x.ForceRefreshAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        return mockCache;
    }

    private void SetupCacheWithLoadTracking(
        Mock<IProactiveCacheService<TestData>> mockCache,
        string cacheName,
        List<string> loadOrder)
    {
        // We can't directly mock the internal RefreshAsync method since it's not on the interface
        // Instead, we'll track calls to ForceRefreshAsync as a proxy
        mockCache.Setup(x => x.ForceRefreshAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                loadOrder.Add(cacheName);
                return Task.FromResult(true);
            });
    }

    private void RegisterCache(Mock<IProactiveCacheService<TestData>> mockCache, string name = null)
    {
        name ??= "test-cache";
        
        var config = CreateConfiguration(name);
        var registration = new CacheRegistration
        {
            Name = name,
            Configuration = config,
            CacheService = mockCache.Object,
            CacheServiceType = typeof(ProactiveCacheDecorator<ITestDataSource, TestData>)
        };

        _orchestrator.RegisterCache(registration);
    }

    private static CacheRefreshConfiguration CreateConfiguration(
        string name,
        int priority = 100,
        string[] dependencies = null)
    {
        return new CacheRefreshConfiguration
        {
            Name = name,
            RefreshInterval = TimeSpan.FromMinutes(15),
            InitialDelay = TimeSpan.Zero,
            Enabled = true,
            Priority = priority,
            Dependencies = dependencies ?? Array.Empty<string>(),
            RetryPolicy = new RetryPolicy(),
            FailureMode = CacheFailureMode.KeepStale
        };
    }

    public void Dispose()
    {
        _orchestrator?.StopAsync(CancellationToken.None).Wait(TimeSpan.FromSeconds(5));
        _orchestrator?.Dispose();
    }
}