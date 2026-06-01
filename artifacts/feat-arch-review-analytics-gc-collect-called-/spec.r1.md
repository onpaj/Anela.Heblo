# Specification: Remove Forced GC.Collect() From AnalyticsRepository Streaming Loop

## Summary
Remove the unconditional `GC.Collect()` call inside the batching loop of `AnalyticsRepository.StreamProductsWithSalesAsync` at `backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs:120`. The call forces a blocking, full-generation garbage collection on every batch iteration, adding latency to every margin report request while providing no memory benefit since the underlying data is already fully materialized before the loop starts.

## Background
The Analytics module's `AnalyticsRepository.StreamProductsWithSalesAsync` method paginates an in-memory `List<CatalogAggregate>` in batches of 100 products and yields `AnalyticsProduct` items. At the end of each batch iteration it calls `GC.Collect()` with the comment "Allow garbage collection between batches."

Two problems with this code:

1. **`GC.Collect()` does not "allow" GC — it forces a synchronous, blocking, full-generation (Gen2) collection regardless of memory pressure.** The .NET GC is generational and runs automatically based on allocation pressure; explicit `GC.Collect()` calls in hot paths pause all managed threads on every invocation. Microsoft's official guidance is: *"Do not call GC.Collect except in a few well-defined scenarios — it can actually cause more harm than good in production code."*

2. **The batching itself does not reduce memory.** `allProducts` is fully materialized as a `List<CatalogAggregate>` on line 39 (`var allProducts = await _catalogRepository.GetProductsWithSalesInPeriod(...)`) before the loop begins. Peak memory is already allocated. Skipping/taking from the list and forcing GC between batches does not reduce peak working set; it only adds overhead.

This method fans out into 5 margin-report use cases. For a 500+ product catalog, each report request currently triggers 5+ forced Gen2 collections, each pausing all managed threads. The fix is a one-line removal with no behavioral change apart from removing the spurious pause.

The broader concern — that the list is fully materialized before streaming — is explicitly out of scope here and is being tracked under the separate cross-module boundary issue for `IAnalyticsProductSource` ownership.

## Functional Requirements

### FR-1: Remove the forced GC.Collect() call
Delete line 120 (`GC.Collect();`) and the associated `// Allow garbage collection between batches` comment from `AnalyticsRepository.StreamProductsWithSalesAsync`. The surrounding `for`/`foreach` batching loop must remain unchanged so that existing call sites and the yielded sequence are not affected.

**Acceptance criteria:**
- `GC.Collect()` no longer appears in `backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs`.
- The misleading `// Allow garbage collection between batches` comment is removed along with the call.
- The method signature, return type, parameter list, and yielded `AnalyticsProduct` sequence (order and contents) are unchanged.
- All existing call sites of `StreamProductsWithSalesAsync` continue to compile and behave identically.
- A repository-wide search confirms no other `GC.Collect()` calls were introduced as part of this change.

### FR-2: Preserve batching loop structure unchanged
The batching loop (`for (int i = 0; i < allProducts.Count; i += batchSize)` with the inner `foreach` and `yield return`) is retained as-is. Refactoring the loop, removing the batching, or otherwise altering the streaming logic is **not** part of this change. The single, surgical edit is the removal of the `GC.Collect()` line.

**Acceptance criteria:**
- The `for` loop bounds, batch size constant, `Skip`/`Take` calls, and `yield return` statement are byte-identical to the pre-change implementation aside from the deleted line.
- No new branching, allocations, or LINQ operators are introduced.
- No new constants, fields, or method-level locals are added.

### FR-3: Verify margin report behavior is unchanged
The five margin-report use cases that fan out through `StreamProductsWithSalesAsync` must produce identical outputs after the change. This is a pure performance fix with no functional change.

**Acceptance criteria:**
- Existing unit and integration tests that cover `StreamProductsWithSalesAsync` and the five margin-report use cases pass without modification.
- If no direct unit test exists for `StreamProductsWithSalesAsync`, add a single test that calls the method with a fixed in-memory catalog and asserts the yielded sequence (count, ordering, and identity of products) matches expectations. This guards against accidental refactoring during the cleanup.
- `dotnet build` and `dotnet format` succeed.

## Non-Functional Requirements

### NFR-1: Performance
- Each margin-report request must no longer incur synchronous Gen2 collections forced by this method.
- No measurable regression in throughput or latency for any margin-report endpoint compared to the pre-change baseline.
- The change is expected to **reduce** P95/P99 latency for the affected endpoints, particularly under concurrent load where the forced collections compound across requests. Quantifying the improvement is not required, but reviewers should sanity-check with a single before/after run of one margin-report use case against a non-trivial catalog (≥500 products).

### NFR-2: Security
No security impact. The change does not touch authentication, authorization, input validation, persistence, or any externally exposed surface.

### NFR-3: Maintainability
- The misleading comment must not survive the edit. Leaving a comment about GC behavior next to no code is worse than removing both.
- No replacement comment is added; the absence of `GC.Collect()` is self-documenting.

### NFR-4: Risk
- **Risk level: very low.** The deleted line has no functional effect on yielded data. The only effect is the elimination of forced GC pauses, which is the intent.
- **Rollback:** trivial — revert the single-line change.

## Data Model
No data model changes. No entities, DTOs, EF mappings, or migrations are touched.

## API / Interface Design
No API, contract, or UI changes. The method's public signature, semantics, and yielded sequence are unchanged. No new endpoints, events, MediatR handlers, or controllers are introduced or modified.

## Dependencies
- **Code:** `backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs` (single file).
- **Build:** standard `dotnet build` / `dotnet format`.
- **Tests:** existing Analytics test suite; optionally one new unit test per FR-3.
- No external services, libraries, NuGet package changes, or feature flags involved.
- **Not blocked by** the broader `IAnalyticsProductSource` ownership refactor — this fix can ship independently and the refactor will subsume the surrounding loop later.

## Out of Scope
- Refactoring `StreamProductsWithSalesAsync` to stream from the EF query rather than from a fully materialized list. This is tracked separately under the cross-module boundary issue for `IAnalyticsProductSource` ownership.
- Removing the batching loop or changing the batch size.
- Changes to `_catalogRepository.GetProductsWithSalesInPeriod` or any upstream EF Core query.
- Touching any other `GC.*` calls that may exist elsewhere in the codebase (none are known to exist, and an audit is not part of this task — only this site is in scope).
- Adding performance benchmarks, profiling harnesses, or telemetry around GC behavior.
- Modifying any of the five margin-report use cases that consume this method.

## Open Questions
None.

## Status: COMPLETE