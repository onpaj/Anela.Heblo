using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class CatalogManufactureStockSyncAdapterTests
{
    private const string ProductCode = "MAT001";

    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly Mock<ILotsClient> _lotsClientMock = new();
    private readonly CatalogCacheStore _store;
    private readonly IManufactureCatalogStockSync _sync;

    public CatalogManufactureStockSyncAdapterTests()
    {
        _store = new CatalogCacheStore(
            _cache,
            TimeProvider.System,
            Options.Create(new CatalogCacheOptions { EnableBackgroundMerge = true }),
            Mock.Of<ICatalogMergeScheduler>(),
            Mock.Of<ILogger<CatalogCacheStore>>());

        _sync = new CatalogManufactureStockSyncAdapter(
            _store,
            _lotsClientMock.Object,
            Mock.Of<ILogger<CatalogManufactureStockSyncAdapter>>());

        _store.SetErpStockData(new List<ErpStock>
        {
            new() { ProductCode = ProductCode, ProductName = "Material 1", ProductId = 1, Stock = 10 },
        });
        _store.SetLotsData(new List<CatalogLot>
        {
            new() { ProductCode = ProductCode, Lot = "OLD-A", Amount = 10 },
        });
    }

    [Fact]
    public async Task SyncErpStockTakingAsync_ReloadsOnlyTheAffectedProductsLots()
    {
        // Arrange
        _lotsClientMock
            .Setup(c => c.GetAsync(ProductCode, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogLot> { new() { ProductCode = ProductCode, Lot = "NEW-A", Amount = 42.5m } });

        // Act
        await _sync.SyncErpStockTakingAsync(ProductCode, 42.5m, CancellationToken.None);

        // Assert
        _store.GetErpStockData().Single(s => s.ProductCode == ProductCode).Stock.Should().Be(42.5m);
        _store.GetLotsData().Select(l => l.Lot).Should().BeEquivalentTo(new[] { "NEW-A" });
        _lotsClientMock.Verify(
            c => c.GetAsync(ProductCode, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SyncErpStockTakingAsync_WhenLotsReloadFails_StillRefreshesStockAndKeepsKnownLots()
    {
        // Arrange
        _lotsClientMock
            .Setup(c => c.GetAsync(ProductCode, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("ERP unavailable"));

        // Act
        await _sync.SyncErpStockTakingAsync(ProductCode, 42.5m, CancellationToken.None);

        // Assert
        _store.GetErpStockData().Single(s => s.ProductCode == ProductCode).Stock.Should().Be(42.5m);
        _store.GetLotsData().Select(l => l.Lot).Should().BeEquivalentTo(new[] { "OLD-A" });
    }
}
