# Specification: Unit Test Coverage for SalesCostProvider

## Summary
Add comprehensive xUnit test coverage for `SalesCostProvider` (the M2 cost provider), which is currently at 0% line coverage despite being responsible for distributing warehouse and marketing costs across products for margin analysis. The test suite exercises the cost-allocation formula, zero-sales guard, date-range math, product-code filtering, cache-not-hydrated fallback, refresh concurrency lock, and exception propagation to push coverage above the 60% CI threshold (target ≥ 80%) and prevent silent regressions in cost data. No production code changes — pure additive test coverage.

## Background
`SalesCostProvider` lives at `backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/SalesCostProvider.cs` and computes M2 (sales/marketing overhead) costs by:
1. Loading direct costs from cost centers `SKLAD` (warehouse) and `MARKETING` via `ILedgerService`.
2. Summing sold pieces across all products' `SalesHistory` over a rolling window driven by `DataSourceOptions.ManufactureCostHistoryDays`.
3. Allocating `totalCost / totalSoldPieces` flatly to every product for every month in the range.
4. Caching the result in `ISalesCostCache`; `GetCostsAsync` falls back to an empty dictionary with a warning when the cache is not yet hydrated.

The weekly coverage-gap routine (CI run #27416879267, commit `3a6b7f99`) flagged the provider at 0% line coverage. Because the provider feeds the M2 component of product margin analysis, a silent regression — for instance, an inverted zero-sales guard, a wrong cost-center constant, or a leap-year date-range bug — would produce wrong cost figures for every product without raising an error. This spec defines the unit-test surface required to lock in current behaviour.

## Functional Requirements

### FR-1: Test the nominal cost-allocation flow
Verify that `RefreshAsync` (which drives the private `ComputeAllCostsAsync`) correctly computes cost-per-piece and writes the allocation into the cache.

**Acceptance criteria:**
- With 3 mocked `CatalogAggregate` products whose combined `SalesHistory` sums to a known `totalSoldPieces` and mocked `ILedgerService.GetDirectCosts` returning known warehouse + marketing costs, the dictionary passed to `ISalesCostCache.SetCachedDataAsync` contains one entry per product code.
- Each product's `MonthlyCost.Cost` equals `(decimal)((warehouseSum + marketingSum) / totalSoldPieces)` for every month in the generated range.
- Each product's monthly list has exactly the same length as the generated month range.
- `ILedgerService.GetDirectCosts` is called exactly twice — once with `WarehouseCostCenter = "SKLAD"` and once with `MarketingCostCenter = "MARKETING"`.
- `ICatalogRepository.WaitForCurrentMergeAsync` is awaited before `GetAllAsync` is called (verified by ordered mock setup or by asserting call order).

### FR-2: Test the zero sold-pieces guard
Verify the fallback path when no sales exist in the period.

**Acceptance criteria:**
- When all products' `SalesHistory` sums to `0` within the date window, `ISalesCostCache.SetCachedDataAsync` receives a `CostCacheData` whose `ProductCosts` contains one entry per product with all `MonthlyCost.Cost` values equal to `0m`.
- A warning is logged via `ILogger<SalesCostProvider>` with a message containing "No sales history found".
- `IsHydrated` on the cached data is `true`.
- Products with `null` or empty `ProductCode` are excluded from the resulting dictionary.

### FR-3: Test product-code filtering in `GetCostsAsync`
Verify `FilterByProductCodes` behaviour through the public API.

**Acceptance criteria:**
- When the cache is hydrated and `productCodes` is `null`, `GetCostsAsync` returns the full dictionary.
- When `productCodes` is an empty list, the full dictionary is returned (treated the same as `null`).
- When `productCodes` contains a subset of product codes, only those keys appear in the returned dictionary.
- When `productCodes` contains codes that do not exist in the cache, those codes are silently skipped (no exception).
- The original cache dictionary is not mutated by the filter.

### FR-4: Test the cache-not-hydrated fallback
Verify behaviour when `ISalesCostCache.GetCachedDataAsync` returns data with `IsHydrated = false`.

**Acceptance criteria:**
- `GetCostsAsync` returns an empty `Dictionary<string, List<MonthlyCost>>` (not null).
- A warning is logged with message containing "SalesCostCache not hydrated".
- No call is made to `ICatalogRepository` or `ILedgerService` from `GetCostsAsync`.

### FR-5: Test date-range computation including leap-year edge case
Verify `GetDateRange` math through observable effects on `ILedgerService.GetDirectCosts` arguments. Match the existing cost-provider test pattern (see `FlatManufactureCostProviderTests.cs:32-36`) — assert end-of-month behaviour against `DateTime.UtcNow` by capturing the `costsFrom`/`costsTo` arguments passed to the mocked `ILedgerService.GetDirectCosts` and verifying month/day/hour/minute/second relative to "now". No `TimeProvider`/`IClock` refactor in this PR.

**Acceptance criteria:**
- With `DataSourceOptions.ManufactureCostHistoryDays` set to a known value, the `costsFrom` argument captured from `ILedgerService.GetDirectCosts` equals the first day of the month derived from `DateTime.UtcNow - ManufactureCostHistoryDays` (`costsFrom.Day == 1`).
- The `costsTo` argument satisfies `costsTo.Day == DateTime.DaysInMonth(costsTo.Year, costsTo.Month)` AND `costsTo.Hour == 23 && costsTo.Minute == 59 && costsTo.Second == 59`.
- A separate `[Theory]` over fixed `(year, month, expectedDays)` tuples — including `(2024, 2, 29)`, `(2023, 2, 28)`, `(2024, 4, 30)`, `(2024, 12, 31)` — asserts `DateTime.DaysInMonth(year, month) == expectedDays` to lock in the calendar math used by `GetDateRange` without requiring clock injection.

### FR-6: Test `RefreshAsync` concurrency lock
Verify the static `RefreshLock` skips overlapping refreshes. Because the lock is `static`, the test class MUST apply `[Collection("SalesCostProviderTests")]` to serialize tests within the class and prevent cross-test interference — matching the `[Collection("FlatManufactureCostProviderTests")]` pattern on `FlatManufactureCostProviderTests.cs:21`.

**Acceptance criteria:**
- When `RefreshAsync` is invoked concurrently (second call started while the first is in-flight via a `TaskCompletionSource`-gated mock on `ILedgerService.GetDirectCosts`), the second invocation returns without calling `ISalesCostCache.SetCachedDataAsync`.
- An informational log entry containing "refresh already in progress" is emitted for the skipped call.
- After the first call completes and the lock is released, a subsequent `RefreshAsync` call proceeds normally (calls `SetCachedDataAsync`).

### FR-7: Test exception propagation
Verify `GetCostsAsync` and `RefreshAsync` surface failures cleanly.

**Acceptance criteria:**
- When `ISalesCostCache.GetCachedDataAsync` throws, `GetCostsAsync` logs an error with message containing "Error getting sales costs" and rethrows the original exception.
- When `ILedgerService.GetDirectCosts` throws inside `RefreshAsync`, the lock is released (a subsequent `RefreshAsync` call proceeds rather than skipping with "already in progress"), an error is logged with message containing "Failed to refresh SalesCostCache", and the original exception is rethrown.
- When `ICatalogRepository.GetAllAsync` throws, the same release-then-rethrow behaviour holds.

## Non-Functional Requirements

### NFR-1: Performance
- Test suite for `SalesCostProvider` MUST complete in under 5 seconds locally and under 10 seconds in CI.
- No real I/O: all collaborators (`ISalesCostCache`, `ICatalogRepository`, `ILedgerService`, `IOptions<DataSourceOptions>`, `ILogger<SalesCostProvider>`) are mocked.

### NFR-2: Coverage and quality
- Line coverage for `SalesCostProvider.cs` MUST be ≥ 80% after the new tests merge (current 0%; CI threshold is 60% — the spec targets the global 80% standard from `~/.claude/rules/testing.md`).
- Tests follow Arrange-Act-Assert structure with descriptive names matching the `Method_Returns/Throws/Does_When_Condition` convention used elsewhere in this codebase.
- Use **xUnit** + **FluentAssertions 6.12.0** + **Moq 4.20.72** — all already referenced in `Anela.Heblo.Tests.csproj`. Do NOT use NSubstitute for these tests even though the package is referenced; the cost-provider area is uniformly Moq (see `FlatManufactureCostProviderTests.cs:12,38`).
- No flakiness: no `Thread.Sleep`, no real timers, no wall-clock reads beyond what the implementation requires. Concurrent-execution tests (FR-6) use `TaskCompletionSource`-gated mocks rather than sleeps.

### NFR-3: Maintainability
- Reusable test data (sample `CatalogAggregate` instances, `LedgerEntry` mocks) lives in private builder helpers within the test class or a shared `*TestBuilders.cs` file under the same test folder, not duplicated per test.
- Test class name: `SalesCostProviderTests`.
- File path: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/SalesCostProviderTests.cs`.
- Namespace: `Anela.Heblo.Tests.Features.Catalog.CostProviders`.
- Apply `[Collection("SalesCostProviderTests")]` on the class to serialize execution against the static `RefreshLock` (see FR-6).

## Data Model
No new persisted entities. The tests exercise these existing types:
- `CatalogAggregate` — product entity with `ProductCode` and `SalesHistory` (collection containing `Date` and `AmountTotal`).
- `MonthlyCost` — value object `(DateTime Month, decimal Cost)`.
- `CostCacheData` — `{ ProductCosts, LastUpdated, DataFrom, DataTo, IsHydrated }`.
- `DataSourceOptions` — bound config, `ManufactureCostHistoryDays` field consumed by `GetDateRange`.
- Ledger cost rows returned by `ILedgerService.GetDirectCosts` (with a `Cost` property summed via `.Sum(c => c.Cost)`).

Test fixtures construct minimal instances of these types; no schema changes are introduced.

## API / Interface Design
No public API surface changes. Tests interact with:
- `SalesCostProvider.GetCostsAsync(productCodes?, dateFrom?, dateTo?, ct)` — public entry point.
- `SalesCostProvider.RefreshAsync(ct)` — public refresh entry point.

Mocks set up:
- `ISalesCostCache.GetCachedDataAsync(ct)` → returns a `CostCacheData` (hydrated or not, as required per test).
- `ISalesCostCache.SetCachedDataAsync(data, ct)` → captures the argument for assertion.
- `ICatalogRepository.WaitForCurrentMergeAsync(ct)` → returns `Task.CompletedTask`.
- `ICatalogRepository.GetAllAsync(ct)` → returns a list of `CatalogAggregate` test fixtures.
- `ILedgerService.GetDirectCosts(from, to, costCenter, ct)` → returns a list of cost rows, with a verifiable invocation per cost center; `It.IsAny<DateTime>()` argument captures used for FR-5 date assertions.
- `IOptions<DataSourceOptions>` → returns a `DataSourceOptions` with deterministic `ManufactureCostHistoryDays`.
- `ILogger<SalesCostProvider>` → verified via `Verify` on `Log<It.IsAnyType>` for warning/error/info messages.

## Dependencies
- xUnit (already referenced).
- FluentAssertions 6.12.0 (`Anela.Heblo.Tests.csproj:25`).
- Moq 4.20.72 (`Anela.Heblo.Tests.csproj:24`).
- Target project: `Anela.Heblo.Tests` (`backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`) — already references `Anela.Heblo.Application` (line 48).
- No new NuGet packages required.

## Out of Scope
- Refactoring `SalesCostProvider` itself (no behaviour changes). If a test reveals a latent bug, file a separate issue with a failing test; do not change production code in this PR.
- Introducing a clock abstraction (`TimeProvider`/`IClock`) into `SalesCostProvider`. All four cost providers (`SalesCostProvider`, `FlatManufactureCostProvider`, `DirectManufactureCostProvider`, `ManufactureBasedMaterialCostProvider`) read `DateTime.UtcNow` directly; a `TimeProvider` refactor belongs in its own dedicated PR that touches all of them together.
- Integration tests against a real database, real `LedgerService`, or real cache implementation.
- Coverage for other cost providers (`PurchaseCostProvider`, `ManufactureCostProvider`, etc.) — separate gaps, separate spec.
- Tests for `ISalesCostCache` or `ILedgerService` implementations.
- Property-based testing.
- Switching the cost-provider test area from Moq to NSubstitute.

## Open Questions
None.

## Status: COMPLETE