using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public sealed class CatalogReferenceRefreshServiceTests
{
    private readonly MemoryCache _memoryCache;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<CatalogCacheOptions> _cacheOptions;
    private readonly CatalogCacheStore _cacheStore;
    private readonly Mock<ICatalogMergeScheduler> _mergeSchedulerMock;
    private readonly Mock<ILogger<CatalogCacheStore>> _cacheStoreLoggerMock;
    private readonly Mock<ILogger<CatalogReferenceRefreshService>> _serviceLoggerMock;

    public CatalogReferenceRefreshServiceTests()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _timeProvider = TimeProvider.System;
        _mergeSchedulerMock = new Mock<ICatalogMergeScheduler>();
        _cacheStoreLoggerMock = new Mock<ILogger<CatalogCacheStore>>();
        _serviceLoggerMock = new Mock<ILogger<CatalogReferenceRefreshService>>();

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

        var service = CreateService(manufactureDifficultyRepo: manufactureDifficultyRepoMock.Object);

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

        var service = CreateService(manufactureDifficultyRepo: manufactureDifficultyRepoMock.Object);

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

        var service = CreateService();

        // Act
        await service.RefreshManufactureCostData(CancellationToken.None);

        // Assert: the object referenced before the call is untouched
        productBefore.ManufactureHistory.Should().BeNullOrEmpty();

        // Assert: a fresh snapshot reflects the update, untouched product passed through
        var afterSnapshot = _cacheStore.TryGetCurrent()!;
        afterSnapshot.Single(p => p.ProductCode == "P100").ManufactureHistory.Should().ContainSingle();
        afterSnapshot.Single(p => p.ProductCode == "P200").ManufactureHistory.Should().BeNullOrEmpty();
    }

    /// <summary>
    /// Helper to create a CatalogReferenceRefreshService with minimal mocks.
    /// Only the mocked dependencies are set; others use loose mocks.
    /// </summary>
    private CatalogReferenceRefreshService CreateService(
        IStockTakingRepository? stockTakingRepository = null,
        IManufactureDifficultyRepository? manufactureDifficultyRepo = null)
    {
        return new CatalogReferenceRefreshService(
            stockTakingRepository ?? new Mock<IStockTakingRepository>().Object,
            manufactureDifficultyRepo ?? new Mock<IManufactureDifficultyRepository>().Object,
            _timeProvider,
            _cacheStore,
            _serviceLoggerMock.Object);
    }
}
