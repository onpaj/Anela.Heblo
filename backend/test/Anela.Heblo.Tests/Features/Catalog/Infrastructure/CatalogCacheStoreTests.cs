using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class CatalogCacheStoreTests
{
    private readonly MemoryCache _memoryCache;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<CatalogCacheOptions> _cacheOptions;
    private readonly Mock<ICatalogMergeScheduler> _mergeSchedulerMock;
    private readonly Mock<ILogger<CatalogCacheStore>> _loggerMock;

    public CatalogCacheStoreTests()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _timeProvider = TimeProvider.System;
        _mergeSchedulerMock = new Mock<ICatalogMergeScheduler>();
        _loggerMock = new Mock<ILogger<CatalogCacheStore>>();

        var options = new CatalogCacheOptions
        {
            CacheValidityPeriod = TimeSpan.FromMinutes(10),
            StaleDataRetentionPeriod = TimeSpan.FromMinutes(5),
            EnableBackgroundMerge = true
        };
        _cacheOptions = Options.Create(options);
    }

    [Fact]
    public async Task ReplaceCacheAtomicallyAsync_PromotesCurrentToStale()
    {
        // Arrange
        var store = new CatalogCacheStore(
            _memoryCache,
            _timeProvider,
            _cacheOptions,
            _mergeSchedulerMock.Object,
            _loggerMock.Object);

        var oldData = new List<CatalogAggregate>
        {
            new CatalogAggregate { ProductCode = "OLD-001" }
        };
        var newData = new List<CatalogAggregate>
        {
            new CatalogAggregate { ProductCode = "NEW-001" },
            new CatalogAggregate { ProductCode = "NEW-002" }
        };

        // Set initial current data
        _memoryCache.Set("CatalogData_Current", oldData);

        // Act
        await store.ReplaceCacheAtomicallyAsync(newData);

        // Assert
        var currentResult = store.TryGetCurrent();
        var staleResult = store.TryGetStale();

        currentResult.Should().HaveCount(2).And.Contain(p => p.ProductCode == "NEW-001");
        staleResult.Should().HaveCount(1).And.Contain(p => p.ProductCode == "OLD-001");
    }

    [Fact]
    public void IsCacheValid_ReturnsFalse_WhenNoUpdateKey()
    {
        // Arrange
        var store = new CatalogCacheStore(
            _memoryCache,
            _timeProvider,
            _cacheOptions,
            _mergeSchedulerMock.Object,
            _loggerMock.Object);

        // Act
        var result = store.IsCacheValid();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsCacheValid_ReturnsFalse_WhenCacheExpired()
    {
        // Arrange
        var expiredOptions = new CatalogCacheOptions
        {
            CacheValidityPeriod = TimeSpan.FromMinutes(5),
            StaleDataRetentionPeriod = TimeSpan.FromMinutes(5),
            EnableBackgroundMerge = true
        };
        var expiredCacheOptions = Options.Create(expiredOptions);

        var store = new CatalogCacheStore(
            _memoryCache,
            _timeProvider,
            expiredCacheOptions,
            _mergeSchedulerMock.Object,
            _loggerMock.Object);

        var expiredTime = DateTime.UtcNow.AddMinutes(-10);
        _memoryCache.Set("CatalogData_LastUpdate", expiredTime);

        // Act
        var result = store.IsCacheValid();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void GetCatalogData_ReturnsStaleAndSchedulesMerge_WhenCurrentIsNull()
    {
        // Arrange
        var store = new CatalogCacheStore(
            _memoryCache,
            _timeProvider,
            _cacheOptions,
            _mergeSchedulerMock.Object,
            _loggerMock.Object);

        var staleData = new List<CatalogAggregate>
        {
            new CatalogAggregate { ProductCode = "STALE-001" }
        };
        _memoryCache.Set("CatalogData_Stale", staleData);

        // Act
        var result = store.GetCatalogData();

        // Assert
        result.Should().HaveCount(1).And.Contain(p => p.ProductCode == "STALE-001");
        _mergeSchedulerMock.Verify(m => m.ScheduleMerge("CacheRead"), Times.Once);
    }

    [Fact]
    public void SetSalesData_CallsInvalidateSourceData_TriggeringScheduleMerge()
    {
        // Arrange
        var store = new CatalogCacheStore(
            _memoryCache,
            _timeProvider,
            _cacheOptions,
            _mergeSchedulerMock.Object,
            _loggerMock.Object);

        var salesData = new List<CatalogSaleRecord>
        {
            new CatalogSaleRecord { ProductCode = "SALE-001" }
        };

        // Act
        store.SetSalesData(salesData);

        // Assert
        _mergeSchedulerMock.Verify(
            m => m.ScheduleMerge("CachedSalesData"),
            Times.Once);
    }

    [Fact]
    public void GetCatalogData_WhenScheduleMergeThrows_StillReturnsStaleData()
    {
        // Arrange
        var store = new CatalogCacheStore(
            _memoryCache,
            _timeProvider,
            _cacheOptions,
            _mergeSchedulerMock.Object,
            _loggerMock.Object);

        // Put data in stale cache
        var staleData = new List<CatalogAggregate>
        {
            new CatalogAggregate { ProductCode = "STALE001" }
        };
        _memoryCache.Set("CatalogData_Stale", staleData);

        // Make ScheduleMerge throw
        _mergeSchedulerMock.Setup(x => x.ScheduleMerge(It.IsAny<string>()))
            .Throws(new InvalidOperationException("Scheduler unavailable"));

        // Act - should NOT throw
        var result = store.GetCatalogData();

        // Assert
        result.Should().HaveCount(1);
        result.First().ProductCode.Should().Be("STALE001");
    }

    [Fact]
    public void SetSetPartsData_RoundTripsThroughCache()
    {
        // Arrange
        var store = new CatalogCacheStore(
            _memoryCache,
            _timeProvider,
            _cacheOptions,
            _mergeSchedulerMock.Object,
            _loggerMock.Object);

        var parts = new List<CatalogSetPart>
        {
            new() { SetCode = "BAL001", ComponentCode = "KRM001", ComponentName = "Krém", Amount = 2 },
        };

        // Act
        store.SetSetPartsData(parts);
        var result = store.GetSetPartsData();

        // Assert
        result.Should().BeEquivalentTo(parts);
    }

    [Fact]
    public void GetSetPartsData_ReturnsEmptyWhenNeverSet()
    {
        // Arrange
        var store = new CatalogCacheStore(
            _memoryCache,
            _timeProvider,
            _cacheOptions,
            _mergeSchedulerMock.Object,
            _loggerMock.Object);

        // Act
        var result = store.GetSetPartsData();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ReplaceCacheAtomicallyAsync_DoesNotGrantValidity_WhenRequiredSourcesNeverLoaded()
    {
        // Arrange
        var store = CreateStore();
        var merged = new List<CatalogAggregate> { new() { ProductCode = "MAS009050" } };

        // Act - a merge that ran before any source finished loading
        await store.ReplaceCacheAtomicallyAsync(merged);

        // Assert
        store.IsCacheValid().Should().BeFalse(
            "a merge built before its sources loaded must not be trusted for CacheValidityPeriod");
    }

    [Fact]
    public async Task ReplaceCacheAtomicallyAsync_DoesNotGrantValidity_WhenOneRequiredSourceIsMissing()
    {
        // Arrange - everything loaded except sales, the source that caused zeroed M2 costs
        var store = CreateStore();
        LoadRequiredSources(store, includeSales: false);
        var merged = new List<CatalogAggregate> { new() { ProductCode = "MAS009050" } };

        // Act
        await store.ReplaceCacheAtomicallyAsync(merged);

        // Assert
        store.IsCacheValid().Should().BeFalse();
    }

    [Fact]
    public async Task ReplaceCacheAtomicallyAsync_GrantsValidity_WhenAllRequiredSourcesLoaded()
    {
        // Arrange
        var store = CreateStore();
        LoadRequiredSources(store, includeSales: true);
        var merged = new List<CatalogAggregate> { new() { ProductCode = "MAS009050" } };

        // Act
        await store.ReplaceCacheAtomicallyAsync(merged);

        // Assert
        store.IsCacheValid().Should().BeTrue();
    }

    [Fact]
    public async Task ReplaceCacheAtomicallyAsync_StillServesData_WhenValidityIsWithheld()
    {
        // Arrange
        var store = CreateStore();
        var merged = new List<CatalogAggregate> { new() { ProductCode = "MAS009050" } };

        // Act
        await store.ReplaceCacheAtomicallyAsync(merged);

        // Assert - withholding the stamp must not throw the merged data away
        store.TryGetCurrent().Should().HaveCount(1);
    }

    [Fact]
    public async Task ReplaceCacheAtomicallyAsync_GrantsValidity_WhenMemoryCacheIsCompacted()
    {
        // Arrange - the ever-loaded markers are process state, not cached data, so evicting
        // everything from the shared IMemoryCache must not de-stamp the catalog.
        var store = CreateStore();
        LoadRequiredSources(store, includeSales: true);
        _memoryCache.Compact(1.0);
        var merged = new List<CatalogAggregate> { new() { ProductCode = "MAS009050" } };

        // Act
        await store.ReplaceCacheAtomicallyAsync(merged);

        // Assert
        store.IsCacheValid().Should().BeTrue();
    }

    private CatalogCacheStore CreateStore() => new(
        _memoryCache,
        _timeProvider,
        _cacheOptions,
        _mergeSchedulerMock.Object,
        _loggerMock.Object);

    private static void LoadRequiredSources(CatalogCacheStore store, bool includeSales)
    {
        store.SetErpStockData(new List<ErpStock> { new() { ProductCode = "MAS009050" } });
        store.SetPurchaseHistoryData(new List<CatalogPurchaseRecord>());
        store.SetManufactureHistoryData(new List<CatalogManufactureRecord>());

        if (includeSales)
        {
            store.SetSalesData(new List<CatalogSaleRecord>());
        }
    }
}
