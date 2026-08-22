using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

/// <summary>
/// Covers writing an ERP stock taking result back into the catalog caches. The merge rebuilds
/// every aggregate from the source caches, so patching only the merged cache is reverted as soon
/// as any other data source refreshes - which is what made stock takings look "not saved" until
/// a manual background data refresh.
/// </summary>
public class CatalogCacheStoreStockTakingTests
{
    private const string ProductCode = "MAT001";

    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly Mock<ICatalogMergeScheduler> _schedulerMock = new();
    private readonly CatalogCacheOptions _cacheOptions = new() { EnableBackgroundMerge = true };

    private (CatalogCacheStore store, CatalogMergeService merge) Create()
    {
        var store = new CatalogCacheStore(
            _cache,
            TimeProvider.System,
            Options.Create(_cacheOptions),
            _schedulerMock.Object,
            Mock.Of<ILogger<CatalogCacheStore>>());
        var merge = new CatalogMergeService(
            store,
            TimeProvider.System,
            Mock.Of<ILogger<CatalogMergeService>>());
        return (store, merge);
    }

    [Fact]
    public async Task ApplyErpStockTaking_HoldsOffAConcurrentFullErpRefresh()
    {
        // Arrange - the source-cache patch is a read-modify-write. If a full refresh can land
        // between its read and its write, the pre-refresh snapshot is written back and every
        // other product's ERP stock silently reverts. Guarding it means the two are mutually
        // exclusive, which is what this asserts: the refresh cannot complete mid-patch.
        var refreshStarted = new ManualResetEventSlim(false);
        var refreshCompleted = new ManualResetEventSlim(false);

        var cache = new HookedMemoryCache(CachedErpStockDataKeyName, onRead: () =>
        {
            Task.Run(() =>
            {
                refreshStarted.Set();
                StoreUnderTest!.SetErpStockData(new List<ErpStock>());
                refreshCompleted.Set();
            });

            // Give the refresh thread a real chance to get in - it must be actively trying
            // to write, otherwise "did not complete" would prove nothing.
            refreshStarted.Wait(TimeSpan.FromSeconds(5));
            Thread.Sleep(50);
        });

        var store = new CatalogCacheStore(
            cache,
            TimeProvider.System,
            Options.Create(_cacheOptions),
            _schedulerMock.Object,
            Mock.Of<ILogger<CatalogCacheStore>>());
        StoreUnderTest = store;

        store.SetErpStockData(new List<ErpStock>
        {
            new() { ProductCode = ProductCode, ProductId = 1, Stock = 10 },
        });
        cache.ArmHook();

        // Act
        await store.ApplyErpStockTakingAsync(ProductCode, newStock: 42.5m, lots: null);
        refreshCompleted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

        // Assert - the refresh was held off until the patch had written, so it lands last and
        // survives. Unguarded, the refresh slips into the window and the patch writes its stale
        // snapshot over it, resurrecting the product the refresh had dropped.
        store.GetErpStockData().Should().BeEmpty(
            "a full ERP refresh must not be overwritten by the stock taking patch's stale snapshot");
    }

    /// <summary>Set by the mutual-exclusion test so its cache hook can call back into the store.</summary>
    private CatalogCacheStore? StoreUnderTest { get; set; }

    private const string CachedErpStockDataKeyName = "CachedErpStockData";

    /// <summary>
    /// MemoryCache that runs a one-shot callback when a given key is read, so a test can pin
    /// another thread inside the read-modify-write window of the stock taking patch.
    /// </summary>
    private sealed class HookedMemoryCache : IMemoryCache
    {
        private readonly IMemoryCache _inner = new MemoryCache(new MemoryCacheOptions());
        private readonly string _hookedKey;
        private readonly Action _onRead;
        private int _armed;

        public HookedMemoryCache(string hookedKey, Action onRead)
        {
            _hookedKey = hookedKey;
            _onRead = onRead;
        }

        public void ArmHook() => Interlocked.Exchange(ref _armed, 1);

        public bool TryGetValue(object key, out object? value)
        {
            if (key as string != _hookedKey || Interlocked.Exchange(ref _armed, 0) != 1)
            {
                return _inner.TryGetValue(key, out value);
            }

            // Read first, then run the callback: the caller now holds a snapshot taken before
            // whatever the callback does, which is exactly the read-modify-write window.
            var found = _inner.TryGetValue(key, out value);
            _onRead();
            return found;
        }

        public ICacheEntry CreateEntry(object key) => _inner.CreateEntry(key);
        public void Remove(object key) => _inner.Remove(key);
        public void Dispose() => _inner.Dispose();
    }

    [Fact]
    public async Task ApplyErpStockTaking_UpdatesErpStockSourceCache()
    {
        // Arrange
        var (store, _) = Create();
        store.SetErpStockData(new List<ErpStock>
        {
            new() { ProductCode = ProductCode, ProductName = "Material 1", ProductId = 1, Stock = 10 },
            new() { ProductCode = "MAT002", ProductName = "Material 2", ProductId = 2, Stock = 20 },
        });

        // Act
        await store.ApplyErpStockTakingAsync(ProductCode, newStock: 42.5m, lots: null);

        // Assert
        store.GetErpStockData().Single(s => s.ProductCode == ProductCode).Stock.Should().Be(42.5m);
        store.GetErpStockData().Single(s => s.ProductCode == "MAT002").Stock.Should().Be(20);
    }

    [Fact]
    public async Task ApplyErpStockTaking_DoesNotMutateThePreviouslyCachedErpStockList()
    {
        // Arrange
        var (store, _) = Create();
        var original = new ErpStock { ProductCode = ProductCode, ProductName = "Material 1", ProductId = 1, Stock = 10 };
        var originalList = new List<ErpStock> { original };
        store.SetErpStockData(originalList);

        // Act
        await store.ApplyErpStockTakingAsync(ProductCode, newStock: 42.5m, lots: null);

        // Assert - readers holding the previous snapshot are unaffected
        original.Stock.Should().Be(10);
        originalList.Should().ContainSingle().Which.Stock.Should().Be(10);
    }

    [Fact]
    public async Task ApplyErpStockTaking_ReplacesOnlyTheProductsLotsInTheSourceCache()
    {
        // Arrange
        var (store, _) = Create();
        store.SetLotsData(new List<CatalogLot>
        {
            new() { ProductCode = ProductCode, Lot = "OLD-A", Amount = 4 },
            new() { ProductCode = ProductCode, Lot = "OLD-B", Amount = 6 },
            new() { ProductCode = "MAT002", Lot = "OTHER", Amount = 3 },
        });

        var newLots = new List<CatalogLot>
        {
            new() { ProductCode = ProductCode, Lot = "NEW-A", Amount = 12 },
        };

        // Act
        await store.ApplyErpStockTakingAsync(ProductCode, newStock: 12m, lots: newLots);

        // Assert
        var lots = store.GetLotsData();
        lots.Where(l => l.ProductCode == ProductCode).Select(l => l.Lot).Should().BeEquivalentTo(new[] { "NEW-A" });
        lots.Should().ContainSingle(l => l.ProductCode == "MAT002");
    }

    [Fact]
    public async Task ApplyErpStockTaking_WithNullLots_LeavesLotsSourceCacheUntouched()
    {
        // Arrange
        var (store, _) = Create();
        store.SetLotsData(new List<CatalogLot>
        {
            new() { ProductCode = ProductCode, Lot = "OLD-A", Amount = 4 },
        });

        // Act
        await store.ApplyErpStockTakingAsync(ProductCode, newStock: 12m, lots: null);

        // Assert
        store.GetLotsData().Should().ContainSingle().Which.Lot.Should().Be("OLD-A");
    }

    [Fact]
    public async Task ApplyErpStockTaking_NewStockSurvivesTheNextMerge()
    {
        // Arrange - a merged catalog built from ERP stock of 10
        var (store, merge) = Create();
        store.SetErpStockData(new List<ErpStock>
        {
            new() { ProductCode = ProductCode, ProductName = "Material 1", ProductId = 1, Stock = 10, HasLots = true },
        });
        store.SetLotsData(new List<CatalogLot>
        {
            new() { ProductCode = ProductCode, Lot = "OLD-A", Amount = 10 },
        });
        await merge.ExecutePriorityMergeAsync();

        // Act - stock taking confirms 42.5 in a single new lot, then an unrelated merge runs
        await store.ApplyErpStockTakingAsync(
            ProductCode,
            newStock: 42.5m,
            lots: new List<CatalogLot> { new() { ProductCode = ProductCode, Lot = "NEW-A", Amount = 42.5m } });
        await merge.ExecutePriorityMergeAsync();

        // Assert
        var product = store.TryGetCurrent()!.Single(p => p.ProductCode == ProductCode);
        product.Stock.Erp.Should().Be(42.5m);
        product.Stock.Lots.Select(l => l.Lot).Should().BeEquivalentTo(new[] { "NEW-A" });
    }

    [Fact]
    public async Task ApplyErpStockTaking_UpdatesTheMergedCacheImmediately()
    {
        // Arrange
        var (store, merge) = Create();
        store.SetErpStockData(new List<ErpStock>
        {
            new() { ProductCode = ProductCode, ProductName = "Material 1", ProductId = 1, Stock = 10 },
        });
        await merge.ExecutePriorityMergeAsync();

        // Act
        await store.ApplyErpStockTakingAsync(ProductCode, newStock: 42.5m, lots: new List<CatalogLot>());

        // Assert - visible without waiting for a merge
        store.TryGetCurrent()!.Single(p => p.ProductCode == ProductCode).Stock.Erp.Should().Be(42.5m);
    }

    [Fact]
    public async Task ApplyErpStockTaking_DoesNotMutateTheAggregateHandedToConcurrentReaders()
    {
        // Arrange - CatalogRepository returns the cached list itself, and CatalogMergeService
        // clones from it, so patching an aggregate in place would race with anyone mid-read.
        var (store, merge) = Create();
        store.SetErpStockData(new List<ErpStock>
        {
            new() { ProductCode = ProductCode, ProductName = "Material 1", ProductId = 1, Stock = 10 },
        });
        await merge.ExecutePriorityMergeAsync();

        var readerSnapshot = store.GetCatalogData();
        var readerAggregate = readerSnapshot.Single(p => p.ProductCode == ProductCode);

        // Act
        await store.ApplyErpStockTakingAsync(ProductCode, newStock: 42.5m, lots: new List<CatalogLot>());

        // Assert - the reader's instance is untouched; the patch went into a fresh one
        readerAggregate.Stock.Erp.Should().Be(10);
        store.TryGetCurrent()!.Single(p => p.ProductCode == ProductCode).Stock.Erp.Should().Be(42.5m);
        store.TryGetCurrent().Should().NotBeSameAs(readerSnapshot);
    }

    [Fact]
    public async Task ApplyErpStockTaking_ProductMissingFromErpSourceCache_DoesNotThrow()
    {
        // Arrange
        var (store, _) = Create();
        store.SetErpStockData(new List<ErpStock>
        {
            new() { ProductCode = "MAT002", ProductName = "Material 2", ProductId = 2, Stock = 20 },
        });

        // Act
        var act = async () => await store.ApplyErpStockTakingAsync(ProductCode, newStock: 42.5m, lots: null);

        // Assert
        await act.Should().NotThrowAsync();
        store.GetErpStockData().Should().ContainSingle().Which.Stock.Should().Be(20);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ApplyErpStockTaking_WithBlankProductCode_Throws(string productCode)
    {
        // Arrange
        var (store, _) = Create();

        // Act
        var act = async () => await store.ApplyErpStockTakingAsync(productCode, newStock: 1m, lots: null);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("productCode");
    }
}
