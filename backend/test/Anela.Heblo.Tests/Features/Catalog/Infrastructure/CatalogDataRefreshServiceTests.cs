using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.EshopUrl;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Manufacture;
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
    public async Task RefreshManufactureDifficultySettingsData_SingleProduct_DoesNotMutateSharedDictionaryOrAggregate()
    {
        // Arrange
        var originalSetting = new ManufactureDifficultySetting
        {
            Id = 1,
            ProductCode = "ABC",
            DifficultyValue = 1,
            ValidFrom = DateTime.UtcNow.AddDays(-10)
        };
        _cacheStore.SetManufactureDifficultySettingsData(
            new Dictionary<string, List<ManufactureDifficultySetting>> { ["ABC"] = new List<ManufactureDifficultySetting> { originalSetting } });

        var aggregate = new CatalogAggregate { ProductCode = "ABC" };
        aggregate.ManufactureDifficultySettings.Assign(new List<ManufactureDifficultySetting> { originalSetting }, DateTime.UtcNow);
        var catalog = new List<CatalogAggregate> { aggregate };
        await _cacheStore.ReplaceCacheAtomicallyAsync(catalog);

        // Snapshot references taken BEFORE the call under test
        var dictBefore = _cacheStore.GetManufactureDifficultySettingsData();
        var aggregateBefore = _cacheStore.TryGetCurrent()!.Single(p => p.ProductCode == "ABC");

        var newSetting = new ManufactureDifficultySetting
        {
            Id = 2,
            ProductCode = "ABC",
            DifficultyValue = 5,
            ValidFrom = DateTime.UtcNow
        };

        var manufactureDifficultyRepoMock = new Mock<IManufactureDifficultyRepository>();
        manufactureDifficultyRepoMock.Setup(r => r.ListAsync("ABC", It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureDifficultySetting> { newSetting });

        var service = CreateService(manufactureDifficultyRepo: manufactureDifficultyRepoMock.Object, options: Options.Create(new DataSourceOptions()));

        // Act
        await service.RefreshManufactureDifficultySettingsData("ABC", CancellationToken.None);

        // Assert: pre-call references are untouched (isolation contract)
        dictBefore.Should().ContainKey("ABC");
        dictBefore["ABC"].Should().ContainSingle().Which.Should().Be(originalSetting);
        aggregateBefore.ManufactureDifficultySettings.Settings.Should().ContainSingle().Which.Should().Be(originalSetting);

        // Assert: a freshly-obtained snapshot reflects the update
        var dictAfter = _cacheStore.GetManufactureDifficultySettingsData();
        dictAfter["ABC"].Should().ContainSingle().Which.Should().Be(newSetting);

        var aggregateAfter = _cacheStore.TryGetCurrent()!.Single(p => p.ProductCode == "ABC");
        aggregateAfter.ManufactureDifficultySettings.Settings.Should().ContainSingle().Which.Should().Be(newSetting);
        aggregateAfter.ManufactureDifficultySettings.ManufactureDifficulty.Should().Be(5);

        // Assert: Set*Data plumbing ran (load date updated)
        _cacheStore.GetLoadDateFromCache("CachedManufactureDifficultySettingsData").Should().NotBeNull();
    }

    [Fact]
    public async Task RefreshManufactureDifficultySettingsData_SingleProduct_NoCurrentSnapshot_UpdatesDictionaryWithoutThrowing()
    {
        // Arrange - no ReplaceCacheAtomicallyAsync call, so TryGetCurrent() is null
        var newSetting = new ManufactureDifficultySetting { Id = 1, ProductCode = "XYZ", DifficultyValue = 3, ValidFrom = DateTime.UtcNow };
        var manufactureDifficultyRepoMock = new Mock<IManufactureDifficultyRepository>();
        manufactureDifficultyRepoMock.Setup(r => r.ListAsync("XYZ", It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureDifficultySetting> { newSetting });

        var service = CreateService(manufactureDifficultyRepo: manufactureDifficultyRepoMock.Object, options: Options.Create(new DataSourceOptions()));

        // Act
        var ex = await Record.ExceptionAsync(() => service.RefreshManufactureDifficultySettingsData("XYZ", CancellationToken.None));

        // Assert
        ex.Should().BeNull();
        _cacheStore.TryGetCurrent().Should().BeNull();
        _cacheStore.GetManufactureDifficultySettingsData()["XYZ"].Should().ContainSingle().Which.Should().Be(newSetting);
    }

    [Fact]
    public async Task RefreshManufactureCostData_DoesNotMutateLiveCatalogAggregates()
    {
        // Arrange
        var product = new CatalogAggregate { ProductCode = "P100" };
        var untouchedProduct = new CatalogAggregate { ProductCode = "P200" };
        var catalog = new List<CatalogAggregate> { product, untouchedProduct };
        await _cacheStore.ReplaceCacheAtomicallyAsync(catalog);

        var manufactureHistory = new List<CatalogManufactureRecord>
        {
            new CatalogManufactureRecord { ProductCode = "P100", Date = DateTime.UtcNow, Amount = 3 }
        };
        _cacheStore.SetManufactureHistoryData(manufactureHistory);

        var beforeSnapshot = _cacheStore.TryGetCurrent()!;
        var productBefore = beforeSnapshot.Single(p => p.ProductCode == "P100");

        var service = CreateService(options: Options.Create(new DataSourceOptions()));

        // Act
        await service.RefreshManufactureCostData(CancellationToken.None);

        // Assert: the object referenced before the call is untouched
        productBefore.ManufactureHistory.Should().BeNullOrEmpty();

        // Assert: a fresh snapshot reflects the update, untouched product passed through
        var afterSnapshot = _cacheStore.TryGetCurrent()!;
        afterSnapshot.Single(p => p.ProductCode == "P100").ManufactureHistory.Should().ContainSingle();
        afterSnapshot.Single(p => p.ProductCode == "P200").ManufactureHistory.Should().BeNullOrEmpty();
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

    [Fact]
    public async Task RefreshSetPartsData_FetchesPartsOnlyForBundleCodedProducts()
    {
        // Arrange
        _cacheStore.SetErpStockData(new List<ErpStock>
        {
            new() { ProductCode = "BAL001", ProductName = "Balíček", ProductTypeId = (int)ProductType.Product },
            new() { ProductCode = "KRM001", ProductName = "Krém",    ProductTypeId = (int)ProductType.Product },
        });

        var setPartsClient = new Mock<ICatalogSetPartsClient>();
        setPartsClient
            .Setup(c => c.GetAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogSetPart>
            {
                new() { SetCode = "BAL001", ComponentCode = "KRM001", ComponentName = "Krém", Amount = 2 },
            });

        var resilienceServiceMock = new Mock<ICatalogResilienceService>();
        resilienceServiceMock.Setup(r => r.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<IList<CatalogSetPart>>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<IList<CatalogSetPart>>>, string, CancellationToken>(
                (op, _, ct) => op(ct));

        var service = CreateService(
            setPartsClient: setPartsClient.Object,
            resilienceService: resilienceServiceMock.Object);

        // Act
        await service.RefreshSetPartsData(CancellationToken.None);

        // Assert
        setPartsClient.Verify(
            c => c.GetAsync(It.Is<IEnumerable<string>>(codes => codes.SequenceEqual(new[] { "BAL001" })),
                            It.IsAny<CancellationToken>()),
            Times.Once);
        _cacheStore.GetSetPartsData().Should().HaveCount(1);
    }

    [Fact]
    public async Task RefreshSetPartsData_WhenResilienceThrows_RetainsStaleCacheAndLogsWarning()
    {
        // Arrange — a bundle-coded product must exist in ERP stock, otherwise the empty-bundleCodes
        // guard (see RefreshSetPartsData_WhenNoBundleCodedProductsExist_...) returns before the
        // resilience call this test is exercising is ever reached.
        _cacheStore.SetErpStockData(new List<ErpStock>
        {
            new() { ProductCode = "BAL001", ProductName = "Balíček", ProductTypeId = (int)ProductType.Product },
        });
        _cacheStore.SetSetPartsData(new List<CatalogSetPart>
        {
            new() { SetCode = "BAL001", ComponentCode = "KRM001", ComponentName = "Krém", Amount = 2 },
        });

        var resilienceServiceMock = new Mock<ICatalogResilienceService>();
        resilienceServiceMock.Setup(r => r.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<IList<CatalogSetPart>>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test failure"));

        var service = CreateService(resilienceService: resilienceServiceMock.Object);

        // Act
        var ex = await Record.ExceptionAsync(() => service.RefreshSetPartsData(CancellationToken.None));

        // Assert
        ex.Should().BeNull("RefreshSetPartsData should not throw even when resilience fails");
        _cacheStore.GetSetPartsData().Should().HaveCount(1, "stale cache must be retained");
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
    public async Task RefreshSetPartsData_WhenNoBundleCodedProductsExist_RetainsExistingCacheAndLogsWarning()
    {
        // Arrange — ERP stock has no bundle-coded products (BundleProductRule never resolves to Set).
        _cacheStore.SetErpStockData(new List<ErpStock>
        {
            new() { ProductCode = "KRM001", ProductName = "Krém", ProductTypeId = (int)ProductType.Product },
        });

        // Previously good parts cache must survive this refresh untouched.
        _cacheStore.SetSetPartsData(new List<CatalogSetPart>
        {
            new() { SetCode = "BAL001", ComponentCode = "KRM001", ComponentName = "Krém", Amount = 2 },
        });

        var setPartsClient = new Mock<ICatalogSetPartsClient>();
        var resilienceServiceMock = new Mock<ICatalogResilienceService>();
        resilienceServiceMock.Setup(r => r.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<IList<CatalogSetPart>>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<IList<CatalogSetPart>>>, string, CancellationToken>(
                (op, _, ct) => op(ct));

        var service = CreateService(
            setPartsClient: setPartsClient.Object,
            resilienceService: resilienceServiceMock.Object);

        // Act
        await service.RefreshSetPartsData(CancellationToken.None);

        // Assert — client never called, previously good cache retained, warning logged.
        setPartsClient.Verify(
            c => c.GetAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _cacheStore.GetSetPartsData().Should().HaveCount(1, "previously populated set-parts cache must not be cleared");
        _cacheStore.GetSetPartsData().Single().SetCode.Should().Be("BAL001");

        _serviceLoggerMock.Verify(
            x => x.Log(
                It.Is<LogLevel>(l => l == LogLevel.Warning),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("no bundle-coded products")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// Helper to create a CatalogDataRefreshService with minimal mocks.
    /// Only the mocked dependencies are set; others use loose mocks.
    /// </summary>
    private CatalogDataRefreshService CreateService(
        ICatalogSalesClient? salesClient = null,
        ICatalogSetPartsClient? setPartsClient = null,
        ICatalogAttributesClient? attributesClient = null,
        IEshopStockClient? eshopStockClient = null,
        IConsumedMaterialsClient? consumedMaterialClient = null,
        IPurchaseHistoryClient? purchaseHistoryClient = null,
        IErpStockClient? erpStockClient = null,
        ILotsClient? lotsClient = null,
        IProductPriceEshopClient? productPriceEshopClient = null,
        IProductPriceErpClient? productPriceErpClient = null,
        IProductEshopUrlClient? productEshopUrlClient = null,
        ICatalogTransportSource? transportSource = null,
        IStockTakingRepository? stockTakingRepository = null,
        ICatalogPurchaseSource? purchaseSource = null,
        ICatalogManufactureSource? manufactureSource = null,
        IManufactureDifficultyRepository? manufactureDifficultyRepo = null,
        ICatalogResilienceService? resilienceService = null,
        IOptions<DataSourceOptions>? options = null)
    {
        return new CatalogDataRefreshService(
            salesClient ?? new Mock<ICatalogSalesClient>().Object,
            setPartsClient ?? new Mock<ICatalogSetPartsClient>().Object,
            attributesClient ?? new Mock<ICatalogAttributesClient>().Object,
            eshopStockClient ?? new Mock<IEshopStockClient>().Object,
            consumedMaterialClient ?? new Mock<IConsumedMaterialsClient>().Object,
            purchaseHistoryClient ?? new Mock<IPurchaseHistoryClient>().Object,
            erpStockClient ?? new Mock<IErpStockClient>().Object,
            lotsClient ?? new Mock<ILotsClient>().Object,
            productPriceEshopClient ?? new Mock<IProductPriceEshopClient>().Object,
            productPriceErpClient ?? new Mock<IProductPriceErpClient>().Object,
            productEshopUrlClient ?? new Mock<IProductEshopUrlClient>().Object,
            transportSource ?? new Mock<ICatalogTransportSource>().Object,
            stockTakingRepository ?? new Mock<IStockTakingRepository>().Object,
            purchaseSource ?? new Mock<ICatalogPurchaseSource>().Object,
            manufactureSource ?? new Mock<ICatalogManufactureSource>().Object,
            manufactureDifficultyRepo ?? new Mock<IManufactureDifficultyRepository>().Object,
            resilienceService ?? new Mock<ICatalogResilienceService>().Object,
            _timeProvider,
            options ?? Options.Create(new DataSourceOptions()),
            _cacheStore,
            _serviceLoggerMock.Object);
    }
}
