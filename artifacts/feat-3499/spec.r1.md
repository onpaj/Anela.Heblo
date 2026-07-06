# Specification: Unit Test Coverage for DirectManufactureCostProvider

## Summary
`DirectManufactureCostProvider` (`backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/DirectManufactureCostProvider.cs`) has 0% line coverage against a 60% threshold. This spec defines the unit tests needed to cover three untested behaviors: the `RefreshAsync` concurrency guard, the `GetCostsAsync` unhydrated-cache fallback, and the `FilterByProductCodes` static filter. No production code changes are in scope.

## Background
Direct manufacture costs feed financial dashboards, so silent regressions in the caching/concurrency logic could produce incomplete or stale data without any visible failure. The class currently has no test file backing it, so all three logic branches identified in the coverage-gap report are unverified. This work adds tests only; it does not alter `DirectManufactureCostProvider.cs` or any other production code.

## Functional Requirements

### FR-1: `RefreshAsync` concurrency guard
Verify that when `RefreshAsync` is already in progress (holding the static `RefreshLock` semaphore), a second concurrent call to `RefreshAsync` skips the refresh work and returns without touching the repository or cache.

Test design notes:
- `RefreshLock` is a `private static readonly SemaphoreSlim(1, 1)` shared across all instances of the class. Since it is static, a test that acquires it directly (e.g. via reflection) or that drives a real concurrent call while the first call is still inside `ComputeAllCostsAsync` must release the semaphore afterward (in a `finally`/`try-finally`) so it does not leak into other tests running in the same test process.
- Recommended approach: make the first call's `ICatalogRepository.GetAllAsync` (or `WaitForCurrentMergeAsync`) block on an uncompleted `TaskCompletionSource` so the first `RefreshAsync` call is provably still holding the lock when the second call is issued. Then start the second `RefreshAsync` call, assert it returns promptly, and assert `_catalogRepository`/`_cache` methods were invoked only once (from the first call). Finally, complete the `TaskCompletionSource` and await the first call to let it finish cleanly.

**Acceptance criteria:**
- Given a first call to `RefreshAsync` is in progress (has acquired the lock and not yet released it), when a second call to `RefreshAsync` is made, then the second call returns (does not throw, does not block indefinitely).
- The mocked `ICatalogRepository.GetAllAsync` (and/or `WaitForCurrentMergeAsync`) is invoked exactly once across both calls — i.e., the second, skipped call does not invoke it.
- The mocked `IDirectManufactureCostCache.SetCachedDataAsync` is invoked exactly once across both calls.
- An informational log entry is recorded on the skip path (verify via the mocked `ILogger<DirectManufactureCostProvider>`, e.g. asserting `LogInformation` was called with a message consistent with "refresh already in progress, skipping").
- After the test completes, the static `RefreshLock` is left in a released state (no lingering acquisition) so subsequent tests in the same run are unaffected.

### FR-2: `GetCostsAsync` unhydrated-cache fallback
Verify that when `IDirectManufactureCostCache.GetCachedDataAsync` returns cache data with `IsHydrated == false`, `GetCostsAsync` returns an empty dictionary and logs a warning, without attempting to filter or compute costs.

**Acceptance criteria:**
- Given `_cache.GetCachedDataAsync` returns a `CostCacheData` with `IsHydrated = false`, when `GetCostsAsync` is called (with any combination of `productCodes`, `dateFrom`, `dateTo`, including defaults/nulls), then the returned dictionary is non-null and has `Count == 0`.
- A warning is logged via the mocked `ILogger<DirectManufactureCostProvider>` (verify `LogWarning` invoked with a message consistent with "not hydrated yet").
- `_catalogRepository` methods (`GetAllAsync`, `WaitForCurrentMergeAsync`) are not invoked when taking this path (confirms no cost computation is attempted).

### FR-3: `FilterByProductCodes` behavior (exercised via `GetCostsAsync` with a hydrated cache)
`FilterByProductCodes` is a `private static` method with no direct public entry point, so it must be exercised indirectly through `GetCostsAsync` when `IsHydrated == true`. Set up `_cache.GetCachedDataAsync` to return a hydrated `CostCacheData` with a known `ProductCosts` dictionary containing at least two distinct product codes, then call `GetCostsAsync` with varying `productCodes` arguments.

**Acceptance criteria:**
- Given `productCodes = null`, when `GetCostsAsync` is called, then the returned dictionary contains all entries from the cached `ProductCosts` (same keys and count, values unchanged).
- Given `productCodes = new List<string>()` (empty list), when `GetCostsAsync` is called, then the returned dictionary contains all entries from the cached `ProductCosts` (empty/null list passthrough — no filtering applied).
- Given `productCodes` containing a subset of the cached product codes (e.g. one match, one non-existent code), when `GetCostsAsync` is called, then the returned dictionary contains only the entries whose keys are in `productCodes`, and entries for codes not present in the cache are simply absent (no exception).
- Given `productCodes` containing only codes not present in the cache, when `GetCostsAsync` is called, then the returned dictionary is empty.

## Non-Functional Requirements

### NFR-1: Performance
Tests must run as fast, isolated unit tests (no real delays, no `Thread.Sleep`/real timers). Any concurrency simulation (FR-1) must use deterministic synchronization (e.g. `TaskCompletionSource`) rather than sleep-based timing to avoid flaky tests.

### NFR-2: Security
Not applicable — this class contains no auth or sensitive-data handling; no new security-relevant test scenarios are introduced.

### NFR-3: Test isolation
Because `RefreshLock` is `static`, the `RefreshAsync` concurrency test(s) must not leave the semaphore in an acquired state after the test finishes, and should be written so they are safe to run in parallel with, or in sequence with, other tests in the same test class/assembly (xUnit may run test classes in parallel by default). If the existing test project disables parallelization for this class or assembly, follow the existing convention already used by sibling cost-provider test files, if any.

## Data Model
No data model changes. Relevant existing types used in test setup/mocks:
- `CostCacheData` (`IsHydrated`, `ProductCosts: Dictionary<string, List<MonthlyCost>>`, `LastUpdated`, `DataFrom`, `DataTo`)
- `MonthlyCost` (value object used inside `ProductCosts`)
- `IDirectManufactureCostCache` (mocked: `GetCachedDataAsync`, `SetCachedDataAsync`)
- `ICatalogRepository` (mocked: `GetAllAsync`, `WaitForCurrentMergeAsync`)
- `IOptions<DataSourceOptions>` (mocked/stubbed with a `ManufactureCostHistoryDays` value; only relevant to the hydrated-cache-computation path, not directly asserted on by these new tests but required to construct the SUT)

## API / Interface Design
No API or interface changes. Tests target the existing public methods `GetCostsAsync` and `RefreshAsync` on `IDirectManufactureCostProvider`/`DirectManufactureCostProvider` directly (unit-level, no HTTP/controller involvement).

## Dependencies
- Existing test project/framework already used for the `Anela.Heblo.Application` test suite (xUnit, per repository convention) and its mocking library (e.g. Moq/NSubstitute — match whatever sibling test files in `Features/Catalog` already use).
- No new NuGet packages required.

## Out of Scope
- Any change to `DirectManufactureCostProvider.cs` production logic (it is explicitly a STUB implementation; its constant-15 cost calculation is not under test here).
- Testing `ComputeAllCostsAsync` / `CalculateDirectManufacturingCosts` cost values in detail (the stub math) beyond what FR-1's concurrency test incidentally requires to let the first `RefreshAsync` call complete.
- Integration or E2E tests; this is unit-test-only coverage work.
- Coverage of `IDirectManufactureCostCache` or `ICatalogRepository` implementations themselves — they are mocked.

## Open Questions
None.

## Status: COMPLETE
