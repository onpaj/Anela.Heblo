using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class CatalogCacheStoreTests
{
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly Mock<ICatalogMergeScheduler> _schedulerMock = new();
    private readonly Mock<TimeProvider> _timeProviderMock = new();
    private readonly CatalogCacheOptions _options = new() { EnableBackgroundMerge = true };

    private CatalogCacheStore CreateStore()
    {
        _timeProviderMock.Setup(t => t.GetUtcNow()).Returns(DateTimeOffset.UtcNow);
        return new CatalogCacheStore(
            _cache,
            _timeProviderMock.Object,
            Options.Create(_options),
            _schedulerMock.Object,
            Mock.Of<ILogger<CatalogCacheStore>>());
    }

    [Fact]
    public async Task ReplaceCacheAtomicallyAsync_WithExistingCurrent_PromotesToStaleThenInstallsNew()
    {
        var store = CreateStore();
        var oldList = new List<CatalogAggregate> { new() { ProductCode = "OLD" } };
        var newList = new List<CatalogAggregate> { new() { ProductCode = "NEW" } };

        await store.ReplaceCacheAtomicallyAsync(oldList);
        await store.ReplaceCacheAtomicallyAsync(newList);

        store.TryGetCurrent().Should().BeSameAs(newList);
        store.TryGetStale().Should().BeSameAs(oldList);
    }

    [Fact]
    public void GetCatalogData_WithCurrentPopulated_ReturnsCurrent_DoesNotScheduleMerge()
    {
        var store = CreateStore();
        var list = new List<CatalogAggregate> { new() { ProductCode = "A" } };
        store.ReplaceCacheAtomicallyAsync(list).GetAwaiter().GetResult();

        var result = store.GetCatalogData();

        result.Should().BeSameAs(list);
        _schedulerMock.Verify(s => s.ScheduleMerge(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetCatalogData_WithOnlyStale_ReturnsStaleAndSchedulesMerge()
    {
        var store = CreateStore();
        var oldList = new List<CatalogAggregate> { new() { ProductCode = "STALE" } };
        var newList = new List<CatalogAggregate> { new() { ProductCode = "NEW" } };
        await store.ReplaceCacheAtomicallyAsync(oldList);
        await store.ReplaceCacheAtomicallyAsync(newList);
        ((MemoryCache)_cache).Remove("CatalogData_Current");

        var result = store.GetCatalogData();

        result.Should().BeSameAs(oldList);
        _schedulerMock.Verify(s => s.ScheduleMerge("CacheRead"), Times.Once);
    }

    [Fact]
    public void GetCatalogData_EmptyCache_ReturnsEmptyAndSchedulesMerge()
    {
        var store = CreateStore();

        var result = store.GetCatalogData();

        result.Should().BeEmpty();
        _schedulerMock.Verify(s => s.ScheduleMerge("CacheEmpty"), Times.Once);
    }

    [Fact]
    public void SetSalesData_WithBackgroundMergeEnabled_SchedulesMergeAndRecordsLoadDate()
    {
        var store = CreateStore();
        var sales = new List<CatalogSaleRecord> { new() { ProductCode = "X" } };

        store.SetSalesData(sales);

        store.GetSalesData().Should().BeEquivalentTo(sales);
        store.GetLoadDate(CatalogCacheStore.SourceKeys.Sales).Should().NotBeNull();
        _schedulerMock.Verify(s => s.ScheduleMerge(CatalogCacheStore.SourceKeys.Sales), Times.Once);
    }

    [Fact]
    public void SetSalesData_WithBackgroundMergeDisabled_EvictsAggregateCacheInstead()
    {
        _options.EnableBackgroundMerge = false;
        var store = CreateStore();
        store.ReplaceCacheAtomicallyAsync(new List<CatalogAggregate> { new() { ProductCode = "A" } }).GetAwaiter().GetResult();

        store.SetSalesData(new List<CatalogSaleRecord>());

        store.TryGetCurrent().Should().BeNull();
        store.IsCacheValid().Should().BeFalse();
        _schedulerMock.Verify(s => s.ScheduleMerge(It.IsAny<string>()), Times.Never);
    }
}
