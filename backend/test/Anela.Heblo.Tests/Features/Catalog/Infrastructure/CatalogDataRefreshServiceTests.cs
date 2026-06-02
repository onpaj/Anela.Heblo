using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.EshopUrl;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Domain.Features.Purchase;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class CatalogDataRefreshServiceTests
{
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly Mock<ICatalogMergeScheduler> _schedulerMock = new();
    private readonly Mock<TimeProvider> _timeProviderMock = new();
    private readonly Mock<ICatalogResilienceService> _resilienceMock = new();
    private readonly Mock<ICatalogSalesClient> _salesMock = new();
    private readonly Mock<ICatalogAttributesClient> _attributesMock = new();
    private readonly Mock<IEshopStockClient> _eshopStockMock = new();
    private readonly Mock<IConsumedMaterialsClient> _consumedMock = new();
    private readonly Mock<IPurchaseHistoryClient> _purchaseHistoryMock = new();
    private readonly Mock<IErpStockClient> _erpStockMock = new();
    private readonly Mock<ILotsClient> _lotsMock = new();
    private readonly Mock<IProductPriceEshopClient> _eshopPriceMock = new();
    private readonly Mock<IProductPriceErpClient> _erpPriceMock = new();
    private readonly Mock<IProductEshopUrlClient> _eshopUrlMock = new();
    private readonly Mock<ITransportBoxRepository> _transportBoxMock = new();
    private readonly Mock<IStockTakingRepository> _stockTakingMock = new();
    private readonly Mock<IPurchaseOrderRepository> _purchaseOrderMock = new();
    private readonly Mock<IManufactureOrderRepository> _manufactureOrderMock = new();
    private readonly Mock<IManufactureHistoryClient> _manufactureHistoryMock = new();
    private readonly Mock<IManufactureDifficultyRepository> _difficultyMock = new();
    private readonly Mock<IManufacturedProductInventoryRepository> _manufacturedInventoryMock = new();

    private CatalogCacheStore _store = default!;

    private CatalogDataRefreshService CreateService()
    {
        _timeProviderMock.Setup(t => t.GetUtcNow()).Returns(DateTimeOffset.UtcNow);
        _store = new CatalogCacheStore(
            _cache,
            _timeProviderMock.Object,
            Options.Create(new CatalogCacheOptions { EnableBackgroundMerge = true }),
            _schedulerMock.Object,
            Mock.Of<ILogger<CatalogCacheStore>>());

        return new CatalogDataRefreshService(
            _salesMock.Object,
            _attributesMock.Object,
            _eshopStockMock.Object,
            _consumedMock.Object,
            _purchaseHistoryMock.Object,
            _erpStockMock.Object,
            _lotsMock.Object,
            _eshopPriceMock.Object,
            _erpPriceMock.Object,
            _eshopUrlMock.Object,
            _transportBoxMock.Object,
            _stockTakingMock.Object,
            _purchaseOrderMock.Object,
            _manufactureOrderMock.Object,
            _manufactureHistoryMock.Object,
            _difficultyMock.Object,
            _manufacturedInventoryMock.Object,
            _resilienceMock.Object,
            _timeProviderMock.Object,
            Options.Create(new DataSourceOptions
            {
                SalesHistoryDays = 30,
                PurchaseHistoryDays = 30,
                ConsumedHistoryDays = 30,
                ManufactureHistoryDays = 30,
            }),
            _store,
            Mock.Of<ILogger<CatalogDataRefreshService>>());
    }

    [Fact]
    public async Task RefreshTransportData_SetsInTransportDataAndSchedulesMerge()
    {
        _transportBoxMock
            .Setup(r => r.FindAsync(TransportBox.IsInTransportPredicate, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox>());

        var service = CreateService();
        await service.RefreshTransportData(CancellationToken.None);

        _store.GetInTransportData().Should().NotBeNull();
        _store.GetLoadDate(CatalogCacheStore.SourceKeys.InTransport).Should().NotBeNull();
        _schedulerMock.Verify(s => s.ScheduleMerge(CatalogCacheStore.SourceKeys.InTransport), Times.Once);
    }

    [Fact]
    public async Task RefreshSalesData_WhenResilienceServiceThrows_RetainsStaleCacheAndDoesNotRethrow()
    {
        _resilienceMock
            .Setup(r => r.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<IEnumerable<CatalogSaleRecord>>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var service = CreateService();
        _store.SetSalesData(new List<CatalogSaleRecord> { new() { ProductCode = "STALE" } });

        Func<Task> act = () => service.RefreshSalesData(CancellationToken.None);
        await act.Should().NotThrowAsync();
        _store.GetSalesData().Should().ContainSingle(s => s.ProductCode == "STALE");
    }

    [Fact]
    public async Task RefreshManufactureDifficultySettingsData_WithSpecificProduct_UpdatesLiveAggregate()
    {
        var settings = new List<ManufactureDifficultySetting>
        {
            new() { ProductCode = "ABC", ValidFrom = DateTime.UtcNow.AddDays(-1) }
        };
        _difficultyMock.Setup(r => r.ListAsync("ABC", It.IsAny<CancellationToken>())).ReturnsAsync(settings);

        var service = CreateService();
        await _store.ReplaceCacheAtomicallyAsync(new List<CatalogAggregate>
        {
            new() { ProductCode = "ABC" }
        });

        await service.RefreshManufactureDifficultySettingsData("ABC", CancellationToken.None);

        _store.GetManufactureDifficultySettingsData().Should().ContainKey("ABC");
        _store.TryGetCurrent()!.Single(s => s.ProductCode == "ABC")
            .ManufactureDifficultySettings.Should().NotBeNull();
    }
}
