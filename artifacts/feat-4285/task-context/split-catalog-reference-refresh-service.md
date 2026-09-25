### task: split-catalog-reference-refresh-service

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogReferenceRefreshService.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogReferenceRefreshServiceTests.cs`

- [ ] **Step 1: Create `CatalogReferenceRefreshService.cs`**

```csharp
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

/// <summary>
/// Refreshes internal reference data (stock taking, manufacture difficulty settings) and
/// performs the manufacture-cost cross-reference pass over the cached catalog aggregate.
/// </summary>
public sealed class CatalogReferenceRefreshService
{
    private readonly IStockTakingRepository _stockTakingRepository;
    private readonly IManufactureDifficultyRepository _manufactureDifficultyRepository;
    private readonly TimeProvider _timeProvider;
    private readonly CatalogCacheStore _cacheStore;
    private readonly ILogger<CatalogReferenceRefreshService> _logger;

    public CatalogReferenceRefreshService(
        IStockTakingRepository stockTakingRepository,
        IManufactureDifficultyRepository manufactureDifficultyRepository,
        TimeProvider timeProvider,
        CatalogCacheStore cacheStore,
        ILogger<CatalogReferenceRefreshService> logger)
    {
        _stockTakingRepository = stockTakingRepository ?? throw new ArgumentNullException(nameof(stockTakingRepository));
        _manufactureDifficultyRepository = manufactureDifficultyRepository ?? throw new ArgumentNullException(nameof(manufactureDifficultyRepository));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RefreshStockTakingData(CancellationToken ct)
    {
        _cacheStore.SetStockTakingData((await _stockTakingRepository.GetAllAsync(ct)).ToList());
    }

    public async Task RefreshManufactureDifficultySettingsData(string? product, CancellationToken ct)
    {
        var difficultySettings = await _manufactureDifficultyRepository.ListAsync(product, cancellationToken: ct);

        if (product == null) // All
        {
            _cacheStore.SetManufactureDifficultySettingsData(difficultySettings
                .GroupBy(h => h.ProductCode)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(h => h.ValidFrom ?? DateTime.MinValue).ToList()));
        }
        else
        {
            // Single product: copy-then-set the dictionary so InvalidateSourceData/SetLoadDateInCache
            // run through the same Set*Data plumbing every other refresh path uses.
            var existingDict = _cacheStore.GetManufactureDifficultySettingsData();
            var newDict = new Dictionary<string, List<ManufactureDifficultySetting>>(existingDict)
            {
                [product] = difficultySettings.ToList()
            };
            _cacheStore.SetManufactureDifficultySettingsData(newDict);

            // Update the live snapshot, if one exists, by swapping in a clone of the touched
            // product rather than mutating the shared aggregate a concurrent reader may hold.
            var current = _cacheStore.TryGetCurrent();
            var productAggregate = current?.SingleOrDefault(s => s.ProductCode == product);
            if (current != null && productAggregate != null)
            {
                var updated = current.Select(p =>
                {
                    if (p != productAggregate)
                    {
                        return p;
                    }

                    var clone = p.Clone();
                    clone.ManufactureDifficultySettings.Assign(difficultySettings, _timeProvider.GetUtcNow().UtcDateTime);
                    return clone;
                }).ToList();

                await _cacheStore.ReplaceCacheAtomicallyAsync(updated);
            }
        }
    }

    public async Task RefreshManufactureCostData(CancellationToken ct)
    {
        // Add ManufactureHistory data
        var manufactureMap = _cacheStore.GetManufactureHistoryData()
            .GroupBy(p => p.ProductCode)
            .ToDictionary(k => k.Key, v => v.ToList());

        var catalogData = _cacheStore.GetCatalogData();
        var updated = (catalogData ?? []).Select(product =>
        {
            if (!manufactureMap.TryGetValue(product.ProductCode, out var manufactures))
            {
                return product;
            }

            var clone = product.Clone();
            clone.ManufactureHistory = manufactures.ToList();
            return clone;
        }).ToList();

        await _cacheStore.ReplaceCacheAtomicallyAsync(updated);
    }
}
```

- [ ] **Step 2: Build to confirm it compiles standalone**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: `Build succeeded.`

- [ ] **Step 3: Create `CatalogReferenceRefreshServiceTests.cs`**, moving the 3 relevant test cases from `CatalogDataRefreshServiceTests.cs` verbatim (`RefreshManufactureDifficultySettingsData_SingleProduct_DoesNotMutateSharedDictionaryOrAggregate`, `RefreshManufactureDifficultySettingsData_SingleProduct_NoCurrentSnapshot_UpdatesDictionaryWithoutThrowing`, `RefreshManufactureCostData_DoesNotMutateLiveCatalogAggregates`), with a `CreateService` helper scoped to this class's 5 constructor parameters:

```csharp
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
```

- [ ] **Step 4: Run the new test file to confirm all 3 tests pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CatalogReferenceRefreshServiceTests"`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogReferenceRefreshService.cs
git add backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogReferenceRefreshServiceTests.cs
git commit -m "feat(catalog): extract CatalogReferenceRefreshService from CatalogDataRefreshService"
```

---

