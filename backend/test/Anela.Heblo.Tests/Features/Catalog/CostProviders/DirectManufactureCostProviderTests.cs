using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.CostProviders;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Cache;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.CostProviders;

/// <summary>
/// Tests for DirectManufactureCostProvider.
/// Uses Collection attribute to ensure sequential execution due to static RefreshLock in the provider.
/// </summary>
[Collection("DirectManufactureCostProviderTests")]
public class DirectManufactureCostProviderTests
{
    [Fact]
    internal async Task RefreshAsync_WhenRefreshAlreadyInProgress_SkipsSecondCallAndLogsInformation()
    {
        // Arrange
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var catalogRepositoryMock = new Mock<ICatalogRepository>();
        catalogRepositoryMock
            .Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>()))
            .Returns(gate.Task);
        catalogRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate>());

        var cacheMock = new Mock<IDirectManufactureCostCache>();
        cacheMock
            .Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var loggerMock = new Mock<ILogger<DirectManufactureCostProvider>>();

        var provider = CreateProvider(
            cache: cacheMock.Object,
            catalogRepository: catalogRepositoryMock.Object,
            logger: loggerMock.Object);

        // Act
        var firstRefresh = provider.RefreshAsync();
        try
        {
            // Second call is issued while the first call still holds the lock (blocked on the gate),
            // so it must hit the WaitAsync(0) skip path and return promptly without blocking.
            await provider.RefreshAsync();
        }
        finally
        {
            // Always release the gate and await the first call, even if an assertion above throws,
            // so the static RefreshLock is never left acquired for subsequent tests.
            gate.TrySetResult();
            await firstRefresh;
        }

        // Assert
        catalogRepositoryMock.Verify(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>()), Times.Once);
        catalogRepositoryMock.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once);
        cacheMock.Verify(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()), Times.Once);
        VerifyLogged(loggerMock, LogLevel.Information, "DirectManufactureCostCache refresh already in progress, skipping");
    }

    [Fact]
    internal async Task GetCostsAsync_WhenCacheNotHydrated_ReturnsEmptyDictionaryAndLogsWarning()
    {
        // Arrange
        var cacheMock = new Mock<IDirectManufactureCostCache>();
        cacheMock
            .Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CostCacheData { IsHydrated = false });

        var catalogRepositoryMock = new Mock<ICatalogRepository>();
        var loggerMock = new Mock<ILogger<DirectManufactureCostProvider>>();

        var provider = CreateProvider(
            cache: cacheMock.Object,
            catalogRepository: catalogRepositoryMock.Object,
            logger: loggerMock.Object);

        // Act
        var result = await provider.GetCostsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
        VerifyLogged(loggerMock, LogLevel.Warning, "DirectManufactureCostCache not hydrated yet");
        catalogRepositoryMock.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
        catalogRepositoryMock.Verify(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    internal async Task GetCostsAsync_WhenCacheNotHydrated_WithFilterArgs_StillReturnsEmptyDictionary()
    {
        // Arrange
        var cacheMock = new Mock<IDirectManufactureCostCache>();
        cacheMock
            .Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CostCacheData { IsHydrated = false });

        var provider = CreateProvider(cache: cacheMock.Object);

        // Act
        var result = await provider.GetCostsAsync(
            new List<string> { "PROD001" },
            DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    internal async Task GetCostsAsync_WithNullProductCodes_ReturnsAllCachedEntries()
    {
        // Arrange
        var cacheMock = new Mock<IDirectManufactureCostCache>();
        cacheMock
            .Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildHydratedCacheData());

        var provider = CreateProvider(cache: cacheMock.Object);

        // Act
        var result = await provider.GetCostsAsync(productCodes: null);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.True(result.ContainsKey("PROD001"));
        Assert.True(result.ContainsKey("PROD002"));
    }

    [Fact]
    internal async Task GetCostsAsync_WithEmptyProductCodes_ReturnsAllCachedEntries()
    {
        // Arrange
        var cacheMock = new Mock<IDirectManufactureCostCache>();
        cacheMock
            .Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildHydratedCacheData());

        var provider = CreateProvider(cache: cacheMock.Object);

        // Act
        var result = await provider.GetCostsAsync(productCodes: new List<string>());

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    internal async Task GetCostsAsync_WithSubsetProductCodes_ReturnsOnlyMatchingEntries()
    {
        // Arrange
        var cacheMock = new Mock<IDirectManufactureCostCache>();
        cacheMock
            .Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildHydratedCacheData());

        var provider = CreateProvider(cache: cacheMock.Object);

        // Act
        var result = await provider.GetCostsAsync(productCodes: new List<string> { "PROD001", "NONEXISTENT" });

        // Assert
        Assert.Single(result);
        Assert.True(result.ContainsKey("PROD001"));
    }

    [Fact]
    internal async Task GetCostsAsync_WithNoMatchingProductCodes_ReturnsEmptyDictionary()
    {
        // Arrange
        var cacheMock = new Mock<IDirectManufactureCostCache>();
        cacheMock
            .Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildHydratedCacheData());

        var provider = CreateProvider(cache: cacheMock.Object);

        // Act
        var result = await provider.GetCostsAsync(productCodes: new List<string> { "NONEXISTENT" });

        // Assert
        Assert.Empty(result);
    }

    private static CostCacheData BuildHydratedCacheData()
    {
        return new CostCacheData
        {
            IsHydrated = true,
            ProductCosts = new Dictionary<string, List<MonthlyCost>>
            {
                ["PROD001"] = new List<MonthlyCost> { new(new DateTime(2026, 1, 1), 15m) },
                ["PROD002"] = new List<MonthlyCost> { new(new DateTime(2026, 1, 1), 15m) }
            }
        };
    }

    private static void VerifyLogged(
        Mock<ILogger<DirectManufactureCostProvider>> logger,
        LogLevel level,
        string substring)
    {
        logger.Verify(l => l.Log(
            level,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(substring)),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()!),
            Times.AtLeastOnce);
    }

    private DirectManufactureCostProvider CreateProvider(
        IDirectManufactureCostCache? cache = null,
        ICatalogRepository? catalogRepository = null,
        ILogger<DirectManufactureCostProvider>? logger = null,
        DataSourceOptions? options = null)
    {
        return new DirectManufactureCostProvider(
            cache ?? Mock.Of<IDirectManufactureCostCache>(),
            catalogRepository ?? Mock.Of<ICatalogRepository>(),
            logger ?? Mock.Of<ILogger<DirectManufactureCostProvider>>(),
            Options.Create(options ?? new DataSourceOptions())
        );
    }
}
