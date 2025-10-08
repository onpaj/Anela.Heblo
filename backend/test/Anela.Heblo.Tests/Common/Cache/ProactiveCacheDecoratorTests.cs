using System;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Common.Cache.Abstractions;
using Anela.Heblo.Application.Common.Cache.Implementation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Common.Cache;

public class ProactiveCacheDecoratorTests
{
    private readonly Mock<ILogger<ProactiveCacheDecorator<ITestDataSource, TestData>>> _mockLogger;
    private readonly FakeTimeProvider _timeProvider;
    private readonly Mock<ITestDataSource> _mockDataSource;
    private readonly TestData _testData;

    public ProactiveCacheDecoratorTests()
    {
        _mockLogger = new Mock<ILogger<ProactiveCacheDecorator<ITestDataSource, TestData>>>();
        _timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        _mockDataSource = new Mock<ITestDataSource>();
        _testData = new TestData { Value = "test-value" };
    }

    [Fact]
    public async Task GetCurrent_WhenNotLoaded_ReturnsNull()
    {
        // Arrange
        var cache = CreateCache();

        // Act
        var result = cache.GetCurrent();

        // Assert
        Assert.Null(result);
        Assert.Equal(CacheStatus.NotLoaded, cache.Status);
        Assert.False(cache.IsReady);
    }

    [Fact]
    public async Task ForceRefreshAsync_WhenEnabled_LoadsDataSuccessfully()
    {
        // Arrange
        _mockDataSource.Setup(x => x.GetDataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testData);

        var cache = CreateCache();

        // Act
        var result = await cache.ForceRefreshAsync();

        // Assert
        Assert.True(result);
        Assert.Equal(_testData, cache.GetCurrent());
        Assert.Equal(CacheStatus.Ready, cache.Status);
        Assert.True(cache.IsReady);
        Assert.NotNull(cache.LastRefreshTime);
    }

    [Fact]
    public async Task ForceRefreshAsync_WhenDisabled_ReturnsFalse()
    {
        // Arrange
        var config = CreateConfiguration();
        config.Enabled = false;
        var cache = CreateCache(config);

        // Act
        var result = await cache.ForceRefreshAsync();

        // Assert
        Assert.False(result);
        Assert.Null(cache.GetCurrent());
        Assert.Equal(CacheStatus.NotLoaded, cache.Status);
    }

    [Fact]
    public async Task RefreshAsync_WhenIntervalNotMet_ReturnsFalse()
    {
        // Arrange
        _mockDataSource.Setup(x => x.GetDataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testData);

        var config = CreateConfiguration();
        config.RefreshInterval = TimeSpan.FromMinutes(15);
        var cache = CreateCache(config);

        // First refresh
        await cache.ForceRefreshAsync();
        var firstRefreshTime = cache.LastRefreshTime;

        // Advance time by 5 minutes (less than interval)
        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        // Act - try to refresh again
        var result = await CallRefreshAsync(cache);

        // Assert
        Assert.False(result);
        Assert.Equal(firstRefreshTime, cache.LastRefreshTime);
    }

    [Fact]
    public async Task RefreshAsync_WhenIntervalMet_ReturnsTrue()
    {
        // Arrange
        _mockDataSource.Setup(x => x.GetDataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testData);

        var config = CreateConfiguration();
        config.RefreshInterval = TimeSpan.FromMinutes(15);
        var cache = CreateCache(config);

        // First refresh
        await cache.ForceRefreshAsync();
        var firstRefreshTime = cache.LastRefreshTime;

        // Advance time by 16 minutes (more than interval)
        _timeProvider.Advance(TimeSpan.FromMinutes(16));

        // Act - try to refresh again
        var result = await CallRefreshAsync(cache);

        // Assert
        Assert.True(result);
        Assert.True(cache.LastRefreshTime > firstRefreshTime);
    }

    [Fact]
    public async Task ForceRefreshAsync_WhenRefreshFails_HandlesByFailureMode()
    {
        // Arrange
        _mockDataSource.Setup(x => x.GetDataAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test error"));

        var config = CreateConfiguration();
        config.FailureMode = CacheFailureMode.KeepStale;
        config.RetryPolicy.MaxRetries = 0; // No retries for faster test
        var cache = CreateCache(config);

        // Act
        var result = await cache.ForceRefreshAsync();

        // Assert
        Assert.False(result);
        Assert.Equal(CacheStatus.Failed, cache.Status);
        Assert.False(cache.IsReady);
    }

    [Fact]
    public async Task ForceRefreshAsync_WhenRefreshFailsWithStaleData_KeepsStaleData()
    {
        // Arrange
        _mockDataSource.SetupSequence(x => x.GetDataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testData) // First call succeeds
            .ThrowsAsync(new InvalidOperationException("Test error")); // Second call fails

        var config = CreateConfiguration();
        config.FailureMode = CacheFailureMode.KeepStale;
        config.RetryPolicy.MaxRetries = 0; // No retries for faster test
        var cache = CreateCache(config);

        // First successful refresh
        await cache.ForceRefreshAsync();
        Assert.Equal(_testData, cache.GetCurrent());

        // Act - second refresh that fails
        var result = await cache.ForceRefreshAsync();

        // Assert
        Assert.False(result);
        Assert.Equal(_testData, cache.GetCurrent()); // Stale data is kept
        Assert.Equal(CacheStatus.Stale, cache.Status);
        Assert.True(cache.IsReady); // Still ready despite being stale
    }

    [Fact]
    public async Task ForceRefreshAsync_WithRetryPolicy_RetriesOnFailure()
    {
        // Arrange
        var callCount = 0;
        _mockDataSource.Setup(x => x.GetDataAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount <= 2)
                    throw new InvalidOperationException("Test error");
                return Task.FromResult(_testData);
            });

        var config = CreateConfiguration();
        config.RetryPolicy.MaxRetries = 3;
        config.RetryPolicy.BaseDelay = TimeSpan.FromMilliseconds(10);
        var cache = CreateCache(config);

        // Act
        var result = await cache.ForceRefreshAsync();

        // Assert
        Assert.True(result);
        Assert.Equal(_testData, cache.GetCurrent());
        Assert.Equal(3, callCount); // Should have retried twice and succeeded on third attempt
    }

    [Fact]
    public async Task ForceRefreshAsync_ConcurrentCalls_OnlyOneExecutes()
    {
        // Arrange
        var callCount = 0;
        var tcs = new TaskCompletionSource<TestData>();

        _mockDataSource.Setup(x => x.GetDataAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                return tcs.Task;
            });

        var cache = CreateCache();

        // Act - start two concurrent refresh operations
        var task1 = cache.ForceRefreshAsync();
        var task2 = cache.ForceRefreshAsync();

        // Complete the data source call
        tcs.SetResult(_testData);

        var results = await Task.WhenAll(task1, task2);

        // Assert
        Assert.Equal(1, callCount); // Should only call data source once
        Assert.True(results[0]); // First call should succeed
        Assert.False(results[1]); // Second call should return false (couldn't acquire semaphore)
    }

    private ProactiveCacheDecorator<ITestDataSource, TestData> CreateCache(
        CacheRefreshConfiguration? config = null)
    {
        config ??= CreateConfiguration();
        
        return new ProactiveCacheDecorator<ITestDataSource, TestData>(
            _mockDataSource.Object,
            (source, ct) => source.GetDataAsync(ct),
            config,
            _mockLogger.Object,
            _timeProvider);
    }

    private static CacheRefreshConfiguration CreateConfiguration()
    {
        return new CacheRefreshConfiguration
        {
            Name = "test-cache",
            RefreshInterval = TimeSpan.FromMinutes(15),
            InitialDelay = TimeSpan.Zero,
            Enabled = true,
            Priority = 100,
            Dependencies = Array.Empty<string>(),
            RetryPolicy = new RetryPolicy
            {
                MaxRetries = 3,
                BaseDelay = TimeSpan.FromMilliseconds(100),
                BackoffMultiplier = 2.0,
                MaxDelay = TimeSpan.FromSeconds(10)
            },
            FailureMode = CacheFailureMode.KeepStale
        };
    }

    private async Task<bool> CallRefreshAsync(ProactiveCacheDecorator<ITestDataSource, TestData> cache)
    {
        // Use reflection to call the internal RefreshAsync method
        var method = cache.GetType().GetMethod("RefreshAsync", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var task = (Task<bool>?)method?.Invoke(cache, new object[] { CancellationToken.None });
        return task != null ? await task : false;
    }
}

public interface ITestDataSource
{
    Task<TestData> GetDataAsync(CancellationToken cancellationToken);
}

public class TestData
{
    public string Value { get; set; } = string.Empty;
}