using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.EshopUrl;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;
using Anela.Heblo.Domain.Features.Catalog.Price;
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

namespace Anela.Heblo.Tests.Features.Catalog;

public class CatalogRepositoryStaleDataAndChangesPendingTests
{
    private readonly Mock<ICatalogSalesClient> _salesClientMock = new();
    private readonly Mock<ICatalogAttributesClient> _attributesClientMock = new();
    private readonly Mock<IEshopStockClient> _eshopStockClientMock = new();
    private readonly Mock<IConsumedMaterialsClient> _consumedMaterialClientMock = new();
    private readonly Mock<IPurchaseHistoryClient> _purchaseHistoryClientMock = new();
    private readonly Mock<IErpStockClient> _erpStockClientMock = new();
    private readonly Mock<ILotsClient> _lotsClientMock = new();
    private readonly Mock<IProductPriceEshopClient> _productPriceEshopClientMock = new();
    private readonly Mock<IProductPriceErpClient> _productPriceErpClientMock = new();
    private readonly Mock<IProductEshopUrlClient> _productEshopUrlClientMock = new();
    private readonly Mock<ICatalogTransportSource> _transportSourceMock = new();
    private readonly Mock<IStockTakingRepository> _stockTakingRepositoryMock = new();
    private readonly Mock<ICatalogPurchaseSource> _purchaseSourceMock = new();
    private readonly Mock<ICatalogManufactureSource> _manufactureSourceMock = new();
    private readonly Mock<IManufactureDifficultyRepository> _manufactureDifficultyRepositoryMock = new();
    private readonly Mock<ICatalogResilienceService> _resilienceServiceMock = new();
    private readonly Mock<ICatalogMergeScheduler> _mergeSchedulerMock = new();
    private readonly Mock<TimeProvider> _timeProviderMock = new();
    private readonly Mock<ILogger<CatalogRepository>> _loggerMock = new();

    private readonly IMemoryCache _cache;
    private readonly CatalogRepository _repository;

    private static readonly string[] AllLoadDateKeys =
    [
        "CachedInTransportData", "CachedManufacturedData", "CachedInReserveData",
        "CachedInQuarantineData", "CachedOrderedData", "CachedPlannedData",
        "CachedSalesData", "CachedCatalogAttributesData", "CachedErpStockData",
        "CachedEshopStockData", "CachedPurchaseHistoryData", "CachedManufactureHistoryData",
        "CachedConsumedData", "CachedStockTakingData", "CachedLotsData",
        "CachedEshopPriceData", "CachedErpPriceData", "CachedEshopUrlData",
        "CachedManufactureDifficultySettingsData",
    ];

    public CatalogRepositoryStaleDataAndChangesPendingTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(DateTimeOffset.UtcNow);

        _productEshopUrlClientMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductEshopUrl>());
        _manufactureSourceMock.Setup(x => x.GetManufacturedInventoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal>());
        _manufactureSourceMock.Setup(x => x.GetManufactureHistoryAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogManufactureRecord>());

        var cacheOptions = new CatalogCacheOptions
        {
            EnableBackgroundMerge = true,
            CacheValidityPeriod = TimeSpan.FromMinutes(5),
            AllowStaleDataDuringMerge = true,
            StaleDataRetentionPeriod = TimeSpan.FromMinutes(5),
        };
        var cacheOptionsMock = new Mock<IOptions<CatalogCacheOptions>>();
        cacheOptionsMock.Setup(x => x.Value).Returns(cacheOptions);

        var optionsMock = new Mock<IOptions<DataSourceOptions>>();
        optionsMock.Setup(x => x.Value).Returns(new DataSourceOptions
        {
            SalesHistoryDays = 30,
            PurchaseHistoryDays = 30,
            ConsumedHistoryDays = 30,
            ManufactureHistoryDays = 30,
        });

        var cacheStore = new CatalogCacheStore(
            _cache,
            _timeProviderMock.Object,
            cacheOptionsMock.Object,
            _mergeSchedulerMock.Object,
            new Mock<ILogger<CatalogCacheStore>>().Object);

        var mergeService = new CatalogMergeService(
            cacheStore,
            _timeProviderMock.Object,
            new Mock<ILogger<CatalogMergeService>>().Object);

        var refreshService = new CatalogDataRefreshService(
            _salesClientMock.Object,
            _attributesClientMock.Object,
            _eshopStockClientMock.Object,
            _consumedMaterialClientMock.Object,
            _purchaseHistoryClientMock.Object,
            _erpStockClientMock.Object,
            _lotsClientMock.Object,
            _productPriceEshopClientMock.Object,
            _productPriceErpClientMock.Object,
            _productEshopUrlClientMock.Object,
            _transportSourceMock.Object,
            _stockTakingRepositoryMock.Object,
            _purchaseSourceMock.Object,
            _manufactureSourceMock.Object,
            _manufactureDifficultyRepositoryMock.Object,
            _resilienceServiceMock.Object,
            _timeProviderMock.Object,
            optionsMock.Object,
            cacheStore,
            new Mock<ILogger<CatalogDataRefreshService>>().Object);

        _repository = new CatalogRepository(
            cacheStore,
            mergeService,
            refreshService,
            _mergeSchedulerMock.Object,
            cacheOptionsMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task GetAllAsync_WhenMergeInProgressAndStaleAvailable_LogsWarning()
    {
        var staleData = new List<CatalogAggregate>
        {
            new CatalogAggregate { ProductCode = "STALE001", ProductName = "Stale Product" }
        };
        _cache.Set("CatalogData_Stale", staleData);
        _cache.Set("CatalogData_LastUpdate", DateTime.UtcNow.AddHours(-10));

        _mergeSchedulerMock.Setup(x => x.IsMergeInProgress).Returns(true);

        var result = await _repository.GetAllAsync();

        result.Should().ContainSingle(p => p.ProductCode == "STALE001");
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Serving stale data during merge operation")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void ChangesPendingForMerge_WhenLastMergeDateTimeIsNull_ReturnsTrue()
    {
        _repository.ChangesPendingForMerge.Should().BeTrue();
    }

    [Fact]
    public void ChangesPendingForMerge_WhenAtLeastOneLoadDateIsNull_ReturnsTrue()
    {
        _cache.Set("LastMergeDateTime", (DateTime?)DateTime.UtcNow.AddMinutes(-30));
        _cache.Set("CachedErpStockData_LoadDate", (DateTime?)DateTime.UtcNow.AddMinutes(-60));

        _repository.ChangesPendingForMerge.Should().BeTrue();
    }

    [Fact]
    public void ChangesPendingForMerge_WhenAllLoadDatesBeforeLastMerge_ReturnsFalse()
    {
        var lastMerge = DateTime.UtcNow;
        _cache.Set("LastMergeDateTime", (DateTime?)lastMerge);
        SetAllLoadDates(lastMerge.AddMinutes(-5));

        _repository.ChangesPendingForMerge.Should().BeFalse();
    }

    [Fact]
    public void ChangesPendingForMerge_WhenMaxLoadDateAfterLastMerge_ReturnsTrue()
    {
        var lastMerge = DateTime.UtcNow;
        _cache.Set("LastMergeDateTime", (DateTime?)lastMerge);
        SetAllLoadDates(lastMerge.AddMinutes(-5));
        _cache.Set("CachedSalesData_LoadDate", (DateTime?)(lastMerge.AddMinutes(1)));

        _repository.ChangesPendingForMerge.Should().BeTrue();
    }

    private void SetAllLoadDates(DateTime value)
    {
        foreach (var key in AllLoadDateKeys)
            _cache.Set($"{key}_LoadDate", (DateTime?)value);
    }
}
