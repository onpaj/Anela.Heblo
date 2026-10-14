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

