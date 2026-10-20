# Specification: Split CatalogDataRefreshService by source family

## Summary
`CatalogDataRefreshService` currently takes 22 constructor-injected dependencies to implement 18 independent `Refresh*` cache-population methods, violating the Single Responsibility Principle and forcing every unit test of one method to satisfy the full dependency graph. This specification defines a pure refactor — no behavioural change — that splits the class into four cohesive services grouped by source family, updates the sole consumer (`CatalogRepository`) to delegate to whichever service owns each method, and updates DI registration accordingly.

## Background
`CatalogDataRefreshService` (`backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogDataRefreshService.cs`) is registered as `Transient` in `CatalogModule.AddCatalogModule` and injected into `CatalogRepository`, which implements `ICatalogRepository` and forwards each `Refresh*Data` call to `_refreshService.Refresh*Data`. `CatalogModule.RegisterBackgroundRefreshTasks` wires 19 scheduled background tasks against `ICatalogRepository` (not against `CatalogDataRefreshService` directly) via `services.RegisterRefreshTask<ICatalogRepository>(...)`. Each registered task ultimately reaches one `CatalogDataRefreshService` method through this one-hop delegation.

The 22 constructor dependencies are:
- 11 external API clients: `ICatalogSalesClient`, `ICatalogSetPartsClient`, `IEshopStockClient`, `IConsumedMaterialsClient`, `IPurchaseHistoryClient`, `IErpStockClient`, `ILotsClient`, `IProductPriceEshopClient`, `IProductPriceErpClient`, `IProductEshopUrlClient`, `ICatalogAttributesClient`
- 3 cross-module sources: `ICatalogTransportSource`, `ICatalogPurchaseSource`, `ICatalogManufactureSource`
- 2 repositories: `IStockTakingRepository`, `IManufactureDifficultyRepository`
- 5 shared/cross-cutting: `ICatalogResilienceService`, `TimeProvider`, `IOptions<DataSourceOptions>`, `CatalogCacheStore`, `ILogger<CatalogDataRefreshService>`

The class exposes 18 public `Refresh*` methods (`RefreshTransportData`, `RefreshManufacturedData`, `RefreshReserveData`, `RefreshOrderedData`, `RefreshPlannedData`, `RefreshSalesData`, `RefreshSetPartsData`, `RefreshAttributesData`, `RefreshErpStockData`, `RefreshEshopStockData`, `RefreshPurchaseHistoryData`, `RefreshConsumedHistoryData`, `RefreshStockTakingData`, `RefreshLotsData`, `RefreshEshopPricesData`, `RefreshErpPricesData`, `RefreshEshopUrlData`, `RefreshManufactureDifficultySettingsData`) plus two more used indirectly (`RefreshManufactureHistoryData`, called by `RegisterBackgroundRefreshTasks`; `RefreshManufactureCostData`, currently **not** wired into either `CatalogRepository` or `RegisterBackgroundRefreshTasks` — it is exercised only by a unit test and appears to be dead production code, but must be preserved verbatim as it is out of scope to remove).

The GitHub issue's suggested grouping is directionally correct but does not partition the actual 18 methods cleanly (e.g. it omits `RefreshManufactureCostData`, and groups by data-domain intuition rather than by the private helper each method actually shares). This spec re-derives the grouping from the real method bodies and their shared private helpers so every method has exactly one new home and no method is dropped.

## Functional Requirements

### FR-1: Introduce four new refresh services, grouped by shared private helper / cohesive source family
Replace the single `CatalogDataRefreshService` with four new sealed classes in the same `Anela.Heblo.Application.Features.Catalog.Infrastructure` namespace and directory:

- **`CatalogHistoryRefreshService`** — sales, set-parts (and its private `FetchSetPartsPerBundleAsync` helper), purchase history, consumed-material history, manufacture history.
  - Methods: `RefreshSalesData`, `RefreshSetPartsData` (+ private `FetchSetPartsPerBundleAsync`), `RefreshPurchaseHistoryData`, `RefreshConsumedHistoryData`, `RefreshManufactureHistoryData`
  - Dependencies: `ICatalogSalesClient`, `ICatalogSetPartsClient`, `IPurchaseHistoryClient`, `IConsumedMaterialsClient`, `ICatalogManufactureSource`, `ICatalogResilienceService`, `TimeProvider`, `IOptions<DataSourceOptions>`, `CatalogCacheStore`, `ILogger<CatalogHistoryRefreshService>`
  - Note: `ICatalogManufactureSource` is also used by `CatalogStockRefreshService` (for `RefreshManufacturedData`/`RefreshPlannedData`) — it is a shared cross-module source injected into both classes, not moved exclusively into one.

- **`CatalogStockRefreshService`** — ERP/eshop stock levels, in-transport/reserve/quarantine, manufactured/planned inventory, ordered quantities.
  - Methods: `RefreshErpStockData`, `RefreshEshopStockData`, `RefreshTransportData`, `RefreshReserveData`, `RefreshOrderedData`, `RefreshManufacturedData`, `RefreshPlannedData`
  - Dependencies: `IErpStockClient`, `IEshopStockClient`, `ICatalogTransportSource`, `ICatalogPurchaseSource`, `ICatalogManufactureSource`, `ICatalogResilienceService`, `CatalogCacheStore`, `ILogger<CatalogStockRefreshService>`
  - Note: this class has no need for `TimeProvider` or `IOptions<DataSourceOptions>` — none of its methods use them today; do not add them speculatively.

- **`CatalogMetaRefreshService`** — product attributes, lots, prices (eshop + ERP), eshop URLs.
  - Methods: `RefreshAttributesData`, `RefreshLotsData`, `RefreshEshopPricesData`, `RefreshErpPricesData`, `RefreshEshopUrlData`
  - Dependencies: `ICatalogAttributesClient`, `ILotsClient`, `IProductPriceEshopClient`, `IProductPriceErpClient`, `IProductEshopUrlClient`, `ICatalogResilienceService`, `CatalogCacheStore`, `ILogger<CatalogMetaRefreshService>`

- **`CatalogReferenceRefreshService`** — stock taking, manufacture difficulty settings, and the manufacture-cost cross-reference pass.
  - Methods: `RefreshStockTakingData`, `RefreshManufactureDifficultySettingsData`, `RefreshManufactureCostData`
  - Dependencies: `IStockTakingRepository`, `IManufactureDifficultyRepository`, `TimeProvider`, `CatalogCacheStore`, `ILogger<CatalogReferenceRefreshService>`

**Acceptance criteria:**
- Every one of the 18 (+2 helper/indirect) methods on the original `CatalogDataRefreshService` exists, with an identical signature and identical body (including comments), on exactly one of the four new classes.
- No new class takes more than 10 constructor parameters (verify by counting).
- `ICatalogManufactureSource` is injected into both `CatalogHistoryRefreshService` and `CatalogStockRefreshService` — this is an accepted exception to "each dependency belongs to exactly one class" because the source itself is legitimately shared by two unrelated read shapes (`GetManufactureHistoryAsync` vs. `GetManufacturedInventoryAsync`/`GetPlannedQuantitiesAsync`); do not attempt to split `ICatalogManufactureSource` itself in this change.
- `CatalogDataRefreshService` is deleted.

### FR-2: Update `CatalogRepository` to delegate to the four new services
`CatalogRepository` currently holds one `CatalogDataRefreshService _refreshService` field and forwards 19 `Refresh*` calls to it 1:1. Replace this with four fields (`CatalogHistoryRefreshService`, `CatalogStockRefreshService`, `CatalogMetaRefreshService`, `CatalogReferenceRefreshService`), each injected via constructor, and route each existing delegate method (lines 124–143 of `CatalogRepository.cs`) to whichever new service now owns that method. `RefreshMarginData` (implemented directly in `CatalogRepository`, not delegated) is unaffected.

**Acceptance criteria:**
- `CatalogRepository`'s public surface (the `ICatalogRepository` interface it implements) is unchanged — same method names, same signatures.
- Each `Refresh*Data(...)` one-line delegate in `CatalogRepository` now calls the correct one of the four new services; no delegate method's behavior changes.
- `CatalogRepository`'s constructor grows from 9 parameters to 12 (add 4 new services, remove 1 old one that's replaced).

### FR-3: Update DI registration in `CatalogModule`
In `CatalogModule.AddCatalogModule`, replace `services.AddTransient<CatalogDataRefreshService>();` with four `AddTransient` registrations, one per new class. `RegisterBackgroundRefreshTasks` is unaffected — it already wires tasks against `ICatalogRepository`, which is the level of indirection that absorbs this internal restructuring; **no changes to `RegisterBackgroundRefreshTasks` are required or expected**, contrary to a literal reading of the issue's suggested fix ("the existing `CatalogModule.RegisterBackgroundRefreshTasks` method... routes to whichever class owns each task" — in fact it is `CatalogRepository`, one layer below `RegisterBackgroundRefreshTasks`, that does this routing; `RegisterBackgroundRefreshTasks` itself only ever knew about `ICatalogRepository` and needs no edit).

**Acceptance criteria:**
- `CatalogModule.cs` registers all four new services as `Transient` (matching the original service's lifetime) and no longer registers `CatalogDataRefreshService`.
- `CatalogModule.RegisterBackgroundRefreshTasks` is textually unchanged (diff shows zero lines changed in that method).

### FR-4: Update unit tests to match the new class boundaries
`CatalogDataRefreshServiceTests.cs` (495 lines, 22-parameter `CreateService` helper) exercises all `Refresh*` methods against one instance. Split its test cases across four new test files — `CatalogHistoryRefreshServiceTests.cs`, `CatalogStockRefreshServiceTests.cs`, `CatalogMetaRefreshServiceTests.cs`, `CatalogReferenceRefreshServiceTests.cs` — mirroring the new class boundaries, each with its own minimal `CreateService`-style helper scoped to that class's actual dependencies. Delete the original test file once every test case has a new home. Do not lose any existing assertion or test case; do not add new test cases beyond what's needed to preserve 1:1 coverage.

**Acceptance criteria:**
- Every `[Fact]`/`[Theory]` in the original `CatalogDataRefreshServiceTests.cs` has an equivalent in exactly one of the four new test files, unchanged in assertions.
- No new test file's `CreateService` helper takes more than 10 parameters.
- `CatalogDataRefreshServiceTests.cs` no longer exists after the change.
- `dotnet test` passes for the full `Anela.Heblo.Tests` project.

## Non-Functional Requirements

### NFR-1: No behavioral change
This is a pure structural refactor. Every method's implementation (fetch logic, caching calls, error handling, logging, comments) must be copied verbatim into its new home — no logic changes, no reordering of operations, no changes to exception handling or log messages. The background refresh schedule, task names (derived from `ICatalogRepository` method names via `nameof(...)` in `RegisterRefreshTask`), tiering, and cache semantics are all unaffected because the seam being changed (`CatalogDataRefreshService` → 4 classes) sits entirely below `ICatalogRepository`, which is the level `RegisterBackgroundRefreshTasks` and all task scheduling operate at.

### NFR-2: Testability
Each new class's dependency list must be independently mockable without needing to construct unrelated collaborators (e.g. testing `RefreshStockTakingData` must no longer require mocking `ICatalogSalesClient`).

### NFR-3: Backward compatibility
No public contract outside `Anela.Heblo.Application.Features.Catalog.Infrastructure` changes. `ICatalogRepository` and `CatalogRepository`'s public methods are unchanged. No `Anela.Heblo.Domain`, `Anela.Heblo.Persistence`, or `Anela.Heblo.Api` code references `CatalogDataRefreshService` directly (confirmed by search — only `CatalogRepository.cs`, `CatalogModule.cs`, and the one test file reference it), so no other module or the frontend is affected.

## Data Model
No data model changes. `CatalogCacheStore`'s public surface (`SetSalesData`, `SetSetPartsData`, `SetErpStockData`, etc.) is unchanged and continues to be injected into whichever of the four new classes calls each setter.

## API / Interface Design
No public API changes (no controller, no MediatR request/response, no OpenAPI contract is touched). This is an internal application-layer refactor.

New internal class shapes (illustrative — exact bodies are copied verbatim from the existing methods, not redesigned):

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
        ILogger<CatalogHistoryRefreshService> logger) { ... }

    public Task RefreshSalesData(CancellationToken ct);
    public Task RefreshSetPartsData(CancellationToken ct);
    public Task RefreshPurchaseHistoryData(CancellationToken ct);
    public Task RefreshConsumedHistoryData(CancellationToken ct);
    public Task RefreshManufactureHistoryData(CancellationToken ct);
}

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
        ILogger<CatalogStockRefreshService> logger) { ... }

    public Task RefreshErpStockData(CancellationToken ct);
    public Task RefreshEshopStockData(CancellationToken ct);
    public Task RefreshTransportData(CancellationToken ct);
    public Task RefreshReserveData(CancellationToken ct);
    public Task RefreshOrderedData(CancellationToken ct);
    public Task RefreshManufacturedData(CancellationToken ct);
    public Task RefreshPlannedData(CancellationToken ct);
}

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
        ILogger<CatalogMetaRefreshService> logger) { ... }

    public Task RefreshAttributesData(CancellationToken ct);
    public Task RefreshLotsData(CancellationToken ct);
    public Task RefreshEshopPricesData(CancellationToken ct);
    public Task RefreshErpPricesData(CancellationToken ct);
    public Task RefreshEshopUrlData(CancellationToken ct);
}

public sealed class CatalogReferenceRefreshService
{
    public CatalogReferenceRefreshService(
        IStockTakingRepository stockTakingRepository,
        IManufactureDifficultyRepository manufactureDifficultyRepository,
        TimeProvider timeProvider,
        CatalogCacheStore cacheStore,
        ILogger<CatalogReferenceRefreshService> logger) { ... }

    public Task RefreshStockTakingData(CancellationToken ct);
    public Task RefreshManufactureDifficultySettingsData(string? product, CancellationToken ct);
    public Task RefreshManufactureCostData(CancellationToken ct);
}
```

## Dependencies
- No new external libraries or services.
- Depends only on existing types: `CatalogCacheStore`, `ICatalogResilienceService`, `TimeProvider`, `IOptions<DataSourceOptions>`, the 11 external API client interfaces, the 3 cross-module source interfaces, and the 2 repository interfaces — all of which already exist and are unchanged by this work.

## Out of Scope
- Any change to the actual fetch/cache logic inside any `Refresh*` method.
- Any change to `RegisterBackgroundRefreshTasks`, task scheduling, tiering, or task naming.
- Removing or fixing the apparently-dead `RefreshManufactureCostData` wiring gap (it is preserved as-is on `CatalogReferenceRefreshService`).
- Any change to `ICatalogRepository`'s interface or its other (non-refresh) members.
- Renaming or restructuring `CatalogCacheStore`, `ICatalogResilienceService`, or any of the client/source/repository interfaces.
- Splitting `ICatalogManufactureSource` (it remains one interface, injected into two of the four new classes).
- Any frontend, controller, or OpenAPI-surfaced change.

## Open Questions

None.
