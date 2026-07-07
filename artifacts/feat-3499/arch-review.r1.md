# Architecture Review: Unit Test Coverage for DirectManufactureCostProvider

## Skip Design: true

## Architectural Fit Assessment

This is a pure test-coverage addition against an existing, already-shipped class (`DirectManufactureCostProvider`, part of the M1_B cost-provider stub family). No production code, interfaces, or data models change. The class already conforms to the established "cache-backed cost provider" shape used by its siblings (`FlatManufactureCostProvider`, `ManufactureBasedMaterialCostProvider`), so there is no new architecture to design — only test scaffolding that follows an existing, well-established convention in the same directory. Design review is not warranted; proceed straight to implementation guidance.

## Proposed Architecture

### Component Overview

No new components. The test subject is `Anela.Heblo.Application.Features.Catalog.CostProviders.DirectManufactureCostProvider`, exercised through its two public interface methods (`GetCostsAsync`, `RefreshAsync` from `IDirectManufactureCostProvider`), with dependencies `IDirectManufactureCostCache`, `ICatalogRepository`, `ILogger<DirectManufactureCostProvider>`, and `IOptions<DataSourceOptions>` — all mockable via Moq, all already used the same way in `FlatManufactureCostProviderTests.cs`.

### Key Design Decisions

- **Reuse the sibling test file as the template.** `test/Anela.Heblo.Tests/Features/Catalog/CostProviders/FlatManufactureCostProviderTests.cs` tests `FlatManufactureCostProvider`, a class with an almost line-for-line identical `GetCostsAsync`/`RefreshAsync`/static-`RefreshLock` shape (same cache-hydration check, same warning log, same `FilterByProductCodes` helper). Match its namespace, `[Collection("...")]` usage, and constructor-mock conventions.
- **Mock `IDirectManufactureCostCache` directly** (per spec), rather than instantiating the real `DirectManufactureCostCache` backed by `MemoryCache` as `FlatManufactureCostProviderTests` does for its own cache. This is the right call here: FR-1/FR-2/FR-3 all hinge on controlling exactly what `GetCachedDataAsync` returns (unhydrated vs. hydrated with specific `ProductCosts`), which is far more direct via `Mock<IDirectManufactureCostCache>` than by seeding a real `MemoryCache`.
- **Isolate the static `RefreshLock` semaphore.** Because it's `private static readonly SemaphoreSlim` shared across all instances of the class within the test process, the concurrency test (FR-1) must guarantee release via `try/finally`, and the test class should carry `[Collection("DirectManufactureCostProviderTests")]` (following the `FlatManufactureCostProviderTests` precedent) so xUnit doesn't run it in parallel with itself or unrelated tests that might also touch a `DirectManufactureCostProvider` instance sharing the same static lock.

## Implementation Guidance

### Directory / Module Structure

Add a single new file:

```
backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/DirectManufactureCostProviderTests.cs
```

Namespace: `Anela.Heblo.Tests.Features.Catalog.CostProviders` (matches the sibling files in the same folder — `FlatManufactureCostProviderTests`, `SalesCostProviderTests`, `ManufactureBasedMaterialCostProviderTests`). No new test project, no new folders.

### Interfaces and Contracts

No interface or contract changes. Tests consume the existing public surface:
- `IDirectManufactureCostProvider.GetCostsAsync(productCodes, dateFrom, dateTo, ct)`
- `IDirectManufactureCostProvider.RefreshAsync(ct)`

Mocked collaborators (all existing types, no changes needed):
- `Mock<IDirectManufactureCostCache>` — `GetCachedDataAsync`, `SetCachedDataAsync`
- `Mock<ICatalogRepository>` — `GetAllAsync`, `WaitForCurrentMergeAsync`
- `Mock<ILogger<DirectManufactureCostProvider>>` — verify `LogInformation`/`LogWarning` invocations (Moq's logger-verification pattern: match on the `Log` extension's underlying `ILogger.Log` call, or use a helper if one already exists elsewhere in the test project — check for a shared logger-assertion helper before writing a bespoke one)
- `IOptions<DataSourceOptions>` via `Options.Create(new DataSourceOptions())`

A private `CreateProvider(...)` factory method (mirroring `FlatManufactureCostProviderTests.CreateProvider`) with optional parameters defaulting to `Mock.Of<T>()` keeps the three test groups (FR-1/FR-2/FR-3) concise and consistent with the existing file.

### Data Flow

Standard AAA unit test flow, no infrastructure involved:
1. Arrange mocks for `IDirectManufactureCostCache` / `ICatalogRepository` / logger to produce the specific cache state (hydrated/unhydrated) or blocking behavior (FR-1) needed for the scenario.
2. Act by calling `GetCostsAsync` or `RefreshAsync` on a `DirectManufactureCostProvider` instance built via the factory method.
3. Assert on the returned dictionary, mock invocation counts (`Verify(..., Times.Once)` / `Times.Never)`), and logged messages.

For FR-1 specifically: use a `TaskCompletionSource` to block the first `RefreshAsync` call inside `ComputeAllCostsAsync` (e.g., have the mocked `ICatalogRepository.GetAllAsync` await an uncompleted task) so the second, concurrent `RefreshAsync` call is provably issued while the lock is held — then complete the TCS and await the first call in a `finally` to guarantee `RefreshLock` is released before the test ends, regardless of assertion outcome.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Static `RefreshLock` leaks an acquired state into other tests if FR-1 test throws before releasing | Medium | Wrap the blocking/concurrent call sequence in `try/finally`; add `[Collection("DirectManufactureCostProviderTests")]` to prevent parallel interference, following the exact precedent in `FlatManufactureCostProviderTests` |
| Logger verification via Moq is brittle if message text is asserted with exact-string matching | Low | Match on a partial/contains substring (e.g. "already in progress" / "not hydrated") rather than the full message, so incidental wording changes don't break tests without changing behavior |
| FR-1's TCS-based blocking approach could deadlock if wired incorrectly | Low | Keep the blocked call's mock setup minimal (block only on `GetAllAsync`, not on the semaphore itself) and always await/complete the TCS in a `finally`, mirroring the spec's documented approach |

## Specification Amendments

None. The specification (`spec.r1.md`) is implementation-ready as written; its FR-1/FR-2/FR-3 breakdown, acceptance criteria, and NFRs already reflect the actual code paths in `DirectManufactureCostProvider.cs`.

## Prerequisites

None beyond what already exists in the repo: xUnit + Moq are already referenced by `Anela.Heblo.Tests`, and the sibling `FlatManufactureCostProviderTests.cs` in the same folder provides a directly reusable structural template.
