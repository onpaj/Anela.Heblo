using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public sealed class CatalogStockRefreshServiceTests
{
    private readonly MemoryCache _memoryCache;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<CatalogCacheOptions> _cacheOptions;
    private readonly CatalogCacheStore _cacheStore;
    private readonly Mock<ICatalogMergeScheduler> _mergeSchedulerMock;
    private readonly Mock<ILogger<CatalogCacheStore>> _cacheStoreLoggerMock;
    private readonly Mock<ILogger<CatalogStockRefreshService>> _serviceLoggerMock;

    public CatalogStockRefreshServiceTests()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _timeProvider = TimeProvider.System;
        _mergeSchedulerMock = new Mock<ICatalogMergeScheduler>();
        _cacheStoreLoggerMock = new Mock<ILogger<CatalogCacheStore>>();
        _serviceLoggerMock = new Mock<ILogger<CatalogStockRefreshService>>();

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

        var service = CreateService(
            erpStockClient: erpStockClientMock.Object,
            resilienceService: resilienceServiceMock.Object);

        // Act
        await service.RefreshErpStockData(CancellationToken.None);

        // Assert
        _cacheStore.GetErpStockData().Should().HaveCount(1);
        _cacheStore.GetErpStockData().First().ProductCode.Should().Be("P001");
        _cacheStore.GetErpStockData().First().Stock.Should().Be(100m);
    }

    /// <summary>
    /// Helper to create a CatalogStockRefreshService with minimal mocks.
    /// Only the mocked dependencies are set; others use loose mocks.
    /// </summary>
    private CatalogStockRefreshService CreateService(
        IErpStockClient? erpStockClient = null,
        IEshopStockClient? eshopStockClient = null,
        ICatalogTransportSource? transportSource = null,
        ICatalogPurchaseSource? purchaseSource = null,
        ICatalogManufactureSource? manufactureSource = null,
        ICatalogResilienceService? resilienceService = null)
    {
        return new CatalogStockRefreshService(
            erpStockClient ?? new Mock<IErpStockClient>().Object,
            eshopStockClient ?? new Mock<IEshopStockClient>().Object,
            transportSource ?? new Mock<ICatalogTransportSource>().Object,
            purchaseSource ?? new Mock<ICatalogPurchaseSource>().Object,
            manufactureSource ?? new Mock<ICatalogManufactureSource>().Object,
            resilienceService ?? new Mock<ICatalogResilienceService>().Object,
            _cacheStore,
            _serviceLoggerMock.Object);
    }
}
