Exploration complete. Writing the architecture review now.

# Architecture Review: Unit Test Coverage for ManufactureBasedMaterialCostProvider

## Skip Design: true

## Architectural Fit Assessment

This is a **test-only feature** that fits cleanly into an established pattern. The codebase already has a direct sibling — `SalesCostProviderTests.cs` (`backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/`) — built on the same xUnit + FluentAssertions + Moq stack, with the same `[Collection]` serialization strategy required because both providers share a `static SemaphoreSlim _refreshLock` lifecycle. No production code changes are needed; no new abstractions; no new dependencies. The architectural decision is essentially "duplicate the SalesCostProviderTests shape and adapt to the manufacture-history domain."

Three integration points were verified against the live source (commit on `feat-coverage-gap-catalog-manufacturebasedmat`):

1. **`ManufactureBasedMaterialCostProvider`** (`backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProvider.cs`) — constructor takes `IMaterialCostCache`, `ICatalogRepository`, `ILogger<...>`, and `IOptions<DataSourceOptions>`. All four are mockable.
2. **`CatalogAggregate`** — `PurchasePriceWithVat` is a **computed read-only property** sourced from `ErpPrice?.PurchasePriceWithVat`. Tests cannot set it directly; they must set `ErpPrice = new ProductPriceErp { PurchasePriceWithVat = ... }` (or leave `ErpPrice = null` for the "null" case).
3. **`ManufactureHistory`** — the property is typed `IReadOnlyList<ManufactureHistoryRecord>` (from `Anela.Heblo.Domain.Features.Manufacture`), **not** `CatalogManufactureRecord` as the spec states.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────────────────────────┐
│ ManufactureBasedMaterialCostProviderTests                                │
│ [Collection("ManufactureBasedMaterialCostProviderTests")]                │
│                                                                          │
│   Helpers                                                                │
│   ├── BuildManufacturedProduct(code, type, history, purchasePrice?)      │
│   ├── BuildNonManufacturedProduct(code, type, purchasePrice?)            │
│   ├── BuildHydratedCacheData(productCodes)                               │
│   ├── CreateProvider(cacheMock?, repoMock?, loggerMock?, historyDays?)   │
│   └── VerifyLog(loggerMock, level, messageContains, times?)              │
│                                                                          │
│   Test methods (one per FR; some Theory-backed)                          │
└────────────────────────┬─────────────────────────────────────────────────┘
                         │ instantiates with mocks
                         ▼
┌──────────────────────────────────────────────────────────────────────────┐
│ ManufactureBasedMaterialCostProvider (SUT)                               │
│  - GetCostsAsync   → IMaterialCostCache.GetCachedDataAsync               │
│  - RefreshAsync    → ICatalogRepository.WaitForCurrentMergeAsync         │
│                    → ICatalogRepository.GetAllAsync                      │
│                    → IMaterialCostCache.SetCachedDataAsync               │
└──────────────────────────────────────────────────────────────────────────┘
                         ▲
                         │ doubles supplied by tests
┌────────────────────────┴─────────────────────────────────────────────────┐
│  Mock<IMaterialCostCache>   Mock<ICatalogRepository>                     │
│  Mock<ILogger<...>>         Options.Create(new DataSourceOptions { ... })│
└──────────────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Drive behavior through `RefreshAsync`, not internal methods
**Options considered:** (a) expose `CalculateMaterialCosts` via `InternalsVisibleTo` and call it directly with controlled `dateFrom`/`dateTo`; (b) drive everything through `RefreshAsync` and capture the `CostCacheData` written to `SetCachedDataAsync`.
**Chosen approach:** Option (b).
**Rationale:** Option (a) would require modifying production code (an `InternalsVisibleTo` attribute or visibility change), violating the "no production behavior changes" out-of-scope rule. `SalesCostProviderTests` already proves the capture-via-mock pattern works for asserting per-month cost values. The provider always uses **all months between `now - ManufactureCostHistoryDays` and `now`**, so by injecting a `ManufactureCostHistoryDays` large enough to cover any synthetic date the test seeds (e.g. 4000 days = ~11 years), the test can place `ManufactureHistoryRecord`s at arbitrary fixed dates in the recent past and the `CalculateFromManufactureHistory` algorithm will visit them deterministically.

#### Decision 2: Use fixed-anchor dates relative to `DateTime.UtcNow` for in-window placement
**Options considered:** (a) build test dates from absolute literals like `new DateTime(2026, 1, 1)`; (b) anchor every test date to the start of "this month" / "last month" via `DateTime.UtcNow`.
**Chosen approach:** Option (b), mirroring `SalesCostProviderTests` line 99–100.
**Rationale:** The provider's window is `[utcNow - historyDays, utcNow]`. Absolute dates risk falling outside the window when the clock advances. Anchoring relative to `DateTime.UtcNow` (e.g. `new DateTime(now.Year, now.Month, 1).AddMonths(-3)` for "M1") keeps tests stable indefinitely, matches the existing test-suite style, and lets each FR control gap/carry-forward/backfill semantics by spacing manufacture records months apart from a known anchor.

#### Decision 3: Test the divide-by-zero behavior (FR-8) by asserting the actual runtime outcome
**Options considered:** (a) pin `Assert.Throws<DivideByZeroException>`; (b) inspect the produced `MonthlyCost.Cost` for a degenerate value.
**Chosen approach:** Option (a) — assert `DivideByZeroException` is raised.
**Rationale:** The provider uses `decimal` arithmetic (`g.Sum(m => m.PricePerPiece * (decimal)m.Amount) / (decimal)g.Sum(m => m.Amount)`). `decimal / 0m` throws `DivideByZeroException` (it does not produce `NaN` — that's a `double`-only behavior). The exception will propagate out of `ComputeAllCostsAsync` → `RefreshAsync`, which already logs at Error and rethrows. The test must therefore `await Assert.ThrowsAsync<DivideByZeroException>(() => provider.RefreshAsync())` — **not** `Assert.Throws`. This pins observed behavior; any future guard added in production code will deliberately break this test and force a spec amendment.

#### Decision 4: Set `PurchasePriceWithVat` via `ErpPrice`, not direct assignment
**Options considered:** none — the property is read-only.
**Chosen approach:** All product builders must set `ErpPrice = new ProductPriceErp { PurchasePriceWithVat = value }` to drive the purchase-price path. The "`PurchasePriceWithVat == null`" scenario from FR-6 is realised by leaving `ErpPrice = null`.
**Rationale:** The spec's "`PurchasePriceWithVat = null`" / "`= 0m`" / "`= -1m`" phrasing implies a setter that does not exist. The test helpers must encapsulate this so individual tests stay readable.

## Implementation Guidance

### Directory / Module Structure

Single new file:

```
backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/
    └── ManufactureBasedMaterialCostProviderTests.cs   (NEW)
```

No changes to other files. No new test project, no new `[Collection]` definition file (xUnit auto-discovers collections from `[Collection("name")]` attributes when the collection has no fixture).

### Interfaces and Contracts

Helpers must use these signatures so each FR test stays a one-liner setup:

```csharp
private static CatalogAggregate BuildManufacturedProduct(
    string productCode,
    ProductType type,
    IEnumerable<(DateTime date, decimal pricePerPiece, double amount)>? history = null,
    decimal? purchasePriceWithVat = null);
// - history == null  → ManufactureHistory left at default empty list
// - history.Empty()  → ManufactureHistory assigned new empty list
// - purchasePriceWithVat == null → ErpPrice left null
// - purchasePriceWithVat != null → ErpPrice = new ProductPriceErp { PurchasePriceWithVat = value.Value }

private static CatalogAggregate BuildNonManufacturedProduct(
    string productCode,
    ProductType type,
    decimal? purchasePriceWithVat);
// Same ErpPrice semantics; ManufactureHistory irrelevant (untouched).

private static ManufactureBasedMaterialCostProvider CreateProvider(
    Mock<IMaterialCostCache>? cacheMock = null,
    Mock<ICatalogRepository>? repoMock = null,
    Mock<ILogger<ManufactureBasedMaterialCostProvider>>? loggerMock = null,
    int manufactureCostHistoryDays = DefaultHistoryDays);

private static CostCacheData BuildHydratedCacheData(IEnumerable<string> productCodes);

private static void VerifyLog(
    Mock<ILogger<ManufactureBasedMaterialCostProvider>> logger,
    LogLevel level,
    string messageContains,
    Times? times = null);
```

The `VerifyLog` body must use the same lambda shape as `SalesCostProviderTests.VerifyLog` (lines 61–75) so the `ILogger` extension-method indirection is intercepted correctly.

`DefaultHistoryDays` should be set to a value that comfortably covers all test scenarios (e.g. **`4000`** ≈ 11 years). This lets tests place manufacture records 1–6 months back without worrying about month-window boundary effects, and it eliminates flakiness when CI runs near a month boundary.

### Data Flow

For an FR-3 carry-forward test, the data flow is:

1. Build `CatalogAggregate` with `Type = Product`, `ManufactureHistory = [{ Date: M1, PricePerPiece: 100m, Amount: 1.0 }, { Date: M3, PricePerPiece: 200m, Amount: 1.0 }]` (M1, M3 are first-of-month anchors derived from `DateTime.UtcNow`).
2. `repoMock.GetAllAsync` returns `[product]`; `repoMock.WaitForCurrentMergeAsync` returns `Task.CompletedTask`.
3. `cacheMock.SetCachedDataAsync` callback captures the `CostCacheData` into a local variable.
4. Call `await provider.RefreshAsync()`.
5. Assert: `captured.ProductCosts["PROD-A"]` contains a `MonthlyCost` for every month in the SUT's window. Filter to `[M1, M2, M3, M4]` by month-anchor equality, then assert `[100m, 100m, 200m, 200m]`.

For FR-9..FR-11, the flow goes through `GetCostsAsync` instead — `cacheMock.GetCachedDataAsync` returns a pre-built `CostCacheData` (or throws), and `repoMock`/repository methods must never be invoked (assert with `repoMock.VerifyNoOtherCalls()` per the FR-10 baseline in `SalesCostProviderTests` line 306).

For FR-12 concurrent-lock, use the exact `TaskCompletionSource<...>` gating pattern from `SalesCostProviderTests` lines 378–432: gate one of the dependencies (here, `GetAllAsync`), spin-wait until the gated invocation registers, then issue the second `RefreshAsync` and assert the "already in progress" log + zero `SetCachedDataAsync` calls before releasing the gate.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `static SemaphoreSlim _refreshLock` leaks between tests when xUnit parallelises | High | `[Collection("ManufactureBasedMaterialCostProviderTests")]` (distinct from the Sales collection) serialises tests within this class. Every lock-touching test must release via the production `finally` block — verified by an explicit "subsequent refresh succeeds" assertion in FR-12 exception-path tests. |
| Test seeds `ManufactureHistoryRecord`s outside the SUT's `[now - historyDays, now]` window, silently producing different cost series | High | Use `DefaultHistoryDays = 4000` and anchor every record to `new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(-N)`. Never use absolute date literals. |
| Spec uses `CatalogManufactureRecord`, but production type is `ManufactureHistoryRecord` | Medium | Spec amendment listed below. Test code must use `ManufactureHistoryRecord` from `Anela.Heblo.Domain.Features.Manufacture`. |
| FR-6 "`PurchasePriceWithVat = null`" implies a settable property that doesn't exist | Medium | Helpers encapsulate the `ErpPrice` setup; null = leave `ErpPrice` unset. Documented above under Decision 4. |
| FR-8 — spec says "DivideByZeroException **or** NaN for `double`"; reality is decimal-only | Medium | Decision 3 above — pin `DivideByZeroException` only. Remove the NaN branch from the test. |
| FR-12 concurrent-lock test races on `Invocations` inspection | Medium | Re-use `SalesCostProviderTests` line 404–407 pattern: spin on `ledgerMock.Invocations.Any(...)` with `await Task.Yield()`. Here, spin on `repoMock.Invocations` looking for `GetAllAsync`. |
| Coverage tooling (Coverlet) excludes private methods in some configs | Low | Driving everything through public `RefreshAsync`/`GetCostsAsync` keeps every private branch covered. No exclusion attributes needed. |

## Specification Amendments

The following corrections to `spec.r1.md` must be applied before or during implementation. None alter intent — they reconcile spec wording with the actual codebase.

1. **§ Data Model** — replace `CatalogManufactureRecord` with `ManufactureHistoryRecord` (namespace `Anela.Heblo.Domain.Features.Manufacture`). Its fields are `Date` (`DateTime`), `Amount` (`double`), `PricePerPiece` (`decimal`).
2. **FR-1, FR-5, FR-6** — clarify that `PurchasePriceWithVat` is **read-only** on `CatalogAggregate`. Set it via `ErpPrice = new ProductPriceErp { PurchasePriceWithVat = value }`. The "`null`" case is realised by leaving `ErpPrice = null` (not by assigning `null` to `PurchasePriceWithVat`).
3. **FR-8** — drop "(or NaN for `double`)". The SUT uses `decimal` arithmetic, so the only possible outcome is `DivideByZeroException`. Test must use `Assert.ThrowsAsync<DivideByZeroException>` (routed through `RefreshAsync`, since the exception propagates).
4. **FR-10** — substring to verify is "`MaterialCostCache not hydrated`" (the production log emits `"MaterialCostCache not hydrated yet"`). Any substring of that string works; "not hydrated" is fine but pin it to "MaterialCostCache not hydrated" for safety against unrelated provider naming changes.
5. **FR-11** — error log substring is "`Error getting material costs`".
6. **FR-12** — concurrent-skip log substring is "`refresh already in progress`". Repository-throws log substring is "`Failed to refresh MaterialCostCache`".
7. **NFR-3** — note that `DefaultHistoryDays` for the test helper should be **~4000** (not 365 as in the spec's outline), to guarantee any synthetic month-anchored record falls inside the SUT's window regardless of when CI runs.
8. **§ API / Interface Design test outline** — change `DefaultHistoryDays` from `365` to `4000`; remove `ILedgerService` (not a dependency of `ManufactureBasedMaterialCostProvider`); add the `BuildHydratedCacheData` helper that already exists in `SalesCostProviderTests`.

## Prerequisites

None. All NuGet packages, project references, and test infrastructure already exist:

- `Anela.Heblo.Tests` already references `Anela.Heblo.Application` (where the SUT lives), xUnit, FluentAssertions, Moq, and `Microsoft.Extensions.*`.
- `Features/Catalog/CostProviders/` directory already exists in the test project (it contains `SalesCostProviderTests.cs`).
- No CI configuration change required — Coverlet picks up the new file automatically.
- No documentation update required (no new pattern introduced; matches `docs/architecture/testing-strategy.md`).

Implementation can begin immediately upon spec amendments §1–§8 being acknowledged.