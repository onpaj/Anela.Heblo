using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class CatalogMergeServiceTests
{
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly Mock<ICatalogMergeScheduler> _schedulerMock = new();
    private readonly Mock<TimeProvider> _timeProviderMock = new();
    private readonly CatalogCacheOptions _cacheOptions = new() { EnableBackgroundMerge = true };

    private (CatalogCacheStore store, CatalogMergeService service) Create()
    {
        _timeProviderMock.Setup(t => t.GetUtcNow()).Returns(DateTimeOffset.UtcNow);
        var store = new CatalogCacheStore(
            _cache,
            _timeProviderMock.Object,
            Options.Create(_cacheOptions),
            _schedulerMock.Object,
            Mock.Of<ILogger<CatalogCacheStore>>());
        var service = new CatalogMergeService(
            store,
            _schedulerMock.Object,
            _timeProviderMock.Object,
            Mock.Of<ILogger<CatalogMergeService>>());
        return (store, service);
    }

    [Fact]
    public async Task ExecutePriorityMergeAsync_WithErpStockOnly_SeedsProductsFromErpStock()
    {
        var (store, service) = Create();
        store.SetErpStockData(new List<ErpStock>
        {
            new() { ProductCode = "P1", ProductName = "Product 1", ProductId = 1, Stock = 5 },
            new() { ProductCode = "P2", ProductName = "Product 2", ProductId = 2, Stock = 10 },
        });

        var result = await service.ExecutePriorityMergeAsync();

        result.Should().HaveCount(2);
        result.Should().Contain(p => p.ProductCode == "P1" && p.ProductName == "Product 1");
        store.LastMergeDateTime.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecutePriorityMergeAsync_PrefixedErpProductCode_BecomesProductTypeSet()
    {
        var (store, service) = Create();
        store.SetErpStockData(new List<ErpStock>
        {
            new() { ProductCode = "BAL001", ProductName = "Bundle 1", ProductId = 10, Stock = 0, ProductTypeId = (int)ProductType.Product },
            new() { ProductCode = "REG001", ProductName = "Regular 1", ProductId = 11, Stock = 0, ProductTypeId = (int)ProductType.Product },
        });

        var result = await service.ExecutePriorityMergeAsync();

        result.Single(p => p.ProductCode == "BAL001").Type.Should().Be(ProductType.Set);
        result.Single(p => p.ProductCode == "REG001").Type.Should().Be(ProductType.Product);
    }
}
