using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.EshopUrl;
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

public sealed class CatalogDataRefreshServiceTests
{
    private readonly MemoryCache _memoryCache;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<CatalogCacheOptions> _cacheOptions;
    private readonly CatalogCacheStore _cacheStore;
    private readonly Mock<ICatalogMergeScheduler> _mergeSchedulerMock;
    private readonly Mock<ILogger<CatalogCacheStore>> _cacheStoreLoggerMock;
    private readonly Mock<ILogger<CatalogDataRefreshService>> _serviceLoggerMock;

    public CatalogDataRefreshServiceTests()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _timeProvider = TimeProvider.System;
        _mergeSchedulerMock = new Mock<ICatalogMergeScheduler>();
        _cacheStoreLoggerMock = new Mock<ILogger<CatalogCacheStore>>();
        _serviceLoggerMock = new Mock<ILogger<CatalogDataRefreshService>>();

        var options = new CatalogCacheOptions
        {
            CacheValidityPeriod = TimeSpan.FromMinutes(10),
            StaleDataRetentionPeriod = TimeSpan.FromMinutes(5),
            EnableBackgroundMerge = true
        };
        _cacheOptions = Options.Create(options);

        _cacheStore = new CatalogCacheStore(
            _memoryCache,
            _timeProvider,
            _cacheOptions,
            _mergeSchedulerMock.Object,
            _cacheStoreLoggerMock.Object);
    }

    [Fact]
    public async Task RefreshSalesData_WhenResilienceThrows_RetainsStaleCacheAndLogsWarning()
    {
        // Arrange
        var staleData = new List<CatalogSaleRecord>
        {
            new CatalogSaleRecord { ProductCode = "P001", Date = DateTime.UtcNow, AmountTotal = 10 }
        };
        _cacheStore.SetSalesData(staleData);

        var resilienceServiceMock = new Mock<ICatalogResilienceService>();
        resilienceServiceMock.Setup(r => r.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<IList<CatalogSaleRecord>>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test failure"));

        var options = Options.Create(new DataSourceOptions { SalesHistoryDays = 30 });
        var service = CreateService(resilienceService: resilienceServiceMock.Object, options: options);

        // Act
        var ex = await Record.ExceptionAsync(() => service.RefreshSalesData(CancellationToken.None));

        // Assert
        ex.Should().BeNull("RefreshSalesData should not throw even when resilience fails");
        _cacheStore.GetSalesData().Should().HaveCount(1).And.Contain(p => p.ProductCode == "P001");
        _serviceLoggerMock.Verify(
            x => x.Log(
                It.Is<LogLevel>(l => l == LogLevel.Warning),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("retaining stale cache")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task RefreshManufactureDifficultySettingsData_SingleProduct_UpdatesLiveAggregate()
    {
        // Arrange
        var catalog = new List<CatalogAggregate>
        {
            new CatalogAggregate { ProductCode = "ABC" }
        };
        await _cacheStore.ReplaceCacheAtomicallyAsync(catalog);

        var newSetting = new ManufactureDifficultySetting
        {
            Id = 1,
            ProductCode = "ABC",
            DifficultyValue = 5,
            ValidFrom = DateTime.UtcNow
        };

        var manufactureDifficultyRepoMock = new Mock<IManufactureDifficultyRepository>();
        manufactureDifficultyRepoMock.Setup(r => r.ListAsync("ABC", It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureDifficultySetting> { newSetting });

        var options = Options.Create(new DataSourceOptions());
        var service = CreateService(
            manufactureDifficultyRepo: manufactureDifficultyRepoMock.Object,
            options: options);

        // Act
        await service.RefreshManufactureDifficultySettingsData("ABC", CancellationToken.None);

        // Assert
        var current = _cacheStore.TryGetCurrent();
        current.Should().NotBeNull();
        current!.First().ProductCode.Should().Be("ABC");
    }

    [Fact]
    public async Task RefreshErpStockData_WritesToCacheStore()
    {
        // Arrange
        var erpStockData = new List<ErpStock>
        {
            new ErpStock { ProductCode = "P001", Stock = 100 }
        };

        var erpStockClientMock = new Mock<IErpStockClient>();
        erpStockClientMock.Setup(c => c.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(erpStockData);

        var resilienceServiceMock = new Mock<ICatalogResilienceService>();
        resilienceServiceMock.Setup(r => r.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<List<ErpStock>>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Func<CancellationToken, Task<List<ErpStock>>> func, string name, CancellationToken ct) =>
                func(ct).Result);

        var options = Options.Create(new DataSourceOptions());
        var service = CreateService(
            erpStockClient: erpStockClientMock.Object,
            resilienceService: resilienceServiceMock.Object,
            options: options);

        // Act
        await service.RefreshErpStockData(CancellationToken.None);

        // Assert
        _cacheStore.GetErpStockData().Should().HaveCount(1);
        _cacheStore.GetErpStockData().First().ProductCode.Should().Be("P001");
        _cacheStore.GetErpStockData().First().Stock.Should().Be(100m);
    }

    /// <summary>
    /// Helper to create a CatalogDataRefreshService with minimal mocks.
    /// Only the mocked dependencies are set; others use loose mocks.
    /// </summary>
    private CatalogDataRefreshService CreateService(
        ICatalogSalesClient? salesClient = null,
        ICatalogAttributesClient? attributesClient = null,
        IEshopStockClient? eshopStockClient = null,
        IConsumedMaterialsClient? consumedMaterialClient = null,
        IPurchaseHistoryClient? purchaseHistoryClient = null,
        IErpStockClient? erpStockClient = null,
        ILotsClient? lotsClient = null,
        IProductPriceEshopClient? productPriceEshopClient = null,
        IProductPriceErpClient? productPriceErpClient = null,
        IProductEshopUrlClient? productEshopUrlClient = null,
        ITransportBoxRepository? transportBoxRepository = null,
        IStockTakingRepository? stockTakingRepository = null,
        IPurchaseOrderRepository? purchaseOrderRepository = null,
        IManufactureOrderRepository? manufactureOrderRepository = null,
        IManufactureHistoryClient? manufactureHistoryClient = null,
        IManufactureDifficultyRepository? manufactureDifficultyRepo = null,
        IManufacturedProductInventoryRepository? manufacturedInventoryRepository = null,
        ICatalogResilienceService? resilienceService = null,
        IOptions<DataSourceOptions>? options = null)
    {
        return new CatalogDataRefreshService(
            salesClient ?? new Mock<ICatalogSalesClient>().Object,
            attributesClient ?? new Mock<ICatalogAttributesClient>().Object,
            eshopStockClient ?? new Mock<IEshopStockClient>().Object,
            consumedMaterialClient ?? new Mock<IConsumedMaterialsClient>().Object,
            purchaseHistoryClient ?? new Mock<IPurchaseHistoryClient>().Object,
            erpStockClient ?? new Mock<IErpStockClient>().Object,
            lotsClient ?? new Mock<ILotsClient>().Object,
            productPriceEshopClient ?? new Mock<IProductPriceEshopClient>().Object,
            productPriceErpClient ?? new Mock<IProductPriceErpClient>().Object,
            productEshopUrlClient ?? new Mock<IProductEshopUrlClient>().Object,
            transportBoxRepository ?? new Mock<ITransportBoxRepository>().Object,
            stockTakingRepository ?? new Mock<IStockTakingRepository>().Object,
            purchaseOrderRepository ?? new Mock<IPurchaseOrderRepository>().Object,
            manufactureOrderRepository ?? new Mock<IManufactureOrderRepository>().Object,
            manufactureHistoryClient ?? new Mock<IManufactureHistoryClient>().Object,
            manufactureDifficultyRepo ?? new Mock<IManufactureDifficultyRepository>().Object,
            manufacturedInventoryRepository ?? new Mock<IManufacturedProductInventoryRepository>().Object,
            resilienceService ?? new Mock<ICatalogResilienceService>().Object,
            _timeProvider,
            options ?? Options.Create(new DataSourceOptions()),
            _cacheStore,
            _serviceLoggerMock.Object);
    }
}
