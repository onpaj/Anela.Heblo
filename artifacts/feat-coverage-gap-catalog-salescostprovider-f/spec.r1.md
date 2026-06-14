# Specification: Unit Test Coverage for SalesCostProvider

## Summary
Add comprehensive xUnit test coverage for `SalesCostProvider` (M2 cost provider), which is currently at 0% line coverage despite being responsible for distributing warehouse and marketing costs across products for margin analysis. The test suite must exercise the cost-allocation formula, zero-sales guard, date-range math, product-code filtering, and cache-not-hydrated fallback to push coverage above the 60% CI threshold and prevent silent regressions in cost data.

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
Verify `GetDateRange` math through observable effects on `ILedgerService.GetDirectCosts` arguments.

**Acceptance criteria:**
- With `DataSourceOptions.ManufactureCostHistoryDays` set to a known value, the `costsFrom` argument to `ILedgerService.GetDirectCosts` equals the first day of the month derived from `UtcNow - ManufactureCostHistoryDays`.
- The `costsTo` argument is the last day of the current UTC month at `23:59:59` (verified via `DateTime.DaysInMonth`).
- A leap-year scenario (running against February of a leap year, e.g. by exercising the helper logic with a date-fixture/clock abstraction or by asserting on `DateTime.DaysInMonth(2024, 2) == 29`) confirms `costsTo.Day == 29`.
- If introducing the leap-year test requires injecting a clock and the provider does not currently support that, the test SHOULD assert the deterministic portion (month start, end-of-month seconds) and a separate test SHOULD assert `DateTime.DaysInMonth` semantics in a dedicated helper test. See Open Questions.

### FR-6: Test `RefreshAsync` concurrency lock
Verify the static `RefreshLock` skips overlapping refreshes.

**Acceptance criteria:**
- When `RefreshAsync` is invoked concurrently (second call started while the first is in-flight via a `TaskCompletionSource`-gated mock), the second invocation returns without calling `ISalesCostCache.SetCachedDataAsync`.
- An informational log entry "refresh already in progress" is emitted for the skipped call.
- After the first call completes and the lock is released, a subsequent `RefreshAsync` call proceeds normally.

### FR-7: Test exception propagation
Verify `GetCostsAsync` and `RefreshAsync` surface failures cleanly.

**Acceptance criteria:**
- When `ISalesCostCache.GetCachedDataAsync` throws, `GetCostsAsync` logs an error with message "Error getting sales costs" and rethrows the original exception.
- When `ILedgerService.GetDirectCosts` throws inside `RefreshAsync`, the lock is released (a subsequent `RefreshAsync` call proceeds rather than skipping with "already in progress"), an error is logged with "Failed to refresh SalesCostCache", and the original exception is rethrown.
- When `ICatalogRepository.GetAllAsync` throws, the same release-then-rethrow behaviour holds.

## Non-Functional Requirements

### NFR-1: Performance
- Test suite for `SalesCostProvider` MUST complete in under 5 seconds locally and under 10 seconds in CI.
- No real I/O: all collaborators (`ISalesCostCache`, `ICatalogRepository`, `ILedgerService`, `IOptions<DataSourceOptions>`, `ILogger<SalesCostProvider>`) are mocked.

### NFR-2: Coverage and quality
- Line coverage for `SalesCostProvider.cs` MUST be ≥ 80% after the new tests merge (current 0%; CI threshold is 60% — the spec targets the global 80% standard from `~/.claude/rules/testing.md`).
- Tests follow Arrange-Act-Assert structure with descriptive names matching the `Method_Returns/Throws/Does_When_Condition` convention used elsewhere in this codebase.
- Use **xUnit** + **FluentAssertions** + **Moq** (or **NSubstitute** if already standard in this project — verify against an adjacent test project before authoring).
- No flakiness: no `Thread.Sleep`, no real timers, no wall-clock reads beyond what the implementation requires.

### NFR-3: Maintainability
- Reusable test data (sample `CatalogAggregate` instances, `LedgerEntry` mocks) lives in private builder helpers within the test class or a shared `*TestBuilders.cs` file under the same test folder, not duplicated per test.
- Test class name: `SalesCostProviderTests`.
- File location: mirror the `src/` path under `tests/` — `backend/test/Anela.Heblo.Application.Tests/Features/Catalog/CostProviders/SalesCostProviderTests.cs` (verify exact test-project path against the existing solution layout).

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
- `ILedgerService.GetDirectCosts(from, to, costCenter, ct)` → returns a list of cost rows, with a verifiable invocation per cost center.
- `IOptions<DataSourceOptions>` → returns a `DataSourceOptions` with deterministic `ManufactureCostHistoryDays`.
- `ILogger<SalesCostProvider>` → verified via `Verify` on `Log<It.IsAnyType>` for warning/error/info messages.

## Dependencies
- xUnit, FluentAssertions, Moq (or NSubstitute — match the existing test-project convention).
- Existing test project for `Anela.Heblo.Application` under `backend/test/` (path to be confirmed against `Anela.Heblo.sln`).
- No new NuGet packages expected; if `FluentAssertions` or the mocking library is not already referenced by the target test project, the test author MUST add it explicitly rather than introducing a different framework.

## Out of Scope
- Refactoring `SalesCostProvider` itself (no behaviour changes). If a test reveals a latent bug, file a separate issue with a failing test; do not change production code in this PR.
- Integration tests against a real database, real `LedgerService`, or real cache implementation.
- Coverage for other cost providers (`PurchaseCostProvider`, `ManufactureCostProvider`, etc.) — separate gaps, separate spec.
- Tests for `ISalesCostCache` or `ILedgerService` implementations.
- Introducing a clock abstraction (`IClock`/`TimeProvider`) into `SalesCostProvider` purely to make `GetDateRange` deterministic — see Open Questions; if needed, treated as a separate refactor.
- Property-based testing.

## Open Questions
1. **Clock injection for leap-year test (FR-5).** `GetDateRange` currently calls `DateTime.UtcNow` directly, so the leap-year `costsTo.Day == 29` assertion cannot be made deterministically without either (a) introducing `TimeProvider`/`IClock` into the provider, or (b) running the test only during a leap-year February. Recommended assumption: scope the leap-year assertion to a unit test on `DateTime.DaysInMonth` semantics and assert the provider's end-of-month behaviour only via the deterministic month/day arithmetic visible in the `ILedgerService.GetDirectCosts` arguments captured against the real `UtcNow`. Confirm whether a `TimeProvider` refactor is acceptable as part of this work or must be deferred.
2. **Test project path.** The exact path/name of the test project that should host `SalesCostProviderTests.cs` (e.g. `Anela.Heblo.Application.Tests` vs. a feature-scoped project) needs verification against the current solution layout.
3. **Mocking library standard.** Confirm whether this repo standardizes on Moq or NSubstitute. The brief and global rules list both; the test author should match whatever the nearest existing test project under `backend/test/` already uses.

## Status: HAS_QUESTIONS