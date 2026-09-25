# Design: Split CatalogDataRefreshService by source family

## Component Design

### `CatalogHistoryRefreshService` (new)
**Responsibility:** Fetch and cache all time-windowed history data — sales, set-parts composition, purchase history, consumed-material history, manufacture history.

**Public interface** (identical signatures to the corresponding methods on the old `CatalogDataRefreshService`):
```csharp
public sealed class CatalogHistoryRefreshService
{
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
        ILogger<CatalogHistoryRefreshService> logger);

    public Task RefreshSalesData(CancellationToken ct);
    public Task RefreshSetPartsData(CancellationToken ct);
    public Task RefreshPurchaseHistoryData(CancellationToken ct);
    public Task RefreshConsumedHistoryData(CancellationToken ct);
    public Task RefreshManufactureHistoryData(CancellationToken ct);

    // private, moves with RefreshSetPartsData — no other method calls it
    private Task<(IList<CatalogSetPart> Parts, int FailedCount)> FetchSetPartsPerBundleAsync(
        IReadOnlyList<string> bundleCodes, CancellationToken ct);
}
```
10 constructor parameters. Every method body, log message, and comment is copied verbatim from `CatalogDataRefreshService`; only the enclosing class and (for logger-typed messages that embed the type via `ILogger<T>`) the generic type argument change.

### `CatalogStockRefreshService` (new)
**Responsibility:** Fetch and cache current stock/inventory position data — ERP stock, eshop stock, in-transport, in-reserve, in-quarantine, ordered quantities, manufactured/planned inventory.

```csharp
public sealed class CatalogStockRefreshService
{
    public CatalogStockRefreshService(
        IErpStockClient erpStockClient,
        IEshopStockClient eshopStockClient,
        ICatalogTransportSource transportSource,
        ICatalogPurchaseSource purchaseSource,
        ICatalogManufactureSource manufactureSource,
        ICatalogResilienceService resilienceService,
        CatalogCacheStore cacheStore,
        ILogger<CatalogStockRefreshService> logger);

    public Task RefreshErpStockData(CancellationToken ct);
    public Task RefreshEshopStockData(CancellationToken ct);
    public Task RefreshTransportData(CancellationToken ct);
    public Task RefreshReserveData(CancellationToken ct);
    public Task RefreshOrderedData(CancellationToken ct);
    public Task RefreshManufacturedData(CancellationToken ct);
    public Task RefreshPlannedData(CancellationToken ct);
}
```
8 constructor parameters. No `TimeProvider` or `IOptions<DataSourceOptions>` — none of these seven methods use date windows.

### `CatalogMetaRefreshService` (new)
**Responsibility:** Fetch and cache reference/metadata that describes products rather than their movement — attributes, lots, prices (eshop + ERP), eshop URLs.

```csharp
public sealed class CatalogMetaRefreshService
{
    public CatalogMetaRefreshService(
        ICatalogAttributesClient attributesClient,
        ILotsClient lotsClient,
        IProductPriceEshopClient productPriceEshopClient,
        IProductPriceErpClient productPriceErpClient,
        IProductEshopUrlClient productEshopUrlClient,
        ICatalogResilienceService resilienceService,
        CatalogCacheStore cacheStore,
        ILogger<CatalogMetaRefreshService> logger);

    public Task RefreshAttributesData(CancellationToken ct);
    public Task RefreshLotsData(CancellationToken ct);
    public Task RefreshEshopPricesData(CancellationToken ct);
    public Task RefreshErpPricesData(CancellationToken ct);
    public Task RefreshEshopUrlData(CancellationToken ct);
}
```
8 constructor parameters.

### `CatalogReferenceRefreshService` (new)
**Responsibility:** Fetch/cache internal reference data (stock taking, manufacture difficulty settings) and perform the manufacture-cost cross-reference pass over the already-cached catalog aggregate.

```csharp
public sealed class CatalogReferenceRefreshService
{
    public CatalogReferenceRefreshService(
        IStockTakingRepository stockTakingRepository,
        IManufactureDifficultyRepository manufactureDifficultyRepository,
        TimeProvider timeProvider,
        CatalogCacheStore cacheStore,
        ILogger<CatalogReferenceRefreshService> logger);

    public Task RefreshStockTakingData(CancellationToken ct);
    public Task RefreshManufactureDifficultySettingsData(string? product, CancellationToken ct);
    public Task RefreshManufactureCostData(CancellationToken ct);
}
```
5 constructor parameters. `ILogger<CatalogReferenceRefreshService>` is unused by the current method bodies (the original methods for these three operations never call `_logger` today) but is kept for parity with the other three classes and for future diagnosability — this mirrors the original class's shape where every method had a logger available even if not every method used it.

### `CatalogRepository` (modified)
**Responsibility change:** none — still implements `ICatalogRepository` end-to-end. Only its refresh-delegation wiring changes: one `CatalogDataRefreshService _refreshService` field becomes four fields, and each of the 19 one-line `Refresh*Data` delegate methods is repointed to whichever new service now owns it (see the method table in `arch-review.r1.md`). `RefreshMarginData` (implemented inline, not delegated) is untouched. Its constructor grows from 9 to 12 parameters (4 new services replacing 1 old one).

### `CatalogModule` (modified)
**Responsibility change:** none. `AddCatalogModule` registers the 4 new classes as `Transient` in place of the 1 old `Transient` registration. `RegisterBackgroundRefreshTasks` is untouched — it only ever referenced `ICatalogRepository`.

### Removed: `CatalogDataRefreshService`
Deleted entirely once its 20 methods (18 public + 1 private helper + the effectively-dead `RefreshManufactureCostData`) have all been relocated.

## Data Schemas

No data schemas change. This refactor touches only in-process class boundaries and DI wiring:

- **`CatalogCacheStore`** — no changes. Its public setter surface (`SetSalesData`, `SetSetPartsData`, `SetErpStockData`, `SetEshopStockData`, `SetInTransportData`, `SetInReserveData`, `SetInQuarantineData`, `SetOrderedData`, `SetManufacturedData`, `SetPlannedData`, `SetCatalogAttributesData`, `SetLotsData`, `SetEshopPriceData`, `SetErpPriceData`, `SetEshopUrlData`, `SetStockTakingData`, `SetManufactureDifficultySettingsData`, `SetPurchaseHistoryData`, `SetConsumedData`, `SetManufactureHistoryData`, `ReplaceCacheAtomicallyAsync`) is called from whichever new class now owns the corresponding `Refresh*` method — same calls, same arguments, same order, just a different caller.
- **`DataSourceOptions`** — no changes. Consumed by `CatalogHistoryRefreshService` (for `SalesHistoryDays`, `PurchaseHistoryDays`, `ConsumedHistoryDays`, `ManufactureHistoryDays`) exactly as it was consumed by the original class.
- **No database migration, no API contract, no event payload** is touched by this change.

## Test Data Shapes

Each new test file's `CreateService`-style helper mirrors the pattern already used by `CatalogDataRefreshServiceTests.CreateService`, but scoped to only that class's dependencies:

```csharp
// CatalogHistoryRefreshServiceTests.cs
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
```
Analogous helpers, with only the relevant subset of optional parameters, apply to the other three test files. This is the concrete mechanism by which FR-4's "no new test file's `CreateService` helper takes more than 10 parameters" acceptance criterion is met.
