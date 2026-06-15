# Specification: Unit Test Coverage for ManufactureBasedMaterialCostProvider

## Summary
Add comprehensive xUnit unit tests for `ManufactureBasedMaterialCostProvider` to raise line coverage from 25% to ≥ 80%, exercising product-type routing, the temporal carry-forward algorithm, future-manufacture backfill, weighted-average aggregation, and purchase-price fallback paths. The tests must mock `ICatalogRepository` and `IMaterialCostCache` and follow the test layout established by `SalesCostProviderTests` (commit `1ac55964`).

## Background
`ManufactureBasedMaterialCostProvider` is the M0 cost source feeding product margin calculations. Its `CalculateMaterialCosts` method routes by `ProductType`, and `CalculateFromManufactureHistory` implements a temporal carry-forward algorithm that silently propagates the last-known weighted-average price across months that have no manufacture record. The current 25% line coverage means a regression in routing, carry-forward, or backfill logic would corrupt historical cost data across all manufactured products without raising an exception — making the file a high-risk hole in the M0 pipeline.

This work mirrors the recently-completed `SalesCostProvider` test harness (PR #3103) and uses the same Moq + xUnit + FluentAssertions stack, the same `[Collection]` strategy to serialize tests that interact with the provider's `static SemaphoreSlim _refreshLock`, and the same `BuildProduct` / `CreateProvider` helper conventions.

## Functional Requirements

### FR-1: Manufactured product types are routed to manufacture-history path
`CalculateMaterialCosts` must route products whose `Type` is `Set`, `Product`, or `SemiProduct` to `CalculateFromManufactureHistory`, returning weighted-average monthly costs derived from `ManufactureHistory`.

**Acceptance criteria:**
- A product with `Type = Product` and a single manufacture record at price `100m` produces a `MonthlyCost` series whose `Cost` values equal `100m`.
- The same assertion holds with `Type = Set` and `Type = SemiProduct`.
- The product's `PurchasePriceWithVat` is **not** used when manufacture history is present (verified by setting `PurchasePriceWithVat = 999m` and asserting the series does not contain `999m`).

### FR-2: Non-manufactured product types fall back to purchase-price path
Products whose `Type` is `Goods`, `Material`, or `UNDEFINED` must be routed to `CalculateFromPurchasePriceWithVat`, returning a constant `PurchasePriceWithVat` for every month in `[dateFrom, dateTo]`, regardless of any `ManufactureHistory` content.

**Acceptance criteria:**
- A product with `Type = Material`, `PurchasePriceWithVat = 50m`, and a populated `ManufactureHistory` produces a series of `MonthlyCost` entries each with `Cost == 50m`.
- The same assertion holds for `Type = Goods` and `Type = UNDEFINED`.
- The returned series count equals the number of whole months in `[dateFrom, dateTo]` inclusive on both endpoints' first-of-month.

### FR-3: Carry-forward fills gap months with last-known price
For manufactured products, months that contain no manufacture record but follow at least one month that does must carry forward the most recent weighted-average price.

**Acceptance criteria:**
- Given manufacture records only in month `M1` at price `120m`, and a range spanning `M1..M4`, the returned series for `M2`, `M3`, `M4` each has `Cost == 120m`.
- Given manufacture records in `M1 = 100m` and `M3 = 200m`, the series produces `M1 = 100m`, `M2 = 100m` (carry-forward), `M3 = 200m`, `M4 = 200m` (carry-forward).
- The carry-forward never skips a month — the returned series has exactly one entry per month in `[dateFrom, dateTo]` after the first known price is set.

### FR-4: Future-manufacture backfill for months preceding any record
For manufactured products, months in `[dateFrom, dateTo]` that precede the earliest manufacture record must be backfilled using the price of that earliest record.

**Acceptance criteria:**
- Given the only manufacture record sits in `M3` at price `300m`, and the range spans `M1..M4`, the series produces `M1 = 300m`, `M2 = 300m`, `M3 = 300m`, `M4 = 300m`.
- Given manufacture records at `M3 = 300m` and `M5 = 500m` with range `M1..M5`, the series produces `M1 = 300m`, `M2 = 300m` (backfill from first future), `M3 = 300m`, `M4 = 300m` (carry-forward), `M5 = 500m`.
- Backfill uses the **earliest** future price, not the latest (verified with two future records of different prices).

### FR-5: Empty manufacture history falls back to purchase price
For a manufactured-type product (`Set`/`Product`/`SemiProduct`) whose `ManufactureHistory` is `null` or empty, `CalculateFromManufactureHistory` must delegate to `CalculateFromPurchasePriceWithVat`, producing the same constant-price series.

**Acceptance criteria:**
- A `Product`-type product with `ManufactureHistory = null` and `PurchasePriceWithVat = 75m` returns a series where every `Cost == 75m`.
- The same product with `ManufactureHistory = new List<...>()` (empty list) returns the same series.
- The series length equals the month count of the date range.

### FR-6: Missing or non-positive purchase price returns empty list
`CalculateFromPurchasePriceWithVat` must return an empty `List<MonthlyCost>` when `PurchasePriceWithVat` is `null`, `0m`, or negative. This applies to both the direct non-manufactured path (FR-2) and the fallback path (FR-5).

**Acceptance criteria:**
- `Type = Material`, `PurchasePriceWithVat = null` → returns an empty list.
- `Type = Material`, `PurchasePriceWithVat = 0m` → returns an empty list.
- `Type = Material`, `PurchasePriceWithVat = -1m` → returns an empty list.
- `Type = Product` with empty `ManufactureHistory` and `PurchasePriceWithVat = null` → returns an empty list (fallback path produces empty).

### FR-7: Weighted-average price is computed per month across grouped records
When multiple manufacture records exist within the same calendar month, the monthly price must equal `Σ(PricePerPiece × Amount) / Σ(Amount)`.

**Acceptance criteria:**
- Two records in month `M1`: `(PricePerPiece=100, Amount=10)` and `(PricePerPiece=200, Amount=30)` → `M1` cost equals `(100*10 + 200*30) / (10 + 30) = 175m`.
- A single record in a month behaves identically (degenerate case) — `PricePerPiece × Amount / Amount == PricePerPiece`.
- Records in different months are not blended — each month's weighted average is independent.

### FR-8: Months with zero total amount are excluded from manufacture aggregation
If a month's manufacture records sum to a total `Amount` of `0`, the weighted-average formula would divide by zero. Test must document and pin the current behavior: the provider currently does not guard against this — confirm whether the test exposes a divide-by-zero or whether the aggregation step never receives such input under realistic conditions.

**Acceptance criteria:**
- A test inputs two records in the same month, both with `Amount = 0`, and asserts the observed behavior. If a `DivideByZeroException` (or NaN for `double`) is raised, the test must `Assert.Throws<DivideByZeroException>` so that future code changes — for example adding a guard — surface a deliberate failure that prompts spec update. (See Open Questions.)

### FR-9: `GetCostsAsync` returns cached data filtered by product codes when cache is hydrated
When the injected `IMaterialCostCache` returns `IsHydrated = true`, `GetCostsAsync` must return the cached `ProductCosts` filtered by the `productCodes` argument; passing `null` or empty `productCodes` returns the full cache.

**Acceptance criteria:**
- Cache hydrated with three product codes; calling `GetCostsAsync(productCodes: ["A"])` returns a dictionary containing only key `"A"`.
- Cache hydrated; calling `GetCostsAsync(productCodes: null)` returns a dictionary with all three keys.
- Cache hydrated; calling `GetCostsAsync(productCodes: [])` returns all entries (matches `SalesCostProvider` semantics).
- Cache hydrated; calling `GetCostsAsync(productCodes: ["MISSING"])` returns an empty dictionary.

### FR-10: `GetCostsAsync` returns empty dictionary and logs a warning when cache is not hydrated
When `IMaterialCostCache.GetCachedDataAsync` returns `IsHydrated = false`, `GetCostsAsync` must return an empty dictionary and emit a `LogLevel.Warning` log containing "not hydrated".

**Acceptance criteria:**
- Cache mock returns `CostCacheData { IsHydrated = false }`; `GetCostsAsync()` returns an empty dictionary.
- Logger verifies exactly one warning entry whose message contains "not hydrated".

### FR-11: `GetCostsAsync` rethrows underlying cache exceptions after logging an error
If `IMaterialCostCache.GetCachedDataAsync` throws, `GetCostsAsync` must log the exception at `LogLevel.Error` and rethrow.

**Acceptance criteria:**
- Cache mock throws `InvalidOperationException("boom")`; `await GetCostsAsync()` re-throws `InvalidOperationException` with the same message.
- Logger verifies one `LogLevel.Error` entry referencing the exception.

### FR-12: `RefreshAsync` computes costs, writes cache, and respects single-flight lock
`RefreshAsync` must call `_catalogRepository.WaitForCurrentMergeAsync`, then `GetAllAsync`, compute per-product costs for all non-empty product codes, and finally write a populated `CostCacheData` via `IMaterialCostCache.SetCachedDataAsync`. A concurrent second call entering while the first holds the lock must short-circuit without invoking `SetCachedDataAsync` a second time.

**Acceptance criteria:**
- A single `RefreshAsync` call results in exactly one `SetCachedDataAsync` invocation, where the supplied `CostCacheData` has `IsHydrated == true`, `ProductCosts.Count` equals the number of products with non-empty `ProductCode`, and `DataFrom`/`DataTo` lie within the configured `ManufactureCostHistoryDays` window.
- Products with `ProductCode == ""` or `null` are excluded from `ProductCosts`.
- When two `RefreshAsync` tasks are awaited concurrently (with the cache write artificially delayed), the second call logs an information entry containing "already in progress" and does **not** invoke `SetCachedDataAsync`.
- If `_catalogRepository.GetAllAsync` throws, `RefreshAsync` logs at `LogLevel.Error`, rethrows the exception, **and** still releases the lock so a subsequent call succeeds.

### FR-13: Test file naming, location, and execution serialization
The test file must follow the project's testing conventions and not interfere with other test classes that share the provider's `static SemaphoreSlim _refreshLock`.

**Acceptance criteria:**
- New file path: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs`.
- Class is decorated with `[Collection("ManufactureBasedMaterialCostProviderTests")]` (mirroring `SalesCostProviderTests`).
- Uses xUnit `[Fact]`/`[Theory]`, FluentAssertions for assertions, and Moq for `IMaterialCostCache`, `ICatalogRepository`, and `ILogger<ManufactureBasedMaterialCostProvider>` doubles.
- Each FR maps to one or more test methods named in the `Method_ExpectedBehavior_WhenCondition` form used elsewhere in the suite.
- `dotnet test --filter "FullyQualifiedName~ManufactureBasedMaterialCostProviderTests"` passes locally and in CI.

## Non-Functional Requirements

### NFR-1: Performance
- Whole suite added by this change must complete in under 5 seconds locally; no test may sleep, hit the filesystem, or open a real database.
- Concurrent-refresh test in FR-12 may use a `TaskCompletionSource` to gate the first refresh — no `Thread.Sleep`.

### NFR-2: Security
- No secrets, no real connection strings, no network calls. Test inputs use synthetic product codes and prices.
- No code under test or test code may log or expose PII.

### NFR-3: Determinism
- Tests must not depend on `DateTime.UtcNow` ambient state. Where the provider uses `DateTime.UtcNow` (in `ComputeAllCostsAsync` / `ComputeCostsAsync`), tests must drive behavior through `RefreshAsync` only when verifying the `DataFrom`/`DataTo` window — and assertions must use tolerant ranges (e.g., "within configured `ManufactureCostHistoryDays` ± 1 day of now") rather than equality on exact dates.
- All other tests must invoke `CalculateMaterialCosts` indirectly through `RefreshAsync` with controlled date ranges, or — if the implementation exposes no seam — must pass a date range driven by fixed-clock inputs constructed inside the test.

### NFR-4: Coverage
- Line coverage for `ManufactureBasedMaterialCostProvider.cs` must rise to ≥ 80% after the new tests land, measured by the same Coverlet configuration used by the existing CI report.
- All branches inside `CalculateMaterialCosts`, `CalculateFromManufactureHistory`, and `CalculateFromPurchasePriceWithVat` must be exercised by at least one test.

## Data Model

The tests construct in-memory `CatalogAggregate` instances; no schema changes.

Key value objects used:
- `CatalogAggregate` — fields exercised: `ProductCode`, `Type` (`ProductType`), `ManufactureHistory` (`IReadOnlyList<CatalogManufactureRecord>` or equivalent — confirm exact property type during implementation), `PurchasePriceWithVat` (`decimal?`).
- `CatalogManufactureRecord` — fields exercised: `Date` (`DateTime`), `PricePerPiece` (`decimal`), `Amount` (`double`).
- `MonthlyCost` — read-only record with `Month` (`DateTime`, always day 1) and `Cost` (`decimal`).
- `ProductType` enum — values used: `Product`, `Set`, `SemiProduct`, `Material`, `Goods`, `UNDEFINED`.
- `CostCacheData` — fields exercised: `ProductCosts` (`Dictionary<string, List<MonthlyCost>>`), `IsHydrated`, `LastUpdated`, `DataFrom`, `DataTo`.
- `DataSourceOptions` — field exercised: `ManufactureCostHistoryDays`.

## API / Interface Design

No production API changes. Test class outline:

```csharp
[Collection("ManufactureBasedMaterialCostProviderTests")]
public sealed class ManufactureBasedMaterialCostProviderTests
{
    private const int DefaultHistoryDays = 365;

    private static CatalogAggregate BuildManufacturedProduct(
        string productCode,
        ProductType type,
        IEnumerable<(DateTime date, decimal pricePerPiece, double amount)> history,
        decimal? purchasePriceWithVat = null) { ... }

    private static CatalogAggregate BuildNonManufacturedProduct(
        string productCode,
        ProductType type,
        decimal? purchasePriceWithVat) { ... }

    private static ManufactureBasedMaterialCostProvider CreateProvider(
        Mock<IMaterialCostCache>? cacheMock = null,
        Mock<ICatalogRepository>? repoMock = null,
        Mock<ILogger<ManufactureBasedMaterialCostProvider>>? loggerMock = null,
        int manufactureCostHistoryDays = DefaultHistoryDays) { ... }

    private static void VerifyLog(
        Mock<ILogger<ManufactureBasedMaterialCostProvider>> logger,
        LogLevel level,
        string messageContains,
        Times? times = null) { ... }

    // FR-1 .. FR-12 test methods
}
```

Each FR maps to ≥ 1 test method:

| FR    | Example test method                                                                                     |
|-------|---------------------------------------------------------------------------------------------------------|
| FR-1  | `RefreshAsync_UsesManufactureHistory_WhenProductTypeIsManufactured(ProductType type)` (Theory)          |
| FR-2  | `RefreshAsync_UsesPurchasePrice_WhenProductTypeIsNonManufactured(ProductType type)` (Theory)            |
| FR-3  | `RefreshAsync_CarriesForwardLastKnownPrice_WhenMonthHasNoManufacture`                                   |
| FR-4  | `RefreshAsync_BackfillsFromEarliestFuturePrice_WhenNoPriorManufactureExists`                            |
| FR-5  | `RefreshAsync_FallsBackToPurchasePrice_WhenManufactureHistoryIsEmpty`                                   |
| FR-6  | `RefreshAsync_ReturnsEmptyCostList_WhenPurchasePriceIsNullOrNonPositive(decimal? price)` (Theory)       |
| FR-7  | `RefreshAsync_AggregatesWithWeightedAverage_WhenMonthHasMultipleManufactureRecords`                     |
| FR-8  | `RefreshAsync_Behavior_WhenMonthlyAmountSumIsZero` (pins observed behavior — see Open Questions)        |
| FR-9  | `GetCostsAsync_ReturnsFilteredCache_WhenHydrated(string[] requestedCodes, string[] expectedKeys)`       |
| FR-10 | `GetCostsAsync_ReturnsEmptyAndLogsWarning_WhenCacheNotHydrated`                                         |
| FR-11 | `GetCostsAsync_LogsAndRethrows_WhenCacheThrows`                                                         |
| FR-12 | `RefreshAsync_PopulatesCache_OnSuccess` + `RefreshAsync_SkipsConcurrentCall_WhenLockHeld` + `RefreshAsync_ReleasesLock_WhenRepositoryThrows` |

## Dependencies

- xUnit (already referenced by `Anela.Heblo.Tests`)
- FluentAssertions (already referenced)
- Moq (already referenced)
- `Microsoft.Extensions.Options` / `Microsoft.Extensions.Logging` (already referenced)
- No new NuGet packages required.

## Out of Scope

- Modifying the production behavior of `ManufactureBasedMaterialCostProvider` (including adding a divide-by-zero guard) — those changes belong in a separate task; FR-8 pins existing behavior only.
- Tests for `IMaterialCostCache` implementations themselves.
- Tests for `CatalogAggregate` construction or `ProductType` enum mapping.
- Integration tests against a real `ICatalogRepository`.
- Updating `docs/architecture/testing-strategy.md` (no new pattern introduced).
- Raising coverage in other files under `Features/Catalog/CostProviders`.

## Open Questions

None.

## Status: COMPLETE