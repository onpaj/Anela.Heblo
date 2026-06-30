# Architecture Review: Remove Explicit GC.Collect() from CatalogAnalyticsSourceAdapter

## Skip Design: true

## Architectural Fit Assessment

This change is a single-line deletion inside an `internal sealed` class that implements `IAnalyticsProductSource`. It touches no public API, no interfaces, no data contracts, and no module boundaries. The class is correctly placed under `Features/Catalog/Infrastructure/` — it is the infrastructure adapter that bridges the Catalog domain to the Analytics module, consistent with the Clean Architecture layering used throughout the project.

The existing test file `CatalogAnalyticsSourceAdapterTests.cs` has 9 unit tests covering all meaningful behaviours of the adapter: type mapping, margin fallback, sales filtering, purchase price selection, and null-product handling. None of these tests reference or depend on `GC.Collect()`. They will all continue to pass after the deletion without modification.

There are no integration points affected. The batch loop structure (`for` + `Skip`/`Take` + `yield return`) is unchanged. Downstream `IAnalyticsProductSource` consumers see identical `IAsyncEnumerable<AnalyticsProduct>` output.

## Proposed Architecture

### Component Overview

```
Analytics handlers
      |
      v
IAnalyticsProductSource (Domain interface)
      |
      v
CatalogAnalyticsSourceAdapter   <-- single-line deletion here (line 36)
      |
      v
ICatalogRepository
```

No new components. No changed relationships.

### Key Design Decisions

#### Decision 1: Deletion only — no structural refactoring

**Options considered:**
- Delete `GC.Collect()` and leave everything else exactly as-is.
- Replace the `Skip`/`Take` batch loop with a true streaming approach that never materialises all products into memory (which would make the original GC concern moot at the root).

**Chosen approach:** Delete line 36 only. Do not restructure the loop.

**Rationale:** The spec explicitly places loop restructuring out of scope, and that is correct. Changing `GetProductsWithSalesInPeriod` to return an `IAsyncEnumerable` instead of a `List` is a meaningful architectural change that touches `ICatalogRepository`, its implementation, and all callers — it belongs in a separate, planned task. The only risk here is the one-line deletion; keeping scope tight minimises the chance of regression and makes the diff trivially reviewable.

#### Decision 2: No replacement memory-management strategy

**Options considered:**
- Delete `GC.Collect()` with no replacement.
- Delete and add `GC.Collect(0, GCCollectionMode.Optimized)` or call `GC.AddMemoryPressure` to hint the runtime.

**Chosen approach:** Delete with no replacement.

**Rationale:** The .NET GC already responds to allocation pressure automatically. The batch objects are short-lived and will be promoted at most to Gen-1 before collection; no explicit hint is needed or beneficial. Adding a softer GC hint would be a premature optimisation with no measurable benefit and would contradict the intent of the fix.

## Implementation Guidance

### Directory / Module Structure

Only one file changes:

```
backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs
```

No new files. No directory changes.

### Interfaces and Contracts

`IAnalyticsProductSource` is unchanged. The method signature of `StreamProductsWithSalesAsync` is unchanged. `AnalyticsProduct` is unchanged. No contracts are affected.

### Data Flow

Before and after the change, the data flow is identical:

1. Caller invokes `StreamProductsWithSalesAsync(fromDate, toDate, productTypes, ct)`.
2. Adapter calls `ICatalogRepository.GetProductsWithSalesInPeriod(...)`, which returns `List<CatalogAggregate>` in memory.
3. Adapter iterates the list in 100-item batches, mapping each `CatalogAggregate` to `AnalyticsProduct` and yielding it.
4. After each batch: previously called `GC.Collect()` — after this change, does nothing (loop continues immediately).

The only runtime difference is that the GC is no longer forced to perform a full-heap blocking collection after each batch.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Regression in streaming output | None | The batch loop and `yield return` are untouched; 9 existing unit tests cover all mapping paths. |
| Memory pressure increase at large product counts | Low | The GC heuristics already handle this correctly; removal restores normal GC behaviour rather than disabling it. Gen-0/1 collections will still occur when needed. |
| Build breakage | None | Deleting a statement cannot introduce a compile error or format violation. |

## Specification Amendments

None required. The spec is complete and accurate. The acceptance criteria (line deleted, loop intact, `dotnet build` clean, `dotnet format` clean, existing tests pass) are sufficient to verify correctness.

## Prerequisites

None. This is a self-contained deletion. No migrations, config changes, or infrastructure work are required before implementation can begin.
