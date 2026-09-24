# Split CatalogDataRefreshService by source family Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Task header note:** each task below is marked with `### task: <kebab-case-id>` instead of `### Task N: [Name]` — this is the AgentHarness planning pipeline's machine-parseable task boundary (see `.claude/agents/plan-orchestrator.md`, Task Extraction). Everything else follows the superpowers:writing-plans conventions (Files section, bite-sized checkbox steps, real code, no placeholders).

**Goal:** Replace the 22-parameter `CatalogDataRefreshService` with four cohesive, independently-testable services grouped by source family, with zero behavioral change.

**Architecture:** Copy each `Refresh*` method verbatim into one of four new sealed classes (`CatalogHistoryRefreshService`, `CatalogStockRefreshService`, `CatalogMetaRefreshService`, `CatalogReferenceRefreshService`) per the method table in `arch-review.r1.md`; repoint `CatalogRepository`'s delegate methods and DI registration in `CatalogModule`; migrate the existing test file 1:1 into four new test files; delete the old class and its old test file.

**Tech Stack:** C# / .NET 8, xUnit, Moq, FluentAssertions.

---

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

### task: split-catalog-stock-refresh-service

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogStockRefreshService.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogStockRefreshServiceTests.cs`

- [ ] **Step 1: Create `CatalogStockRefreshService.cs`**

```csharp
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

/// <summary>
/// Refreshes current stock/inventory position data: ERP stock, eshop stock, in-transport,
/// in-reserve/in-quarantine, ordered quantities, and manufactured/planned inventory.
/// </summary>
public sealed class CatalogStockRefreshService
{
    private readonly IErpStockClient _erpStockClient;
    private readonly IEshopStockClient _eshopStockClient;
    private readonly ICatalogTransportSource _transportSource;
    private readonly ICatalogPurchaseSource _purchaseSource;
    private readonly ICatalogManufactureSource _manufactureSource;
    private readonly ICatalogResilienceService _resilienceService;
    private readonly CatalogCacheStore _cacheStore;
    private readonly ILogger<CatalogStockRefreshService> _logger;

    public CatalogStockRefreshService(
        IErpStockClient erpStockClient,
        IEshopStockClient eshopStockClient,
        ICatalogTransportSource transportSource,
        ICatalogPurchaseSource purchaseSource,
        ICatalogManufactureSource manufactureSource,
        ICatalogResilienceService resilienceService,
        CatalogCacheStore cacheStore,
        ILogger<CatalogStockRefreshService> logger)
    {
        _erpStockClient = erpStockClient ?? throw new ArgumentNullException(nameof(erpStockClient));
        _eshopStockClient = eshopStockClient ?? throw new ArgumentNullException(nameof(eshopStockClient));
        _transportSource = transportSource ?? throw new ArgumentNullException(nameof(transportSource));
        _purchaseSource = purchaseSource ?? throw new ArgumentNullException(nameof(purchaseSource));
        _manufactureSource = manufactureSource ?? throw new ArgumentNullException(nameof(manufactureSource));
        _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
        _cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RefreshErpStockData(CancellationToken ct)
    {
        _cacheStore.SetErpStockData(await _resilienceService.ExecuteWithResilienceAsync(
            async (cancellationToken) => (await _erpStockClient.ListAsync(cancellationToken)).ToList(),
            "RefreshErpStockData", ct));
    }

    public async Task RefreshEshopStockData(CancellationToken ct)
    {
        _cacheStore.SetEshopStockData(await _resilienceService.ExecuteWithResilienceAsync(
            async (cancellationToken) => (await _eshopStockClient.ListAsync(cancellationToken)).ToList(),
            "RefreshEshopStockData", ct));
    }

    public async Task RefreshTransportData(CancellationToken ct)
    {
        var transportData = await _transportSource.GetProductsInTransportAsync(ct);
        _cacheStore.SetInTransportData(transportData);
    }

    public async Task RefreshReserveData(CancellationToken ct)
    {
        var reserveData = await _transportSource.GetProductsInReserveAsync(ct);
        _cacheStore.SetInReserveData(reserveData);

        var quarantineData = await _transportSource.GetProductsInQuarantineAsync(ct);
        _cacheStore.SetInQuarantineData(quarantineData);
    }

    public async Task RefreshOrderedData(CancellationToken ct)
    {
        var orderedData = await _purchaseSource.GetOrderedQuantitiesAsync(ct);
        _cacheStore.SetOrderedData(orderedData);
    }

    public async Task RefreshManufacturedData(CancellationToken ct)
    {
        var manufacturedData = await _manufactureSource.GetManufacturedInventoryAsync(ct);
        _cacheStore.SetManufacturedData(manufacturedData);
    }

    public async Task RefreshPlannedData(CancellationToken ct)
    {
        var plannedData = await _manufactureSource.GetPlannedQuantitiesAsync(ct);
        _cacheStore.SetPlannedData(plannedData);
    }
}
```

- [ ] **Step 2: Build to confirm it compiles standalone**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: `Build succeeded.`

- [ ] **Step 3: Create `CatalogStockRefreshServiceTests.cs`**, moving `RefreshErpStockData_WritesToCacheStore` verbatim, with a `CreateService` helper scoped to this class's 8 constructor parameters:

```csharp
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
```

- [ ] **Step 4: Run the new test file to confirm the test passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CatalogStockRefreshServiceTests"`
Expected: `Passed! - Failed: 0, Passed: 1`

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogStockRefreshService.cs
git add backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogStockRefreshServiceTests.cs
git commit -m "feat(catalog): extract CatalogStockRefreshService from CatalogDataRefreshService"
```

---

### task: split-catalog-meta-refresh-service

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMetaRefreshService.cs`

- [ ] **Step 1: Create `CatalogMetaRefreshService.cs`**

```csharp
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Anela.Heblo.Domain.Features.Catalog.EshopUrl;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

/// <summary>
/// Refreshes catalog metadata: product attributes, lots, eshop/ERP prices, and eshop URLs.
/// </summary>
public sealed class CatalogMetaRefreshService
{
    private readonly ICatalogAttributesClient _attributesClient;
    private readonly ILotsClient _lotsClient;
    private readonly IProductPriceEshopClient _productPriceEshopClient;
    private readonly IProductPriceErpClient _productPriceErpClient;
    private readonly IProductEshopUrlClient _productEshopUrlClient;
    private readonly ICatalogResilienceService _resilienceService;
    private readonly CatalogCacheStore _cacheStore;
    private readonly ILogger<CatalogMetaRefreshService> _logger;

    public CatalogMetaRefreshService(
        ICatalogAttributesClient attributesClient,
        ILotsClient lotsClient,
        IProductPriceEshopClient productPriceEshopClient,
        IProductPriceErpClient productPriceErpClient,
        IProductEshopUrlClient productEshopUrlClient,
        ICatalogResilienceService resilienceService,
        CatalogCacheStore cacheStore,
        ILogger<CatalogMetaRefreshService> logger)
    {
        _attributesClient = attributesClient ?? throw new ArgumentNullException(nameof(attributesClient));
        _lotsClient = lotsClient ?? throw new ArgumentNullException(nameof(lotsClient));
        _productPriceEshopClient = productPriceEshopClient ?? throw new ArgumentNullException(nameof(productPriceEshopClient));
        _productPriceErpClient = productPriceErpClient ?? throw new ArgumentNullException(nameof(productPriceErpClient));
        _productEshopUrlClient = productEshopUrlClient ?? throw new ArgumentNullException(nameof(productEshopUrlClient));
        _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
        _cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RefreshAttributesData(CancellationToken ct)
    {
        _cacheStore.SetCatalogAttributesData(await _resilienceService.ExecuteWithResilienceAsync(
            async (cancellationToken) => await _attributesClient.GetAttributesAsync(cancellationToken: cancellationToken),
            "RefreshAttributesData", ct));
    }

    public async Task RefreshLotsData(CancellationToken ct)
    {
        _cacheStore.SetLotsData((await _lotsClient.GetAsync(cancellationToken: ct)).ToList());
    }

    public async Task RefreshEshopPricesData(CancellationToken ct)
    {
        _cacheStore.SetEshopPriceData((await _productPriceEshopClient.GetAllAsync(ct)).ToList());
    }

    public async Task RefreshErpPricesData(CancellationToken ct)
    {
        _cacheStore.SetErpPriceData((await _productPriceErpClient.GetAllAsync(false, ct)).ToList());
    }

    public async Task RefreshEshopUrlData(CancellationToken ct)
    {
        _cacheStore.SetEshopUrlData((await _productEshopUrlClient.GetAllAsync(ct)).ToList());
    }
}
```

- [ ] **Step 2: Build to confirm it compiles standalone**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: `Build succeeded.`

- [ ] **Step 3: Note on tests** — `CatalogDataRefreshServiceTests.cs` contains no test cases for `RefreshAttributesData`, `RefreshLotsData`, `RefreshEshopPricesData`, `RefreshErpPricesData`, or `RefreshEshopUrlData` today. Per spec FR-4 ("do not add new test cases beyond what's needed to preserve 1:1 coverage"), do **not** create a `CatalogMetaRefreshServiceTests.cs` file in this task — there is nothing to migrate. (Adding new coverage for these methods is legitimate future work but is out of scope for this SRP-only refactor.)

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMetaRefreshService.cs
git commit -m "feat(catalog): extract CatalogMetaRefreshService from CatalogDataRefreshService"
```

---

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

### task: wire-catalog-repository-and-module

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Domain/Catalog/CatalogRepositoryTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogRepositoryCacheOptimizationTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogRepositoryStaleDataAndChangesPendingTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/MarginCostWindowAlignmentTests.cs`

This task repoints every consumer of `CatalogDataRefreshService` to the four new classes. It does **not** delete `CatalogDataRefreshService` yet (that is the final task) — both old and new classes coexist during this task so each step keeps the build green.

- [ ] **Step 1: Update `CatalogRepository.cs`'s constructor and fields**

In `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs`, replace:
```csharp
    private readonly CatalogCacheStore _cacheStore;
    private readonly CatalogMergeService _mergeService;
    private readonly CatalogDataRefreshService _refreshService;
    private readonly ICatalogMergeScheduler _mergeScheduler;
    private readonly IMarginCalculationService _marginService;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<DataSourceOptions> _dataSourceOptions;
    private readonly IOptions<CatalogCacheOptions> _cacheOptions;
    private readonly ILogger<CatalogRepository> _logger;

    public CatalogRepository(
        CatalogCacheStore cacheStore,
        CatalogMergeService mergeService,
        CatalogDataRefreshService refreshService,
        ICatalogMergeScheduler mergeScheduler,
        IMarginCalculationService marginService,
        TimeProvider timeProvider,
        IOptions<DataSourceOptions> dataSourceOptions,
        IOptions<CatalogCacheOptions> cacheOptions,
        ILogger<CatalogRepository> logger)
    {
        _cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
        _mergeService = mergeService ?? throw new ArgumentNullException(nameof(mergeService));
        _refreshService = refreshService ?? throw new ArgumentNullException(nameof(refreshService));
        _mergeScheduler = mergeScheduler ?? throw new ArgumentNullException(nameof(mergeScheduler));
        _marginService = marginService ?? throw new ArgumentNullException(nameof(marginService));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _dataSourceOptions = dataSourceOptions ?? throw new ArgumentNullException(nameof(dataSourceOptions));
        _cacheOptions = cacheOptions ?? throw new ArgumentNullException(nameof(cacheOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
```
with:
```csharp
    private readonly CatalogCacheStore _cacheStore;
    private readonly CatalogMergeService _mergeService;
    private readonly CatalogHistoryRefreshService _historyRefreshService;
    private readonly CatalogStockRefreshService _stockRefreshService;
    private readonly CatalogMetaRefreshService _metaRefreshService;
    private readonly CatalogReferenceRefreshService _referenceRefreshService;
    private readonly ICatalogMergeScheduler _mergeScheduler;
    private readonly IMarginCalculationService _marginService;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<DataSourceOptions> _dataSourceOptions;
    private readonly IOptions<CatalogCacheOptions> _cacheOptions;
    private readonly ILogger<CatalogRepository> _logger;

    public CatalogRepository(
        CatalogCacheStore cacheStore,
        CatalogMergeService mergeService,
        CatalogHistoryRefreshService historyRefreshService,
        CatalogStockRefreshService stockRefreshService,
        CatalogMetaRefreshService metaRefreshService,
        CatalogReferenceRefreshService referenceRefreshService,
        ICatalogMergeScheduler mergeScheduler,
        IMarginCalculationService marginService,
        TimeProvider timeProvider,
        IOptions<DataSourceOptions> dataSourceOptions,
        IOptions<CatalogCacheOptions> cacheOptions,
        ILogger<CatalogRepository> logger)
    {
        _cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
        _mergeService = mergeService ?? throw new ArgumentNullException(nameof(mergeService));
        _historyRefreshService = historyRefreshService ?? throw new ArgumentNullException(nameof(historyRefreshService));
        _stockRefreshService = stockRefreshService ?? throw new ArgumentNullException(nameof(stockRefreshService));
        _metaRefreshService = metaRefreshService ?? throw new ArgumentNullException(nameof(metaRefreshService));
        _referenceRefreshService = referenceRefreshService ?? throw new ArgumentNullException(nameof(referenceRefreshService));
        _mergeScheduler = mergeScheduler ?? throw new ArgumentNullException(nameof(mergeScheduler));
        _marginService = marginService ?? throw new ArgumentNullException(nameof(marginService));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _dataSourceOptions = dataSourceOptions ?? throw new ArgumentNullException(nameof(dataSourceOptions));
        _cacheOptions = cacheOptions ?? throw new ArgumentNullException(nameof(cacheOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
```

- [ ] **Step 2: Update the `Refresh*` delegate methods in `CatalogRepository.cs`**

Replace the "--- Refresh delegates ---" block:
```csharp
    // --- Refresh delegates ---

    public Task RefreshTransportData(CancellationToken ct) => _refreshService.RefreshTransportData(ct);
    public Task RefreshManufacturedData(CancellationToken ct) => _refreshService.RefreshManufacturedData(ct);
    public Task RefreshReserveData(CancellationToken ct) => _refreshService.RefreshReserveData(ct);
    public Task RefreshOrderedData(CancellationToken ct) => _refreshService.RefreshOrderedData(ct);
    public Task RefreshPlannedData(CancellationToken ct) => _refreshService.RefreshPlannedData(ct);
    public Task RefreshSalesData(CancellationToken ct) => _refreshService.RefreshSalesData(ct);
    public Task RefreshSetPartsData(CancellationToken ct) => _refreshService.RefreshSetPartsData(ct);
    public Task RefreshAttributesData(CancellationToken ct) => _refreshService.RefreshAttributesData(ct);
    public Task RefreshErpStockData(CancellationToken ct) => _refreshService.RefreshErpStockData(ct);
    public Task RefreshEshopStockData(CancellationToken ct) => _refreshService.RefreshEshopStockData(ct);
    public Task RefreshPurchaseHistoryData(CancellationToken ct) => _refreshService.RefreshPurchaseHistoryData(ct);
    public Task RefreshManufactureHistoryData(CancellationToken ct) => _refreshService.RefreshManufactureHistoryData(ct);
    public Task RefreshConsumedHistoryData(CancellationToken ct) => _refreshService.RefreshConsumedHistoryData(ct);
    public Task RefreshStockTakingData(CancellationToken ct) => _refreshService.RefreshStockTakingData(ct);
    public Task RefreshLotsData(CancellationToken ct) => _refreshService.RefreshLotsData(ct);
    public Task RefreshEshopPricesData(CancellationToken ct) => _refreshService.RefreshEshopPricesData(ct);
    public Task RefreshErpPricesData(CancellationToken ct) => _refreshService.RefreshErpPricesData(ct);
    public Task RefreshEshopUrlData(CancellationToken ct) => _refreshService.RefreshEshopUrlData(ct);
    public Task RefreshManufactureDifficultySettingsData(string? product, CancellationToken ct) =>
        _refreshService.RefreshManufactureDifficultySettingsData(product, ct);
```
with:
```csharp
    // --- Refresh delegates ---

    public Task RefreshTransportData(CancellationToken ct) => _stockRefreshService.RefreshTransportData(ct);
    public Task RefreshManufacturedData(CancellationToken ct) => _stockRefreshService.RefreshManufacturedData(ct);
    public Task RefreshReserveData(CancellationToken ct) => _stockRefreshService.RefreshReserveData(ct);
    public Task RefreshOrderedData(CancellationToken ct) => _stockRefreshService.RefreshOrderedData(ct);
    public Task RefreshPlannedData(CancellationToken ct) => _stockRefreshService.RefreshPlannedData(ct);
    public Task RefreshSalesData(CancellationToken ct) => _historyRefreshService.RefreshSalesData(ct);
    public Task RefreshSetPartsData(CancellationToken ct) => _historyRefreshService.RefreshSetPartsData(ct);
    public Task RefreshAttributesData(CancellationToken ct) => _metaRefreshService.RefreshAttributesData(ct);
    public Task RefreshErpStockData(CancellationToken ct) => _stockRefreshService.RefreshErpStockData(ct);
    public Task RefreshEshopStockData(CancellationToken ct) => _stockRefreshService.RefreshEshopStockData(ct);
    public Task RefreshPurchaseHistoryData(CancellationToken ct) => _historyRefreshService.RefreshPurchaseHistoryData(ct);
    public Task RefreshManufactureHistoryData(CancellationToken ct) => _historyRefreshService.RefreshManufactureHistoryData(ct);
    public Task RefreshConsumedHistoryData(CancellationToken ct) => _historyRefreshService.RefreshConsumedHistoryData(ct);
    public Task RefreshStockTakingData(CancellationToken ct) => _referenceRefreshService.RefreshStockTakingData(ct);
    public Task RefreshLotsData(CancellationToken ct) => _metaRefreshService.RefreshLotsData(ct);
    public Task RefreshEshopPricesData(CancellationToken ct) => _metaRefreshService.RefreshEshopPricesData(ct);
    public Task RefreshErpPricesData(CancellationToken ct) => _metaRefreshService.RefreshErpPricesData(ct);
    public Task RefreshEshopUrlData(CancellationToken ct) => _metaRefreshService.RefreshEshopUrlData(ct);
    public Task RefreshManufactureDifficultySettingsData(string? product, CancellationToken ct) =>
        _referenceRefreshService.RefreshManufactureDifficultySettingsData(product, ct);
```
(`RefreshMarginData`, implemented inline below this block, is unchanged.)

- [ ] **Step 3: Update DI registration in `CatalogModule.cs`**

Replace:
```csharp
        services.AddTransient<CatalogDataRefreshService>();
```
with:
```csharp
        services.AddTransient<CatalogHistoryRefreshService>();
        services.AddTransient<CatalogStockRefreshService>();
        services.AddTransient<CatalogMetaRefreshService>();
        services.AddTransient<CatalogReferenceRefreshService>();
```
Do not touch `RegisterBackgroundRefreshTasks` — it only ever referenced `ICatalogRepository` and needs no change (confirmed in `arch-review.r1.md`, Decision 3 / FR-3).

- [ ] **Step 4: Update `backend/test/Anela.Heblo.Tests/Domain/Catalog/CatalogRepositoryTests.cs`**

Replace the field:
```csharp
    private readonly CatalogDataRefreshService _refreshService;
```
with:
```csharp
    private readonly CatalogHistoryRefreshService _historyRefreshService;
    private readonly CatalogStockRefreshService _stockRefreshService;
    private readonly CatalogMetaRefreshService _metaRefreshService;
    private readonly CatalogReferenceRefreshService _referenceRefreshService;
```
Replace the construction block:
```csharp
        _refreshService = new CatalogDataRefreshService(
            _salesClientMock.Object,
            new Mock<ICatalogSetPartsClient>().Object,
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
            _optionsMock.Object,
            _cacheStore,
            new Mock<ILogger<CatalogDataRefreshService>>().Object);

        _repository = new CatalogRepository(
            _cacheStore,
            _mergeService,
            _refreshService,
            _mergeSchedulerMock.Object,
            _marginServiceMock.Object,
            _timeProviderMock.Object,
            _optionsMock.Object,
            _cacheOptionsMock.Object,
            _loggerMock.Object);
```
with:
```csharp
        _historyRefreshService = new CatalogHistoryRefreshService(
            _salesClientMock.Object,
            new Mock<ICatalogSetPartsClient>().Object,
            _purchaseHistoryClientMock.Object,
            _consumedMaterialClientMock.Object,
            _manufactureSourceMock.Object,
            _resilienceServiceMock.Object,
            _timeProviderMock.Object,
            _optionsMock.Object,
            _cacheStore,
            new Mock<ILogger<CatalogHistoryRefreshService>>().Object);

        _stockRefreshService = new CatalogStockRefreshService(
            _erpStockClientMock.Object,
            _eshopStockClientMock.Object,
            _transportSourceMock.Object,
            _purchaseSourceMock.Object,
            _manufactureSourceMock.Object,
            _resilienceServiceMock.Object,
            _cacheStore,
            new Mock<ILogger<CatalogStockRefreshService>>().Object);

        _metaRefreshService = new CatalogMetaRefreshService(
            _attributesClientMock.Object,
            _lotsClientMock.Object,
            _productPriceEshopClientMock.Object,
            _productPriceErpClientMock.Object,
            _productEshopUrlClientMock.Object,
            _resilienceServiceMock.Object,
            _cacheStore,
            new Mock<ILogger<CatalogMetaRefreshService>>().Object);

        _referenceRefreshService = new CatalogReferenceRefreshService(
            _stockTakingRepositoryMock.Object,
            _manufactureDifficultyRepositoryMock.Object,
            _timeProviderMock.Object,
            _cacheStore,
            new Mock<ILogger<CatalogReferenceRefreshService>>().Object);

        _repository = new CatalogRepository(
            _cacheStore,
            _mergeService,
            _historyRefreshService,
            _stockRefreshService,
            _metaRefreshService,
            _referenceRefreshService,
            _mergeSchedulerMock.Object,
            _marginServiceMock.Object,
            _timeProviderMock.Object,
            _optionsMock.Object,
            _cacheOptionsMock.Object,
            _loggerMock.Object);
```

- [ ] **Step 5: Apply the identical transformation to the other three test files that construct `CatalogRepository` directly**

`backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogRepositoryCacheOptimizationTests.cs`, `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogRepositoryStaleDataAndChangesPendingTests.cs`, and `backend/test/Anela.Heblo.Tests/Features/Catalog/MarginCostWindowAlignmentTests.cs` each contain one `new CatalogDataRefreshService(...)` call followed by one `new CatalogRepository(...)` call, in the same 22-then-9-parameter shape as `CatalogRepositoryTests.cs` above (confirmed by inspection — grep `new CatalogDataRefreshService(` and `new CatalogRepository(` in each file to find the exact line ranges before editing, since line numbers will have shifted by the time this task runs). For each of the three files:
1. Replace its single `CatalogDataRefreshService` field/local with four fields/locals (`CatalogHistoryRefreshService`, `CatalogStockRefreshService`, `CatalogMetaRefreshService`, `CatalogReferenceRefreshService`), using that file's own existing mocks for whichever constructor arguments they already have in scope (mirror Step 4's field-to-parameter mapping exactly — the four new classes' constructor parameter lists are fixed and identical across every call site).
2. Replace the single `new CatalogDataRefreshService(...)` call with four `new Catalog*RefreshService(...)` calls, each populated from the mapping in Step 4.
3. Replace the `CatalogRepository` constructor call's third argument (the old `_refreshService`/`refreshService` reference) with the four new service references, in the exact parameter order defined in Step 1 (`historyRefreshService, stockRefreshService, metaRefreshService, referenceRefreshService`).

- [ ] **Step 6: Build the whole solution**

Run: `cd backend && dotnet build Anela.Heblo.sln`
Expected: `Build succeeded.` with 0 errors. If any error names a file not listed above, that file has an additional `CatalogDataRefreshService`/`CatalogRepository` construction call site this plan did not anticipate — fix it using the same mapping and re-run.

- [ ] **Step 7: Run the full backend test suite**

Run: `cd backend && dotnet test Anela.Heblo.sln`
Expected: all tests pass, including every test in `CatalogRepositoryTests.cs`, `CatalogRepositoryCacheOptimizationTests.cs`, `CatalogRepositoryStaleDataAndChangesPendingTests.cs`, `MarginCostWindowAlignmentTests.cs`, and the four new `Catalog*RefreshServiceTests.cs` files from the earlier tasks. `CatalogDataRefreshServiceTests.cs` still exists at this point and should also still pass (it is deleted in the next task, not this one).

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs
git add backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs
git add backend/test/Anela.Heblo.Tests/Domain/Catalog/CatalogRepositoryTests.cs
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogRepositoryCacheOptimizationTests.cs
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogRepositoryStaleDataAndChangesPendingTests.cs
git add backend/test/Anela.Heblo.Tests/Features/Catalog/MarginCostWindowAlignmentTests.cs
git commit -m "refactor(catalog): repoint CatalogRepository and DI wiring at the four new refresh services"
```

---

### task: remove-old-refresh-service-and-verify

**Files:**
- Delete: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogDataRefreshService.cs`
- Delete: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogDataRefreshServiceTests.cs`

- [ ] **Step 1: Confirm nothing still references `CatalogDataRefreshService`**

Run: `cd backend && grep -rn "CatalogDataRefreshService" --include="*.cs" .`
Expected: only the file `src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogDataRefreshService.cs` and the test file `test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogDataRefreshServiceTests.cs` themselves are listed. If any other file appears, stop — the previous task's Step 5 missed a call site; go fix it there before continuing.

- [ ] **Step 2: Delete both files**

```bash
git rm backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogDataRefreshService.cs
git rm backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogDataRefreshServiceTests.cs
```

- [ ] **Step 3: Build the whole solution**

Run: `cd backend && dotnet build Anela.Heblo.sln`
Expected: `Build succeeded.` with 0 errors, 0 warnings about unused usings introduced by this change (each new file's using list in the earlier tasks was scoped to only the types it actually uses).

- [ ] **Step 4: Run `dotnet format` and confirm no diff**

Run: `cd backend && dotnet format --verify-no-changes`
Expected: exits 0 (no formatting violations). If it reports violations, run `dotnet format` (without `--verify-no-changes`) to fix them, then re-run `--verify-no-changes` to confirm, then re-stage the affected files.

- [ ] **Step 5: Run the full backend test suite one final time**

Run: `cd backend && dotnet test Anela.Heblo.sln`
Expected: all tests pass. Count the total `[Fact]`/`[Theory]` cases across `CatalogHistoryRefreshServiceTests.cs` (6), `CatalogStockRefreshServiceTests.cs` (1), and `CatalogReferenceRefreshServiceTests.cs` (3) — 10 total, matching the 10 cases that existed in the now-deleted `CatalogDataRefreshServiceTests.cs`, confirming 1:1 coverage preservation (spec FR-4).

- [ ] **Step 6: Manually verify the constructor parameter counts documented in the spec/design**

Confirm by inspection: `CatalogHistoryRefreshService` has 10 constructor parameters, `CatalogStockRefreshService` has 8, `CatalogMetaRefreshService` has 8, `CatalogReferenceRefreshService` has 5 — all comfortably under the spec's "no more than 10" acceptance criterion (FR-1), versus the original 22.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor(catalog): remove CatalogDataRefreshService now that its methods live in four cohesive services"
```
