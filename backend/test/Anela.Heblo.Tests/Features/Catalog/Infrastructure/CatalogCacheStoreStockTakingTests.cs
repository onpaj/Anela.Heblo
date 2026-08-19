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
    public void ApplyErpStockTaking_UpdatesErpStockSourceCache()
    {
        // Arrange
        var (store, _) = Create();
        store.SetErpStockData(new List<ErpStock>
        {
            new() { ProductCode = ProductCode, ProductName = "Material 1", ProductId = 1, Stock = 10 },
            new() { ProductCode = "MAT002", ProductName = "Material 2", ProductId = 2, Stock = 20 },
        });

        // Act
        store.ApplyErpStockTaking(ProductCode, newStock: 42.5m, lots: null);

        // Assert
        store.GetErpStockData().Single(s => s.ProductCode == ProductCode).Stock.Should().Be(42.5m);
        store.GetErpStockData().Single(s => s.ProductCode == "MAT002").Stock.Should().Be(20);
    }

    [Fact]
    public void ApplyErpStockTaking_DoesNotMutateThePreviouslyCachedErpStockList()
    {
        // Arrange
        var (store, _) = Create();
        var original = new ErpStock { ProductCode = ProductCode, ProductName = "Material 1", ProductId = 1, Stock = 10 };
        var originalList = new List<ErpStock> { original };
        store.SetErpStockData(originalList);

        // Act
        store.ApplyErpStockTaking(ProductCode, newStock: 42.5m, lots: null);

        // Assert - readers holding the previous snapshot are unaffected
        original.Stock.Should().Be(10);
        originalList.Should().ContainSingle().Which.Stock.Should().Be(10);
    }

    [Fact]
    public void ApplyErpStockTaking_ReplacesOnlyTheProductsLotsInTheSourceCache()
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
        store.ApplyErpStockTaking(ProductCode, newStock: 12m, lots: newLots);

        // Assert
        var lots = store.GetLotsData();
        lots.Where(l => l.ProductCode == ProductCode).Select(l => l.Lot).Should().BeEquivalentTo(new[] { "NEW-A" });
        lots.Should().ContainSingle(l => l.ProductCode == "MAT002");
    }

    [Fact]
    public void ApplyErpStockTaking_WithNullLots_LeavesLotsSourceCacheUntouched()
    {
        // Arrange
        var (store, _) = Create();
        store.SetLotsData(new List<CatalogLot>
        {
            new() { ProductCode = ProductCode, Lot = "OLD-A", Amount = 4 },
        });

        // Act
        store.ApplyErpStockTaking(ProductCode, newStock: 12m, lots: null);

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
        store.ApplyErpStockTaking(
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
        store.ApplyErpStockTaking(ProductCode, newStock: 42.5m, lots: new List<CatalogLot>());

        // Assert - visible without waiting for a merge
        store.TryGetCurrent()!.Single(p => p.ProductCode == ProductCode).Stock.Erp.Should().Be(42.5m);
    }

    [Fact]
    public void ApplyErpStockTaking_ProductMissingFromErpSourceCache_DoesNotThrow()
    {
        // Arrange
        var (store, _) = Create();
        store.SetErpStockData(new List<ErpStock>
        {
            new() { ProductCode = "MAT002", ProductName = "Material 2", ProductId = 2, Stock = 20 },
        });

        // Act
        var act = () => store.ApplyErpStockTaking(ProductCode, newStock: 42.5m, lots: null);

        // Assert
        act.Should().NotThrow();
        store.GetErpStockData().Should().ContainSingle().Which.Stock.Should().Be(20);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ApplyErpStockTaking_WithBlankProductCode_Throws(string productCode)
    {
        // Arrange
        var (store, _) = Create();

        // Act
        var act = () => store.ApplyErpStockTaking(productCode, newStock: 1m, lots: null);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("productCode");
    }
}
