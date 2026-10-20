using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public sealed class CatalogHistoryRefreshServiceTests
{
    private readonly MemoryCache _memoryCache;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<CatalogCacheOptions> _cacheOptions;
    private readonly CatalogCacheStore _cacheStore;
    private readonly Mock<ICatalogMergeScheduler> _mergeSchedulerMock;
    private readonly Mock<ILogger<CatalogCacheStore>> _cacheStoreLoggerMock;
    private readonly Mock<ILogger<CatalogHistoryRefreshService>> _serviceLoggerMock;

    public CatalogHistoryRefreshServiceTests()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _timeProvider = TimeProvider.System;
        _mergeSchedulerMock = new Mock<ICatalogMergeScheduler>();
        _cacheStoreLoggerMock = new Mock<ILogger<CatalogCacheStore>>();
        _serviceLoggerMock = new Mock<ILogger<CatalogHistoryRefreshService>>();

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
    public async Task RefreshSetPartsData_WhenOneBundleFails_KeepsPartsFromTheOthers()
    {
        // Arrange — two bundles, one of which Flexi cannot resolve. Without per-bundle isolation
        // the broken one would take the whole refresh down and leave every bundle unexpanded.
        _cacheStore.SetErpStockData(new List<ErpStock>
        {
            new() { ProductCode = "BAL001", ProductName = "Balíček 1", ProductTypeId = (int)ProductType.Product },
            new() { ProductCode = "BAL002", ProductName = "Balíček 2", ProductTypeId = (int)ProductType.Product },
        });

        var setPartsClient = new Mock<ICatalogSetPartsClient>();
        setPartsClient
            .Setup(c => c.GetAsync(It.Is<IEnumerable<string>>(codes => codes.Contains("BAL001")), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Broken bundle definition"));
        setPartsClient
            .Setup(c => c.GetAsync(It.Is<IEnumerable<string>>(codes => codes.Contains("BAL002")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogSetPart>
            {
                new() { SetCode = "BAL002", ComponentCode = "MYD001", ComponentName = "Mýdlo", Amount = 3 },
            });

        var service = CreateService(
            setPartsClient: setPartsClient.Object,
            resilienceService: CreatePassThroughResilienceService());

        // Act
        await service.RefreshSetPartsData(CancellationToken.None);

        // Assert
        _cacheStore.GetSetPartsData().Should().ContainSingle()
            .Which.SetCode.Should().Be("BAL002");
    }

    [Fact]
    public async Task RefreshSetPartsData_FetchesEachBundleSeparately()
    {
        // Arrange — the resilience pipeline ends in a fixed timeout, so all bundles must not share
        // one execution budget. Each bundle gets its own call.
        _cacheStore.SetErpStockData(new List<ErpStock>
        {
            new() { ProductCode = "BAL001", ProductName = "Balíček 1", ProductTypeId = (int)ProductType.Product },
            new() { ProductCode = "BAL002", ProductName = "Balíček 2", ProductTypeId = (int)ProductType.Product },
        });

        var setPartsClient = new Mock<ICatalogSetPartsClient>();
        setPartsClient
            .Setup(c => c.GetAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogSetPart>());

        var service = CreateService(
            setPartsClient: setPartsClient.Object,
            resilienceService: CreatePassThroughResilienceService());

        // Act
        await service.RefreshSetPartsData(CancellationToken.None);

        // Assert
        setPartsClient.Verify(
            c => c.GetAsync(It.Is<IEnumerable<string>>(codes => codes.SequenceEqual(new[] { "BAL001" })),
                            It.IsAny<CancellationToken>()),
            Times.Once);
        setPartsClient.Verify(
            c => c.GetAsync(It.Is<IEnumerable<string>>(codes => codes.SequenceEqual(new[] { "BAL002" })),
                            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static ICatalogResilienceService CreatePassThroughResilienceService()
    {
        var mock = new Mock<ICatalogResilienceService>();
        mock.Setup(r => r.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<IList<CatalogSetPart>>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<IList<CatalogSetPart>>>, string, CancellationToken>(
                (op, _, ct) => op(ct));
        return mock.Object;
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
    /// Helper to create a CatalogHistoryRefreshService with minimal mocks.
    /// Only the mocked dependencies are set; others use loose mocks.
    /// </summary>
    private CatalogHistoryRefreshService CreateService(
        ICatalogSalesClient? salesClient = null,
        ICatalogSetPartsClient? setPartsClient = null,
        IPurchaseHistoryClient? purchaseHistoryClient = null,
        IConsumedMaterialsClient? consumedMaterialClient = null,
        ICatalogManufactureSource? manufactureSource = null,
        ICatalogResilienceService? resilienceService = null,
        IOptions<DataSourceOptions>? options = null)
    {
        return new CatalogHistoryRefreshService(
            salesClient ?? new Mock<ICatalogSalesClient>().Object,
            setPartsClient ?? new Mock<ICatalogSetPartsClient>().Object,
            purchaseHistoryClient ?? new Mock<IPurchaseHistoryClient>().Object,
            consumedMaterialClient ?? new Mock<IConsumedMaterialsClient>().Object,
            manufactureSource ?? new Mock<ICatalogManufactureSource>().Object,
            resilienceService ?? new Mock<ICatalogResilienceService>().Object,
            _timeProvider,
            options ?? Options.Create(new DataSourceOptions()),
            _cacheStore,
            _serviceLoggerMock.Object);
    }
}
