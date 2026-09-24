### task: split-catalog-history-refresh-service

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogHistoryRefreshService.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogHistoryRefreshServiceTests.cs`

- [ ] **Step 1: Create `CatalogHistoryRefreshService.cs`**

```csharp
using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

/// <summary>
/// Refreshes time-windowed catalog history: sales, set-parts composition, purchase history,
/// consumed-material history, and manufacture history.
/// </summary>
public sealed class CatalogHistoryRefreshService
{
    private readonly ICatalogSalesClient _salesClient;
    private readonly ICatalogSetPartsClient _setPartsClient;
    private readonly IPurchaseHistoryClient _purchaseHistoryClient;
    private readonly IConsumedMaterialsClient _consumedMaterialClient;
    private readonly ICatalogManufactureSource _manufactureSource;
    private readonly ICatalogResilienceService _resilienceService;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<DataSourceOptions> _options;
    private readonly CatalogCacheStore _cacheStore;
    private readonly ILogger<CatalogHistoryRefreshService> _logger;

    public CatalogHistoryRefreshService(
        ICatalogSalesClient salesClient,
        ICatalogSetPartsClient setPartsClient,
        IPurchaseHistoryClient purchaseHistoryClient,
        IConsumedMaterialsClient consumedMaterialClient,
        ICatalogManufactureSource manufactureSource,
        ICatalogResilienceService resilienceService,
        TimeProvider timeProvider,
        IOptions<DataSourceOptions> options,
        CatalogCacheStore cacheStore,
        ILogger<CatalogHistoryRefreshService> logger)
    {
        _salesClient = salesClient ?? throw new ArgumentNullException(nameof(salesClient));
        _setPartsClient = setPartsClient ?? throw new ArgumentNullException(nameof(setPartsClient));
        _purchaseHistoryClient = purchaseHistoryClient ?? throw new ArgumentNullException(nameof(purchaseHistoryClient));
        _consumedMaterialClient = consumedMaterialClient ?? throw new ArgumentNullException(nameof(consumedMaterialClient));
        _manufactureSource = manufactureSource ?? throw new ArgumentNullException(nameof(manufactureSource));
        _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RefreshSalesData(CancellationToken ct)
    {
        try
        {
            _cacheStore.SetSalesData(await _resilienceService.ExecuteWithResilienceAsync(
                async (cancellationToken) => await _salesClient.GetAsync(
                    _timeProvider.GetUtcNow().Date.AddDays(-1 * _options.Value.SalesHistoryDays),
                    _timeProvider.GetUtcNow().Date,
                    cancellationToken: cancellationToken),
                "RefreshSalesData", ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "RefreshSalesData failed after all retries — retaining stale cache. Items in cache: {Count}", _cacheStore.GetSalesData().Count);
        }
    }

    /// <summary>
    /// Reads the ERP stock cache to decide which products are bundles, so it must run after
    /// RefreshErpStockData has populated it. A hydration tier runs its tasks concurrently, so this
    /// task is configured one tier later than RefreshErpStockData rather than relying on ordering
    /// within a tier — otherwise a cold start can leave bundle expansion inactive until the next
    /// scheduled run.
    /// </summary>
    public async Task RefreshSetPartsData(CancellationToken ct)
    {
        try
        {
            var bundleCodes = _cacheStore.GetErpStockData()
                .Where(s => BundleProductRule.Resolve((ProductType?)s.ProductTypeId ?? ProductType.UNDEFINED, s.ProductCode) == ProductType.Set)
                .Select(s => s.ProductCode)
                .ToList();

            if (bundleCodes.Count == 0)
            {
                _logger.LogWarning(
                    "RefreshSetPartsData found no bundle-coded products in ERP stock — bundle sales expansion will be inactive. Retaining existing set-parts cache. Items in cache: {Count}",
                    _cacheStore.GetSetPartsData().Count);
                return;
            }

            var (setParts, failedCount) = await FetchSetPartsPerBundleAsync(bundleCodes, ct);

            if (failedCount == bundleCodes.Count)
            {
                _logger.LogWarning(
                    "RefreshSetPartsData could not fetch any of {BundleCount} bundles — retaining stale cache. Items in cache: {Count}",
                    bundleCodes.Count,
                    _cacheStore.GetSetPartsData().Count);
                return;
            }

            _cacheStore.SetSetPartsData(setParts);

            _logger.LogInformation(
                "RefreshSetPartsData refreshed successfully: {BundleCount} bundles resolved ({FailedCount} failed), {PartCount} parts retrieved",
                bundleCodes.Count,
                failedCount,
                setParts.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "RefreshSetPartsData failed after all retries — retaining stale cache. Items in cache: {Count}", _cacheStore.GetSetPartsData().Count);
        }
    }

    /// <summary>
    /// Fetches set parts one bundle at a time. Flexi has no batch endpoint for bundle composition,
    /// so a single resilience execution around the whole loop would spend one 30s timeout budget on
    /// N sequential calls and start failing outright once the bundle count grows. Per-bundle
    /// execution also keeps one broken bundle definition from blocking every other bundle.
    /// </summary>
    private async Task<(IList<CatalogSetPart> Parts, int FailedCount)> FetchSetPartsPerBundleAsync(
        IReadOnlyList<string> bundleCodes,
        CancellationToken ct)
    {
        var parts = new List<CatalogSetPart>();
        var failedCount = 0;

        foreach (var bundleCode in bundleCodes)
        {
            try
            {
                var bundleParts = await _resilienceService.ExecuteWithResilienceAsync(
                    async (cancellationToken) => (IList<CatalogSetPart>)(await _setPartsClient.GetAsync(
                        new[] { bundleCode },
                        cancellationToken)).ToList(),
                    "RefreshSetPartsData", ct);

                parts.AddRange(bundleParts);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failedCount++;
                _logger.LogWarning(
                    ex,
                    "RefreshSetPartsData could not fetch bundle {SetCode} — its sales will not be expanded this cycle.",
                    bundleCode);
            }
        }

        return (parts, failedCount);
    }

    public async Task RefreshPurchaseHistoryData(CancellationToken ct)
    {
        _cacheStore.SetPurchaseHistoryData((await _purchaseHistoryClient.GetHistoryAsync(null, _timeProvider.GetUtcNow().Date.AddDays(-1 * _options.Value.PurchaseHistoryDays), _timeProvider.GetUtcNow().Date, cancellationToken: ct))
            .ToList());
    }

    public async Task RefreshConsumedHistoryData(CancellationToken ct)
    {
        _cacheStore.SetConsumedData((await _consumedMaterialClient.GetConsumedAsync(_timeProvider.GetUtcNow().Date.AddDays(-1 * _options.Value.ConsumedHistoryDays), _timeProvider.GetUtcNow().Date, cancellationToken: ct))
            .ToList());
    }

    public async Task RefreshManufactureHistoryData(CancellationToken ct)
    {
        _cacheStore.SetManufactureHistoryData((await _manufactureSource.GetManufactureHistoryAsync(
            _timeProvider.GetUtcNow().Date.AddDays(-1 * _options.Value.ManufactureHistoryDays),
            _timeProvider.GetUtcNow().Date,
            ct)).ToList());
    }
}
```

- [ ] **Step 2: Build the Application project to confirm it compiles standalone**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: `Build succeeded.` (the old `CatalogDataRefreshService.cs` still exists at this point and still compiles too — this step only proves the new file's own syntax/usings/types are correct; `CatalogRepository`/`CatalogModule` are not touched yet, so there is no duplicate-registration or missing-reference error to worry about here)

- [ ] **Step 3: Create `CatalogHistoryRefreshServiceTests.cs`**, moving the 6 relevant test cases from `CatalogDataRefreshServiceTests.cs` verbatim (`RefreshSalesData_WhenResilienceThrows_RetainsStaleCacheAndLogsWarning`, `RefreshSetPartsData_FetchesPartsOnlyForBundleCodedProducts`, `RefreshSetPartsData_WhenOneBundleFails_KeepsPartsFromTheOthers`, `RefreshSetPartsData_FetchesEachBundleSeparately`, `RefreshSetPartsData_WhenResilienceThrows_RetainsStaleCacheAndLogsWarning`, `RefreshSetPartsData_WhenNoBundleCodedProductsExist_RetainsExistingCacheAndLogsWarning`), with a `CreateService` helper scoped to this class's 10 constructor parameters:

```csharp
using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
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
```

- [ ] **Step 4: Run the new test file to confirm all 6 tests pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CatalogHistoryRefreshServiceTests"`
Expected: `Passed! - Failed: 0, Passed: 6`

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogHistoryRefreshService.cs
git add backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogHistoryRefreshServiceTests.cs
git commit -m "feat(catalog): extract CatalogHistoryRefreshService from CatalogDataRefreshService"
```

---

