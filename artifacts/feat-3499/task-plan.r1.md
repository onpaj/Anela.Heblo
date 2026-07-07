# Task Plan: Unit Test Coverage for DirectManufactureCostProvider

## Overview

Single, small, well-defined task: add a new unit test file covering the three
untested logic branches of `DirectManufactureCostProvider` (concurrency guard,
unhydrated-cache fallback, product-code filtering). No production code
changes. This mirrors the architecture review's guidance to reuse the
structurally-identical sibling test file `FlatManufactureCostProviderTests.cs`
as a template. Given the scope, everything fits in one task.

---

### task: add-direct-manufacture-cost-provider-tests

## Goal

Create `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/DirectManufactureCostProviderTests.cs`
with unit tests covering `DirectManufactureCostProvider`'s three currently
untested behaviors, raising its line coverage above the 60% threshold. Do
**not** modify `DirectManufactureCostProvider.cs` or any other production
code — this is test-only work.

## Files

- **Create:** `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/DirectManufactureCostProviderTests.cs`
- **Mirror/template (read-only reference):** `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/FlatManufactureCostProviderTests.cs`
- **System under test (read-only, do not edit):** `backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/DirectManufactureCostProvider.cs`

## Structural conventions to follow (from the template)

- Namespace: `Anela.Heblo.Tests.Features.Catalog.CostProviders`.
- Class-level attribute: `[Collection("DirectManufactureCostProviderTests")]` — follow the exact precedent set by `FlatManufactureCostProviderTests`'s `[Collection("FlatManufactureCostProviderTests")]`, since both classes share the same *kind* of static `RefreshLock` semaphore pattern and must not run in parallel with themselves.
- Add a private `CreateProvider(...)` factory method mirroring the template's, with optional parameters defaulting to mocks, e.g.:
  ```csharp
  private DirectManufactureCostProvider CreateProvider(
      IDirectManufactureCostCache? cache = null,
      ICatalogRepository? catalogRepository = null,
      ILogger<DirectManufactureCostProvider>? logger = null,
      DataSourceOptions? options = null)
  {
      return new DirectManufactureCostProvider(
          cache ?? Mock.Of<IDirectManufactureCostCache>(),
          catalogRepository ?? Mock.Of<ICatalogRepository>(),
          logger ?? Mock.Of<ILogger<DirectManufactureCostProvider>>(),
          Options.Create(options ?? new DataSourceOptions())
      );
  }
  ```
  Unlike the template, mock `IDirectManufactureCostCache` directly via `Mock<IDirectManufactureCostCache>` (do **not** instantiate a real cache backed by `MemoryCache`) — this is the right call here because the tests need precise control over `GetCachedDataAsync`'s returned `IsHydrated`/`ProductCosts` values.
- Use xUnit + Moq (already referenced by the test project; no new packages).
- For logger assertions, there is no shared logger-verification helper in the test project — each test file defines its own local helper (see `CatalogMergeSchedulerTests.cs`'s private `VerifyLogged` method as an example of the Moq pattern for verifying `ILogger.Log` calls with a level and a message substring). Add a similar small private static helper in the new file rather than exact-string matching, so incidental wording changes don't break the tests.

## Test cases to implement

### FR-1: `RefreshAsync` concurrency guard
- Arrange: use a `TaskCompletionSource` so the mocked `ICatalogRepository.GetAllAsync` (or `WaitForCurrentMergeAsync`) blocks on it, guaranteeing the first `RefreshAsync` call is still holding `RefreshLock` when the second call is issued.
- Act: start the first `RefreshAsync()` call (don't await yet), then call a second `RefreshAsync()` and await it — assert it returns promptly (does not throw, does not block).
- Assert:
  - `ICatalogRepository.GetAllAsync` (and/or `WaitForCurrentMergeAsync`) was invoked exactly once (`Times.Once`) across both calls.
  - `IDirectManufactureCostCache.SetCachedDataAsync` was invoked exactly once.
  - An informational log was recorded on the skip path (message consistent with "refresh already in progress, skipping" — matches the literal string `"DirectManufactureCostCache refresh already in progress, skipping"` in the source).
- **Critical gotcha:** wrap the blocking/first-call sequence in `try/finally` so the `TaskCompletionSource` is always completed and the first `RefreshAsync` call is always awaited to completion before the test method returns — this guarantees the static `RefreshLock` semaphore is released even if an assertion fails, so it doesn't leak an acquired state into other tests running later in the same process. This is the single biggest risk called out in the architecture review; get it right.

### FR-2: `GetCostsAsync` unhydrated-cache fallback
- Arrange: mock `IDirectManufactureCostCache.GetCachedDataAsync` to return a `CostCacheData` with `IsHydrated = false`.
- Act: call `GetCostsAsync` (cover at least the default/null-args case; optionally also with non-null `productCodes`/`dateFrom`/`dateTo` to confirm they don't change the outcome).
- Assert:
  - Returned dictionary is non-null with `Count == 0`.
  - A warning was logged (message consistent with `"DirectManufactureCostCache not hydrated yet"`).
  - `ICatalogRepository.GetAllAsync` and `WaitForCurrentMergeAsync` were never invoked (`Times.Never`) — confirms no cost computation was attempted on this path.

### FR-3: `FilterByProductCodes` behavior (via `GetCostsAsync` with hydrated cache)
- Arrange: mock `IDirectManufactureCostCache.GetCachedDataAsync` to return a hydrated `CostCacheData` (`IsHydrated = true`) with a `ProductCosts` dictionary containing at least two distinct product codes (each mapped to a small `List<MonthlyCost>`).
- Implement as separate `[Fact]`s (or a `[Theory]` if it stays clean) covering:
  1. `productCodes = null` → returned dictionary contains all cached entries (same keys/count, values unchanged).
  2. `productCodes = new List<string>()` (empty) → returned dictionary contains all cached entries (empty list passthrough, no filtering).
  3. `productCodes` with a subset — one matching code + one non-existent code → returned dictionary contains only the matching entry; the non-existent code is simply absent, no exception thrown.
  4. `productCodes` containing only codes not present in the cache → returned dictionary is empty (`Count == 0`).

## Non-functional constraints

- No `Thread.Sleep` or real timers/delays anywhere — FR-1's concurrency simulation must use `TaskCompletionSource` for deterministic synchronization (per spec NFR-1).
- Keep the `[Collection("DirectManufactureCostProviderTests")]` isolation in place regardless of whether other tests currently touch `DirectManufactureCostProvider`'s static lock — it's cheap insurance per spec NFR-3 and the architecture review.

## Out of scope

- No changes to `DirectManufactureCostProvider.cs` (it's an intentional STUB; its constant-15 cost math is not under test here beyond what's incidentally needed to let a `RefreshAsync` call complete in FR-1).
- No detailed testing of `ComputeAllCostsAsync`/`CalculateDirectManufacturingCosts` values.
- No integration/E2E tests.

## Definition of done / verification

1. New test file compiles and all new tests pass in isolation and as part of the full suite.
2. Run the relevant test filter, e.g. from `backend/`:
   ```
   dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~DirectManufactureCostProviderTests
   ```
   Also run the full `Anela.Heblo.Tests` project (or at least the `Features/Catalog/CostProviders` folder) once to confirm no interference with sibling tests (particularly `FlatManufactureCostProviderTests`, given both share the static-semaphore-per-class pattern and collection-based isolation).
3. Run `dotnet format` on the backend solution/project per repo conventions before considering the change complete.
4. Confirm no production file (`DirectManufactureCostProvider.cs` or any other `.cs` outside the test project) was modified.
