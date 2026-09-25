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

