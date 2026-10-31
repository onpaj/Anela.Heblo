using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

/// <summary>
/// Covers writing the Top product flag back into the catalog caches. The flag only reaches the
/// merged catalog through a catalog-wide merge that runs after a debounce, so without a direct
/// cache patch the UI refetch that follows a toggle reads the old value.
/// </summary>
public class CatalogCacheStoreTopProductTests
{
    private const string ProductCode = "PROD001";
    private const string OtherProductCode = "PROD002";

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
            new BundleSalesExpander(),
            TimeProvider.System,
            Mock.Of<ILogger<CatalogMergeService>>());
        return (store, merge);
    }

    private static ErpStock ErpStockFor(string productCode, int id) =>
        new() { ProductCode = productCode, ProductName = productCode, ProductId = id, Stock = 1 };

    private static CatalogAttributes AttributesFor(string productCode, int id, bool isTopProduct) =>
        new() { ProductCode = productCode, ProductId = id, IsTopProduct = isTopProduct };

    private async Task<(CatalogCacheStore store, CatalogMergeService merge)> CreateMergedCatalog(bool isTopProduct)
    {
        var (store, merge) = Create();
        store.SetErpStockData(new List<ErpStock> { ErpStockFor(ProductCode, 1), ErpStockFor(OtherProductCode, 2) });
        store.SetCatalogAttributesData(new List<CatalogAttributes>
        {
            AttributesFor(ProductCode, 1, isTopProduct),
            AttributesFor(OtherProductCode, 2, false),
        });
        await merge.ExecutePriorityMergeAsync();
        return (store, merge);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ApplyTopProduct_UpdatesTheMergedCatalogImmediately(bool isTopProduct)
    {
        // Arrange
        var (store, _) = await CreateMergedCatalog(isTopProduct: !isTopProduct);

        // Act
        await store.ApplyTopProductAsync(ProductCode, isTopProduct);

        // Assert
        store.GetCatalogData().Single(p => p.ProductCode == ProductCode)
            .Properties.IsTopProduct.Should().Be(isTopProduct);
    }

    [Fact]
    public async Task ApplyTopProduct_LeavesOtherProductsUntouched()
    {
        // Arrange
        var (store, _) = await CreateMergedCatalog(isTopProduct: false);

        // Act
        await store.ApplyTopProductAsync(ProductCode, true);

        // Assert
        store.GetCatalogData().Single(p => p.ProductCode == OtherProductCode)
            .Properties.IsTopProduct.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyTopProduct_DoesNotMutateAggregatesHeldByConcurrentReaders()
    {
        // Arrange
        var (store, _) = await CreateMergedCatalog(isTopProduct: false);
        var snapshotHeldByReader = store.GetCatalogData();
        var aggregateHeldByReader = snapshotHeldByReader.Single(p => p.ProductCode == ProductCode);

        // Act
        await store.ApplyTopProductAsync(ProductCode, true);

        // Assert
        aggregateHeldByReader.Properties.IsTopProduct.Should().BeFalse();
        snapshotHeldByReader.Single(p => p.ProductCode == ProductCode).Properties.IsTopProduct.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyTopProduct_NewFlagSurvivesTheNextMerge()
    {
        // Arrange - the attributes source cache still holds the pre-toggle value, and every merge
        // rebuilds the aggregates from the source caches
        var (store, merge) = await CreateMergedCatalog(isTopProduct: false);

        // Act
        await store.ApplyTopProductAsync(ProductCode, true);
        await merge.ExecutePriorityMergeAsync();

        // Assert
        store.GetCatalogData().Single(p => p.ProductCode == ProductCode)
            .Properties.IsTopProduct.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyTopProduct_PatchesOnlyTheProductsAttributesInTheSourceCache()
    {
        // Arrange
        var (store, _) = await CreateMergedCatalog(isTopProduct: false);
        var previousList = store.GetCatalogAttributesData();
        var previousEntry = previousList.Single(a => a.ProductCode == ProductCode);

        // Act
        await store.ApplyTopProductAsync(ProductCode, true);

        // Assert
        var attributes = store.GetCatalogAttributesData();
        attributes.Single(a => a.ProductCode == ProductCode).IsTopProduct.Should().BeTrue();
        attributes.Single(a => a.ProductCode == OtherProductCode).IsTopProduct.Should().BeFalse();
        previousEntry.IsTopProduct.Should().BeFalse();
        previousList.Single(a => a.ProductCode == ProductCode).IsTopProduct.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyTopProduct_SchedulesAMergeSoARacingMergeConverges()
    {
        // Arrange - a merge already in flight may install a list built from the old flag; a debounced merge converges
        var (store, _) = await CreateMergedCatalog(isTopProduct: false);
        _schedulerMock.Invocations.Clear();

        // Act
        await store.ApplyTopProductAsync(ProductCode, true);

        // Assert
        _schedulerMock.Verify(s => s.ScheduleMerge("TopProductWriteBack"), Times.Once);
    }

    [Fact]
    public async Task ApplyTopProduct_ProductWithoutAttributesEntry_StillSurvivesTheNextMerge()
    {
        // Arrange - a product whose only attribute row is the Top flag is absent from the attributes
        // query, so MergeAttributes skips it and the merge keeps the previous aggregate's value
        var (store, merge) = Create();
        store.SetErpStockData(new List<ErpStock> { ErpStockFor(ProductCode, 1) });
        store.SetCatalogAttributesData(new List<CatalogAttributes>());
        await merge.ExecutePriorityMergeAsync();

        // Act
        await store.ApplyTopProductAsync(ProductCode, true);
        await merge.ExecutePriorityMergeAsync();

        // Assert
        store.GetCatalogData().Single(p => p.ProductCode == ProductCode)
            .Properties.IsTopProduct.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyTopProduct_UnknownProduct_ChangesNothing()
    {
        // Arrange
        var (store, _) = await CreateMergedCatalog(isTopProduct: false);

        // Act
        await store.ApplyTopProductAsync("MISSING", true);

        // Assert
        store.GetCatalogData().Should().OnlyContain(p => !p.Properties.IsTopProduct);
        store.GetCatalogAttributesData().Should().OnlyContain(a => !a.IsTopProduct);
    }

    [Fact]
    public async Task ApplyTopProduct_BlankProductCode_Throws()
    {
        // Arrange
        var (store, _) = Create();

        // Act
        var act = () => store.ApplyTopProductAsync(" ", true);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
